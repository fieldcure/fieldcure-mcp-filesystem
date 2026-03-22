using System.ComponentModel;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
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
    [McpServerTool, Description(
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

        if (await EncodingDetector.IsBinaryAsync(resolvedPath, cancellationToken))
        {
            var bytes = await File.ReadAllBytesAsync(resolvedPath, cancellationToken);
            return $"[binary file, {bytes.Length} bytes]\n{Convert.ToBase64String(bytes)}";
        }

        return await File.ReadAllTextAsync(resolvedPath, Encoding.UTF8, cancellationToken);
    }

    [McpServerTool, Description(
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

    [McpServerTool, Description(
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

    [McpServerTool, Description(
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

    [McpServerTool, Description(
        "Copy a file or directory to a new location. " +
        "For directories, copies recursively including all contents.")]
    public static Task<string> CopyFile(
        IPathValidator validator,
        [Description("Source file or directory path")]
        string source,
        [Description("Destination path")]
        string destination,
        CancellationToken cancellationToken)
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

    [McpServerTool, Description(
        "Move or rename a file or directory. " +
        "Both source and destination must be within allowed directories.")]
    public static Task<string> MoveFile(
        IPathValidator validator,
        [Description("Current path of the file or directory")]
        string source,
        [Description("New path for the file or directory")]
        string destination,
        CancellationToken cancellationToken)
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
