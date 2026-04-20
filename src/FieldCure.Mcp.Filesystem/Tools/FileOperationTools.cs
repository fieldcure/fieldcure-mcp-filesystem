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
    /// <summary>
    /// Reads a file's complete contents. Supported document formats are parsed
    /// into text; binary files are returned as base64; plain text is returned
    /// as UTF-8.
    /// </summary>
    /// <param name="validator">Injected path validator that enforces the sandbox.</param>
    /// <param name="path">Path of the file to read.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>Extracted text, base64 for binary files, or decoded UTF-8 for text.</returns>
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

        var parsed = await TryExtractDocumentTextAsync(resolvedPath, cancellationToken);
        if (parsed is not null)
            return parsed;

        if (await EncodingDetector.IsBinaryAsync(resolvedPath, cancellationToken))
        {
            var bytes = await File.ReadAllBytesAsync(resolvedPath, cancellationToken);
            return $"[binary file, {bytes.Length} bytes]\n{Convert.ToBase64String(bytes)}";
        }

        return await File.ReadAllTextAsync(resolvedPath, Encoding.UTF8, cancellationToken);
    }

    /// <summary>
    /// Reads multiple files in parallel, concatenating results with a header
    /// per file. Individual read failures are reported inline and do not abort
    /// the overall operation.
    /// </summary>
    /// <param name="validator">Injected path validator that enforces the sandbox.</param>
    /// <param name="paths">File paths to read.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>Concatenated per-file content blocks separated by headers.</returns>
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

    /// <summary>
    /// Creates or overwrites a file with the given text content using an
    /// atomic temp-file-then-rename write. Missing parent directories are
    /// created automatically.
    /// </summary>
    /// <param name="validator">Injected path validator that enforces the sandbox.</param>
    /// <param name="path">Destination file path.</param>
    /// <param name="content">Text content to write (UTF-8).</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>A message describing the number of characters written.</returns>
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

        await WriteTextAtomicallyAsync(resolvedPath, content, cancellationToken);

        return $"Successfully wrote {content.Length} characters to {path}";
    }

    /// <summary>
    /// Performs find-and-replace on a file's contents using either plain-text
    /// or regular-expression matching. Writes are atomic and the original file
    /// is left untouched if no replacements occur.
    /// </summary>
    /// <param name="validator">Injected path validator that enforces the sandbox.</param>
    /// <param name="path">Path of the file to modify.</param>
    /// <param name="find">Text or regex pattern to search for.</param>
    /// <param name="replace">Replacement text.</param>
    /// <param name="allOccurrences">Whether to replace every match instead of just the first.</param>
    /// <param name="regex">Whether <paramref name="find"/> is a regular expression.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>A message describing how many replacements were made.</returns>
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

    /// <summary>
    /// Copies a file or entire directory subtree to a new location within the
    /// sandbox. Missing destination parent directories are created automatically.
    /// </summary>
    /// <param name="validator">Injected path validator that enforces the sandbox.</param>
    /// <param name="source">Source file or directory path.</param>
    /// <param name="destination">Destination path.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>A message describing the copy outcome.</returns>
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

    /// <summary>
    /// Moves or renames a file or directory within the sandbox. Both source
    /// and destination paths are validated before the operation.
    /// </summary>
    /// <param name="validator">Injected path validator that enforces the sandbox.</param>
    /// <param name="source">Current path.</param>
    /// <param name="destination">New path.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>A message describing the move outcome.</returns>
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

    /// <summary>
    /// Deletes a file or directory. Non-empty directories require the
    /// <paramref name="recursive"/> flag; non-existent paths raise
    /// <see cref="FileNotFoundException"/>.
    /// </summary>
    /// <param name="validator">Injected path validator that enforces the sandbox.</param>
    /// <param name="path">Path to delete.</param>
    /// <param name="recursive">Whether to recurse into non-empty directories.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>A message describing what was deleted.</returns>
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

    /// <summary>
    /// Appends text to the end of a file, creating it if necessary. When
    /// <paramref name="newline"/> is true, a leading newline is inserted if
    /// the existing content does not already end with one.
    /// </summary>
    /// <param name="validator">Injected path validator that enforces the sandbox.</param>
    /// <param name="path">File path to append to.</param>
    /// <param name="content">Text content to append.</param>
    /// <param name="newline">Whether to ensure a newline separates existing and appended content.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>A message describing the number of characters appended.</returns>
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

    /// <summary>
    /// Reads a specific 1-based inclusive range of lines from a text file.
    /// Binary files are rejected and the output is capped at 10,000 lines.
    /// </summary>
    /// <param name="validator">Injected path validator that enforces the sandbox.</param>
    /// <param name="path">Text file path.</param>
    /// <param name="startLine">First line to read (1-based, inclusive).</param>
    /// <param name="endLine">Last line to read (inclusive); 0 means read to end.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>A formatted listing with line numbers, total line count, and a truncation notice when applicable.</returns>
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
    /// Parses a supported document file (DOCX, HWPX, XLSX, PPTX, PDF, etc.)
    /// into markdown and writes the result to disk atomically. The markdown
    /// text is not echoed back through MCP — only a summary with sizes — so
    /// this is far more token-efficient than <c>read_file</c> plus
    /// <c>write_file</c>.
    /// </summary>
    /// <param name="validator">Injected path validator that enforces the sandbox.</param>
    /// <param name="path">Source document path.</param>
    /// <param name="output_path">Optional markdown output path; defaults to the source filename with a .md extension.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>A summary describing the source, destination, and size of the generated markdown.</returns>
    [McpServerTool(ReadOnly = false, Destructive = false, Idempotent = true), Description(
        "RECOMMENDED for converting a supported document file to markdown for LLM processing. " +
        "Supports DocumentParsers formats such as DOCX, HWPX, XLSX, PPTX, and PDF. " +
        "Writes the converted markdown directly to disk to avoid sending the full content through MCP context.")]
    public static async Task<string> ConvertToMarkdown(
        IPathValidator validator,
        [Description("Source document path")]
        string path,
        [Description("Optional output markdown path. Defaults to the same filename with a .md extension.")]
        string? output_path = null,
        CancellationToken cancellationToken = default)
    {
        var resolvedPath = validator.ValidateAndResolve(path);

        if (!File.Exists(resolvedPath))
            throw new FileNotFoundException($"File not found: {path}");

        var markdown = await TryExtractDocumentTextAsync(resolvedPath, cancellationToken);
        if (markdown is null)
            throw new InvalidOperationException(
                $"Unsupported document format: {Path.GetExtension(resolvedPath)}. " +
                "Supported formats are those recognized by DocumentParsers.");

        var resolvedOutputPath = ResolveMarkdownOutputPath(validator, resolvedPath, output_path);
        var outputDirectory = Path.GetDirectoryName(resolvedOutputPath);
        if (!string.IsNullOrEmpty(outputDirectory))
            Directory.CreateDirectory(outputDirectory);

        await WriteTextAtomicallyAsync(resolvedOutputPath, markdown, cancellationToken);

        var inputSize = new FileInfo(resolvedPath).Length;
        var outputSize = new FileInfo(resolvedOutputPath).Length;

        return
            $"Converted to markdown\n" +
            $"Source: {resolvedPath}\n" +
            $"Output: {resolvedOutputPath}\n" +
            $"Input Size: {inputSize} bytes ({FileSize.Format(inputSize)})\n" +
            $"Output Size: {outputSize} bytes ({FileSize.Format(outputSize)})";
    }

    /// <summary>
    /// Batch variant of <see cref="ConvertToMarkdown"/>. Iterates files matching
    /// <paramref name="pattern"/> under the source directory, converts each to
    /// markdown, and reports per-file success, failure, or skip for unsupported
    /// extensions. Individual failures do not abort the batch.
    /// </summary>
    /// <param name="validator">Injected path validator that enforces the sandbox.</param>
    /// <param name="directory_path">Source directory.</param>
    /// <param name="output_directory">Optional destination directory; defaults to the source directory.</param>
    /// <param name="pattern">File-name glob to filter candidates. Unsupported extensions are still skipped.</param>
    /// <param name="recursive">Whether to include subdirectories.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>A summary with success / failure / skip counts and a per-file outcome list.</returns>
    [McpServerTool(ReadOnly = false, Destructive = false, Idempotent = true), Description(
        "RECOMMENDED for batch-converting supported document files in a directory to markdown for LLM processing. " +
        "Converts each file directly on disk and reports per-file success or failure without stopping the batch.")]
    public static async Task<string> ConvertDirectoryToMarkdown(
        IPathValidator validator,
        [Description("Source directory path")]
        string directory_path,
        [Description("Optional output directory. Defaults to the source directory.")]
        string? output_directory = null,
        [Description("Glob pattern to filter file names. Defaults to '*' and still only converts supported document formats.")]
        string pattern = "*",
        [Description("Whether to include subdirectories. Defaults to false.")]
        bool recursive = false,
        CancellationToken cancellationToken = default)
    {
        var resolvedDirectory = validator.ValidateAndResolve(directory_path);
        if (!Directory.Exists(resolvedDirectory))
            throw new DirectoryNotFoundException($"Directory not found: {directory_path}");

        var resolvedOutputDirectory = output_directory is null
            ? resolvedDirectory
            : validator.ValidateAndResolve(output_directory);

        Directory.CreateDirectory(resolvedOutputDirectory);

        var matches = Directory.EnumerateFiles(
            resolvedDirectory,
            pattern,
            new EnumerationOptions
            {
                RecurseSubdirectories = recursive,
                IgnoreInaccessible = true,
                MatchCasing = MatchCasing.CaseInsensitive,
            });

        var results = new List<string>();
        var successCount = 0;
        var failureCount = 0;
        var skippedCount = 0;

        foreach (var match in matches)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string resolvedInput;
            try
            {
                resolvedInput = validator.ValidateAndResolve(match);
            }
            catch (UnauthorizedAccessException)
            {
                failureCount++;
                results.Add($"ERROR | {match} | Access denied by sandbox");
                continue;
            }

            if (await TryExtractDocumentTextAsync(resolvedInput, cancellationToken) is not { } markdown)
            {
                skippedCount++;
                continue;
            }

            var relativePath = Path.GetRelativePath(resolvedDirectory, resolvedInput);

            try
            {
                var outputPath = Path.ChangeExtension(Path.Combine(resolvedOutputDirectory, relativePath), ".md");
                var resolvedOutput = validator.ValidateAndResolve(outputPath);
                var outputDir = Path.GetDirectoryName(resolvedOutput);
                if (!string.IsNullOrEmpty(outputDir))
                    Directory.CreateDirectory(outputDir);

                await WriteTextAtomicallyAsync(resolvedOutput, markdown, cancellationToken);
                successCount++;
                results.Add($"OK    | {relativePath} -> {Path.GetRelativePath(resolvedDirectory, resolvedOutput)}");
            }
            catch (Exception ex)
            {
                failureCount++;
                results.Add($"ERROR | {relativePath} | {ex.Message}");
            }
        }

        var summary = new StringBuilder();
        summary.AppendLine($"Converted {successCount} file(s) to markdown.");
        summary.AppendLine($"Failed: {failureCount}");
        summary.AppendLine($"Skipped unsupported: {skippedCount}");

        if (results.Count > 0)
        {
            summary.AppendLine();
            foreach (var line in results)
                summary.AppendLine(line);
        }

        return summary.ToString().TrimEnd();
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

    /// <summary>
    /// Attempts to extract text from a file using a matching DocumentParsers
    /// parser. Returns <see langword="null"/> when no parser is registered for
    /// the file's extension.
    /// </summary>
    /// <param name="resolvedPath">Validated file path.</param>
    /// <param name="cancellationToken">Cancellation token for the read.</param>
    /// <returns>Extracted text, or <see langword="null"/> when unsupported.</returns>
    private static async Task<string?> TryExtractDocumentTextAsync(string resolvedPath, CancellationToken cancellationToken)
    {
        var parser = DocumentParserFactory.GetParser(Path.GetExtension(resolvedPath));
        if (parser is null)
            return null;

        var bytes = await File.ReadAllBytesAsync(resolvedPath, cancellationToken);
        return parser.ExtractText(bytes);
    }

    /// <summary>
    /// Resolves the markdown output path for a conversion. When the caller
    /// omits <paramref name="requestedOutputPath"/>, the input filename with
    /// a <c>.md</c> extension is used. The final path is re-validated through
    /// the sandbox.
    /// </summary>
    /// <param name="validator">Path validator used to enforce the sandbox.</param>
    /// <param name="resolvedInputPath">Already-validated source path.</param>
    /// <param name="requestedOutputPath">Optional caller-supplied output path.</param>
    /// <returns>A validated absolute markdown output path.</returns>
    private static string ResolveMarkdownOutputPath(
        IPathValidator validator,
        string resolvedInputPath,
        string? requestedOutputPath)
    {
        var candidate = string.IsNullOrWhiteSpace(requestedOutputPath)
            ? Path.ChangeExtension(resolvedInputPath, ".md")
            : requestedOutputPath;

        return validator.ValidateAndResolve(candidate);
    }

    /// <summary>
    /// Writes text to <paramref name="resolvedPath"/> atomically by staging it
    /// in a temp file and then renaming. Best-effort deletes the temp file if
    /// the rename fails.
    /// </summary>
    /// <param name="resolvedPath">Already-validated destination path.</param>
    /// <param name="content">Text content to write (UTF-8).</param>
    /// <param name="cancellationToken">Cancellation token for the write.</param>
    private static async Task WriteTextAtomicallyAsync(string resolvedPath, string content, CancellationToken cancellationToken)
    {
        var tempPath = $"{resolvedPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            await File.WriteAllTextAsync(tempPath, content, Encoding.UTF8, cancellationToken);
            File.Move(tempPath, resolvedPath, overwrite: true);
        }
        catch
        {
            try { File.Delete(tempPath); } catch { /* best effort */ }
            throw;
        }
    }
}
