using System.ComponentModel;
using System.Text;
using System.Text.RegularExpressions;
using FieldCure.DocumentParsers;
using FieldCure.Mcp.Filesystem.Security;
using FieldCure.Mcp.Filesystem.Utilities;
using ModelContextProtocol.Server;

namespace FieldCure.Mcp.Filesystem.Tools;

/// <summary>
/// Provides file read/write/modify/copy/move operations as MCP tools.
/// All operations are sandboxed to allowed directories via IPathValidator.
/// </summary>
[McpServerToolType]
public static class FileOperationTools
{
    [McpServerTool(ReadOnly = true, Destructive = false, Idempotent = true), Description(
        "Read the complete contents of a file. " +
        "Returns text content directly for text files, or base64-encoded string for binary files. " +
        "Use this to examine existing files.")]
    public static async Task<string> ReadFile(
        IPathValidator validator,
        [Description("Absolute or relative path to the file to read")]
        string path,
        CancellationToken cancellationToken)
    {
        var resolvedPath = validator.ValidateAndResolve(path);

        if (!File.Exists(resolvedPath))
            throw new FileNotFoundException($"File not found: {path}");

        // Use DocumentParsers for supported document formats (hwpx, docx, etc.)
        var ext = Path.GetExtension(resolvedPath);
        var parser = DocumentParserFactory.GetParser(ext);
        if (parser is not null)
        {
            var bytes = await File.ReadAllBytesAsync(resolvedPath, cancellationToken);
            return parser.ExtractText(bytes);
        }

        if (await EncodingDetector.IsBinaryAsync(resolvedPath, cancellationToken))
        {
            var bytes = await File.ReadAllBytesAsync(resolvedPath, cancellationToken);
            return $"[binary file, {bytes.Length} bytes]\n{Convert.ToBase64String(bytes)}";
        }

        return await File.ReadAllTextAsync(resolvedPath, Encoding.UTF8, cancellationToken);
    }

    [McpServerTool(ReadOnly = true, Destructive = false, Idempotent = true), Description(
        "Read multiple files simultaneously. " +
        "Returns the contents of each file with a header separator. " +
        "Failed reads are reported inline without stopping other files.")]
    public static async Task<string> ReadMultipleFiles(
        IPathValidator validator,
        [Description("Array of file paths to read")]
        string[] paths,
        CancellationToken cancellationToken)
    {
        var tasks = paths.Select(async p =>
        {
            try
            {
                var content = await ReadFile(validator, p, cancellationToken);
                return $"=== {p} ===\n{content}";
            }
            catch (Exception ex)
            {
                return $"=== {p} ===\nError: {ex.Message}";
            }
        });

        var results = await Task.WhenAll(tasks);
        return string.Join("\n\n", results);
    }

    [McpServerTool(Destructive = true, ReadOnly = false, Idempotent = true), Description(
        "Create a new file or overwrite an existing file with the given content. " +
        "Parent directories are created automatically if they don't exist. " +
        "Uses atomic write (temp file + rename) for safety.")]
    public static async Task<string> WriteFile(
        IPathValidator validator,
        [Description("Path where the file should be written")]
        string path,
        [Description("Text content to write to the file")]
        string content,
        CancellationToken cancellationToken)
    {
        var resolvedPath = validator.ValidateAndResolve(path);

        // Ensure parent directory exists
        var dir = Path.GetDirectoryName(resolvedPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        // Atomic write: write to temp file, then rename
        var tempPath = $"{resolvedPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            await File.WriteAllTextAsync(tempPath, content, Encoding.UTF8, cancellationToken);
            File.Move(tempPath, resolvedPath, overwrite: true);
        }
        catch
        {
            // Clean up temp file on failure
            try { File.Delete(tempPath); } catch { /* best effort */ }
            throw;
        }

        return $"Successfully wrote {content.Length} characters to {path}";
    }

    [McpServerTool(Destructive = true, ReadOnly = false, Idempotent = true), Description(
        "Search and replace text within a file. " +
        "Supports plain text and regular expression patterns. " +
        "Returns the number of replacements made.")]
    public static async Task<string> ModifyFile(
        IPathValidator validator,
        [Description("Path to the file to modify")]
        string path,
        [Description("Text or regex pattern to search for")]
        string find,
        [Description("Replacement text")]
        string replace,
        [Description("Replace all occurrences (default: true)")]
        bool allOccurrences = true,
        [Description("Treat 'find' as a regular expression (default: false)")]
        bool regex = false,
        CancellationToken cancellationToken = default)
    {
        var resolvedPath = validator.ValidateAndResolve(path);

        if (!File.Exists(resolvedPath))
            throw new FileNotFoundException($"File not found: {path}");

        var original = await File.ReadAllTextAsync(resolvedPath, Encoding.UTF8, cancellationToken);
        string modified;
        int count;

        if (regex)
        {
            var rx = new Regex(find, RegexOptions.None, TimeSpan.FromSeconds(5));
            count = rx.Matches(original).Count;
            modified = allOccurrences
                ? rx.Replace(original, replace)
                : rx.Replace(original, replace, 1);
            if (!allOccurrences && count > 0) count = 1;
        }
        else
        {
            count = CountOccurrences(original, find);
            if (allOccurrences)
            {
                modified = original.Replace(find, replace);
            }
            else
            {
                var idx = original.IndexOf(find, StringComparison.Ordinal);
                if (idx >= 0)
                {
                    modified = string.Concat(original.AsSpan(0, idx), replace, original.AsSpan(idx + find.Length));
                    count = 1;
                }
                else
                {
                    modified = original;
                    count = 0;
                }
            }
        }

        if (count == 0)
            return "No matches found. File was not modified.";

        // Atomic write
        var tempPath = $"{resolvedPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            await File.WriteAllTextAsync(tempPath, modified, Encoding.UTF8, cancellationToken);
            File.Move(tempPath, resolvedPath, overwrite: true);
        }
        catch
        {
            try { File.Delete(tempPath); } catch { /* best effort */ }
            throw;
        }

        return $"Made {count} replacement(s) in {path}";
    }

    [McpServerTool(Destructive = false, ReadOnly = false, Idempotent = true), Description(
        "Copy a file or directory to a new location. " +
        "For directories, copies recursively including all contents.")]
    public static Task<string> CopyFile(
        IPathValidator validator,
        [Description("Source file or directory path")]
        string source,
        [Description("Destination path")]
        string destination,
        CancellationToken cancellationToken = default)
    {
        var resolvedSource = validator.ValidateAndResolve(source);
        var resolvedDest = validator.ValidateAndResolve(destination);

        if (Directory.Exists(resolvedSource))
        {
            CopyDirectoryRecursive(resolvedSource, resolvedDest);
            return Task.FromResult($"Directory copied: {source} → {destination}");
        }

        if (!File.Exists(resolvedSource))
            throw new FileNotFoundException($"Source not found: {source}");

        var destDir = Path.GetDirectoryName(resolvedDest);
        if (!string.IsNullOrEmpty(destDir))
            Directory.CreateDirectory(destDir);

        File.Copy(resolvedSource, resolvedDest, overwrite: true);
        return Task.FromResult($"File copied: {source} → {destination}");
    }

    [McpServerTool(Destructive = true, ReadOnly = false, Idempotent = false), Description(
        "Move or rename a file or directory. " +
        "Both source and destination must be within allowed directories.")]
    public static Task<string> MoveFile(
        IPathValidator validator,
        [Description("Current path of the file or directory")]
        string source,
        [Description("New path for the file or directory")]
        string destination,
        CancellationToken cancellationToken = default)
    {
        var resolvedSource = validator.ValidateAndResolve(source);
        var resolvedDest = validator.ValidateAndResolve(destination);

        if (Directory.Exists(resolvedSource))
        {
            Directory.Move(resolvedSource, resolvedDest);
            return Task.FromResult($"Directory moved: {source} → {destination}");
        }

        if (!File.Exists(resolvedSource))
            throw new FileNotFoundException($"Source not found: {source}");

        var destDir = Path.GetDirectoryName(resolvedDest);
        if (!string.IsNullOrEmpty(destDir))
            Directory.CreateDirectory(destDir);

        File.Move(resolvedSource, resolvedDest, overwrite: true);
        return Task.FromResult($"File moved: {source} → {destination}");
    }

    [McpServerTool(Destructive = true, ReadOnly = false, Idempotent = true), Description(
        "Delete a file or directory. " +
        "For non-empty directories, set recursive to true. " +
        "This is a destructive operation and cannot be undone.")]
    public static Task<string> DeleteFile(
        IPathValidator validator,
        [Description("Path to the file or directory to delete")]
        string path,
        [Description("Delete directory contents recursively (default: false). Required for non-empty directories.")]
        bool recursive = false,
        CancellationToken cancellationToken = default)
    {
        var resolvedPath = validator.ValidateAndResolve(path);

        if (Directory.Exists(resolvedPath))
        {
            if (!recursive && Directory.EnumerateFileSystemEntries(resolvedPath).Any())
                throw new IOException($"Directory is not empty: {path}. Set recursive=true to delete.");

            Directory.Delete(resolvedPath, recursive);
            return Task.FromResult(recursive
                ? $"Deleted directory (recursive): {path}"
                : $"Deleted directory: {path}");
        }

        if (!File.Exists(resolvedPath))
            throw new FileNotFoundException($"File or directory not found: {path}");

        File.Delete(resolvedPath);
        return Task.FromResult($"Deleted file: {path}");
    }

    [McpServerTool(Destructive = false, ReadOnly = false, Idempotent = false), Description(
        "Append content to the end of a file. " +
        "Creates the file if it doesn't exist. " +
        "By default, ensures a newline separates existing and appended content.")]
    public static async Task<string> AppendFile(
        IPathValidator validator,
        [Description("Path to the file to append to")]
        string path,
        [Description("Text content to append")]
        string content,
        [Description("Prepend a newline if the file doesn't end with one (default: true)")]
        bool newline = true,
        CancellationToken cancellationToken = default)
    {
        var resolvedPath = validator.ValidateAndResolve(path);

        // Ensure parent directory exists
        var dir = Path.GetDirectoryName(resolvedPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        // If file exists and newline is requested, check if it ends with a newline
        if (newline && File.Exists(resolvedPath))
        {
            var existing = await File.ReadAllTextAsync(resolvedPath, Encoding.UTF8, cancellationToken);
            if (existing.Length > 0 && !existing.EndsWith('\n'))
                content = "\n" + content;
        }

        await File.AppendAllTextAsync(resolvedPath, content, Encoding.UTF8, cancellationToken);

        return $"Appended {content.Length} characters to {path}";
    }

    [McpServerTool(ReadOnly = true, Destructive = false, Idempotent = true), Description(
        "Read a specific range of lines from a text file. " +
        "Uses 1-based line numbering. Returns the requested lines with total line count. " +
        "Useful for reading portions of large files without loading the entire content.")]
    public static async Task<string> ReadFileLines(
        IPathValidator validator,
        [Description("Path to the file to read")]
        string path,
        [Description("First line to read (1-based, inclusive)")]
        int startLine,
        [Description("Last line to read (1-based, inclusive). Omit or 0 to read to end of file.")]
        int endLine = 0,
        CancellationToken cancellationToken = default)
    {
        var resolvedPath = validator.ValidateAndResolve(path);

        if (!File.Exists(resolvedPath))
            throw new FileNotFoundException($"File not found: {path}");

        if (startLine < 1)
            throw new ArgumentOutOfRangeException(nameof(startLine), "startLine must be >= 1.");

        if (await EncodingDetector.IsBinaryAsync(resolvedPath, cancellationToken))
            throw new InvalidOperationException("Cannot read lines from a binary file.");

        var allLines = await File.ReadAllLinesAsync(resolvedPath, Encoding.UTF8, cancellationToken);
        var totalLines = allLines.Length;

        if (startLine > totalLines)
            throw new ArgumentOutOfRangeException(nameof(startLine),
                $"startLine ({startLine}) exceeds total lines ({totalLines}).");

        var effectiveEnd = endLine > 0 ? Math.Min(endLine, totalLines) : totalLines;

        if (effectiveEnd < startLine)
            throw new ArgumentOutOfRangeException(nameof(endLine),
                $"endLine ({endLine}) must be >= startLine ({startLine}).");

        // Cap at 10,000 lines
        const int maxLines = 10_000;
        var requestedCount = effectiveEnd - startLine + 1;
        var truncated = false;
        if (requestedCount > maxLines)
        {
            effectiveEnd = startLine + maxLines - 1;
            truncated = true;
        }

        var lines = allLines[(startLine - 1)..effectiveEnd];
        var sb = new StringBuilder();
        sb.AppendLine($"Lines {startLine}-{effectiveEnd} of {totalLines} total in {path}:");

        for (var i = 0; i < lines.Length; i++)
            sb.AppendLine($"{startLine + i,6} | {lines[i]}");

        if (truncated)
            sb.AppendLine($"[WARNING: Output truncated at {maxLines} lines. Request a smaller range.]");

        return sb.ToString();
    }

    /// <summary>
    /// Counts the number of non-overlapping occurrences of <paramref name="pattern"/> in <paramref name="text"/>.
    /// </summary>
    private static int CountOccurrences(string text, string pattern)
    {
        var count = 0;
        var idx = 0;
        while ((idx = text.IndexOf(pattern, idx, StringComparison.Ordinal)) != -1)
        {
            count++;
            idx += pattern.Length;
        }
        return count;
    }

    /// <summary>
    /// Recursively copies all files and subdirectories from <paramref name="sourceDir"/> to <paramref name="destDir"/>.
    /// </summary>
    private static void CopyDirectoryRecursive(string sourceDir, string destDir)
    {
        Directory.CreateDirectory(destDir);

        foreach (var file in Directory.GetFiles(sourceDir))
        {
            var destFile = Path.Combine(destDir, Path.GetFileName(file));
            File.Copy(file, destFile, overwrite: true);
        }

        foreach (var dir in Directory.GetDirectories(sourceDir))
        {
            var destSubDir = Path.Combine(destDir, Path.GetFileName(dir));
            CopyDirectoryRecursive(dir, destSubDir);
        }
    }
}
