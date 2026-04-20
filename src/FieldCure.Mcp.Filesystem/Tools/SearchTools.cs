using System.ComponentModel;
using System.Text;
using FieldCure.Mcp.Filesystem.Security;
using FieldCure.Mcp.Filesystem.Utilities;
using ModelContextProtocol.Server;

namespace FieldCure.Mcp.Filesystem.Tools;

/// <summary>
/// Provides file search, content search, and file info operations as MCP tools.
/// </summary>
[McpServerToolType]
public static class SearchTools
{
    /// <summary>
    /// Recursively searches for files whose names match the given glob pattern
    /// under the specified base directory. Each hit is re-validated through
    /// the sandbox so symlinks pointing outside allowed directories are skipped.
    /// </summary>
    /// <param name="validator">Injected path validator that enforces the sandbox.</param>
    /// <param name="path">Base directory to search from.</param>
    /// <param name="pattern">Glob pattern matched against file names (case-insensitive).</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>Newline-delimited relative paths, or a "no match" placeholder.</returns>
    [McpServerTool(ReadOnly = true, Destructive = false, Idempotent = true), Description(
        "Search for files by name pattern within a directory tree. " +
        "Supports glob patterns like '*.cs', '*.txt', 'Program.*'. " +
        "Returns a list of matching file paths.")]
    public static Task<string> SearchFiles(
        IPathValidator validator,
        [Description("Base directory to search in")]
        string path,
        [Description("Glob pattern to match file names (e.g., '*.cs', 'readme*')")]
        string pattern,
        CancellationToken cancellationToken)
    {
        var resolvedPath = validator.ValidateAndResolve(path);

        if (!Directory.Exists(resolvedPath))
            throw new DirectoryNotFoundException($"Directory not found: {path}");

        var matches = Directory.EnumerateFiles(resolvedPath, pattern, new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            MatchCasing = MatchCasing.CaseInsensitive,
        });

        var results = new List<string>();
        foreach (var match in matches)
        {
            // Validate each result to ensure it's within allowed directories
            // (symlinks inside the search tree might point outside)
            try
            {
                validator.ValidateAndResolve(match);
                results.Add(Path.GetRelativePath(resolvedPath, match));
            }
            catch (UnauthorizedAccessException)
            {
                // Skip files that resolve outside allowed directories
            }
        }

        return Task.FromResult(results.Count > 0
            ? string.Join("\n", results)
            : "No matching files found.");
    }

    /// <summary>
    /// Searches for a text substring within files under the given base directory.
    /// Binary files are skipped automatically; matches are returned with the
    /// relative path and 1-based line number prefix.
    /// </summary>
    /// <param name="validator">Injected path validator that enforces the sandbox.</param>
    /// <param name="path">Base directory to search from.</param>
    /// <param name="substring">Case-insensitive substring to search for.</param>
    /// <param name="depth">Maximum recursion depth.</param>
    /// <param name="maxResults">Maximum number of matching lines to return.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>Newline-delimited match lines, optionally with a truncation notice.</returns>
    [McpServerTool(ReadOnly = true, Destructive = false, Idempotent = true), Description(
        "Search for text content within files in a directory. " +
        "Returns matching lines with file paths and line numbers. " +
        "Skips binary files automatically.")]
    public static async Task<string> SearchWithinFiles(
        IPathValidator validator,
        [Description("Base directory to search in")]
        string path,
        [Description("Text substring to search for (case-insensitive)")]
        string substring,
        [Description("Maximum directory depth to search (default: 10)")]
        int depth = 10,
        [Description("Maximum number of results to return (default: 100)")]
        int maxResults = 100,
        CancellationToken cancellationToken = default)
    {
        var resolvedPath = validator.ValidateAndResolve(path);

        if (!Directory.Exists(resolvedPath))
            throw new DirectoryNotFoundException($"Directory not found: {path}");

        var sb = new StringBuilder();
        var resultCount = 0;

        var files = Directory.EnumerateFiles(resolvedPath, "*", new EnumerationOptions
        {
            RecurseSubdirectories = true,
            MaxRecursionDepth = depth,
            IgnoreInaccessible = true,
        });

        foreach (var file in files)
        {
            if (cancellationToken.IsCancellationRequested || resultCount >= maxResults)
                break;

            try
            {
                validator.ValidateAndResolve(file);
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }

            // Skip binary files
            if (await Utilities.EncodingDetector.IsBinaryAsync(file, cancellationToken))
                continue;

            var relativePath = Path.GetRelativePath(resolvedPath, file);
            var lineNumber = 0;

            await foreach (var line in File.ReadLinesAsync(file, cancellationToken))
            {
                lineNumber++;

                if (line.Contains(substring, StringComparison.OrdinalIgnoreCase))
                {
                    sb.AppendLine($"{relativePath}:{lineNumber}: {line.TrimStart()}");
                    resultCount++;

                    if (resultCount >= maxResults)
                    {
                        sb.AppendLine($"\n(truncated at {maxResults} results)");
                        break;
                    }
                }
            }
        }

        return sb.Length > 0 ? sb.ToString().TrimEnd() : "No matches found.";
    }

    /// <summary>
    /// Returns a human-readable metadata summary for a file or directory,
    /// including size, timestamps, attributes, and type-specific detail.
    /// </summary>
    /// <param name="validator">Injected path validator that enforces the sandbox.</param>
    /// <param name="path">Path of the file or directory to inspect.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>A multi-line metadata report.</returns>
    [McpServerTool(ReadOnly = true, Destructive = false, Idempotent = true), Description(
        "Get detailed metadata about a file or directory. " +
        "Returns size, creation date, modification date, attributes, and more.")]
    public static Task<string> GetFileInfo(
        IPathValidator validator,
        [Description("Path to the file or directory")]
        string path,
        CancellationToken cancellationToken)
    {
        var resolvedPath = validator.ValidateAndResolve(path);

        if (Directory.Exists(resolvedPath))
        {
            var dir = new DirectoryInfo(resolvedPath);
            var childCount = dir.GetFileSystemInfos().Length;

            return Task.FromResult(
                $"Type: Directory\n" +
                $"Name: {dir.Name}\n" +
                $"Full Path: {dir.FullName}\n" +
                $"Created: {dir.CreationTime:yyyy-MM-dd HH:mm:ss}\n" +
                $"Modified: {dir.LastWriteTime:yyyy-MM-dd HH:mm:ss}\n" +
                $"Attributes: {dir.Attributes}\n" +
                $"Items: {childCount}");
        }

        if (File.Exists(resolvedPath))
        {
            var file = new FileInfo(resolvedPath);

            return Task.FromResult(
                $"Type: File\n" +
                $"Name: {file.Name}\n" +
                $"Full Path: {file.FullName}\n" +
                $"Size: {file.Length} bytes ({FileSize.Format(file.Length)})\n" +
                $"Created: {file.CreationTime:yyyy-MM-dd HH:mm:ss}\n" +
                $"Modified: {file.LastWriteTime:yyyy-MM-dd HH:mm:ss}\n" +
                $"Accessed: {file.LastAccessTime:yyyy-MM-dd HH:mm:ss}\n" +
                $"Attributes: {file.Attributes}\n" +
                $"Extension: {file.Extension}\n" +
                $"Read Only: {file.IsReadOnly}");
        }

        throw new FileNotFoundException($"Path not found: {path}");
    }

}
