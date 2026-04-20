using System.ComponentModel;
using System.Text;
using FieldCure.Mcp.Filesystem.Security;
using FieldCure.Mcp.Filesystem.Utilities;
using ModelContextProtocol.Server;

namespace FieldCure.Mcp.Filesystem.Tools;

/// <summary>
/// Provides directory listing, creation, and tree operations as MCP tools.
/// </summary>
[McpServerToolType]
public static class DirectoryTools
{
    /// <summary>
    /// Lists the immediate contents of the given directory with type, size,
    /// and last-modified markers suitable for MCP tool output.
    /// </summary>
    /// <param name="validator">Injected path validator that enforces the sandbox.</param>
    /// <param name="path">Directory path to list.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>A newline-delimited listing, or a placeholder for an empty directory.</returns>
    [McpServerTool(ReadOnly = true, Destructive = false, Idempotent = true), Description(
        "List the contents of a directory. " +
        "Returns entries with [FILE] or [DIR] markers, size, and last modified date.")]
    public static Task<string> ListDirectory(
        IPathValidator validator,
        [Description("Path of the directory to list")]
        string path,
        CancellationToken cancellationToken)
    {
        var resolvedPath = validator.ValidateAndResolve(path);

        if (!Directory.Exists(resolvedPath))
            throw new DirectoryNotFoundException($"Directory not found: {path}");

        var entries = new DirectoryInfo(resolvedPath).GetFileSystemInfos();
        var sb = new StringBuilder();

        foreach (var entry in entries.OrderBy(e => e is not DirectoryInfo).ThenBy(e => e.Name))
        {
            if (entry is DirectoryInfo dir)
            {
                sb.AppendLine($"[DIR]  {dir.Name,-40} {dir.LastWriteTime:yyyy-MM-dd HH:mm}");
            }
            else if (entry is FileInfo file)
            {
                sb.AppendLine($"[FILE] {file.Name,-40} {FileSize.Format(file.Length),10} {file.LastWriteTime:yyyy-MM-dd HH:mm}");
            }
        }

        return Task.FromResult(sb.Length > 0 ? sb.ToString().TrimEnd() : "(empty directory)");
    }

    /// <summary>
    /// Creates the specified directory and any missing parent directories.
    /// </summary>
    /// <param name="validator">Injected path validator that enforces the sandbox.</param>
    /// <param name="path">Directory path to create.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>A success message referencing the created directory path.</returns>
    [McpServerTool(Destructive = false, ReadOnly = false, Idempotent = true), Description(
        "Create a new directory. Creates parent directories recursively if they don't exist.")]
    public static Task<string> CreateDirectory(
        IPathValidator validator,
        [Description("Path of the directory to create")]
        string path,
        CancellationToken cancellationToken)
    {
        var resolvedPath = validator.ValidateAndResolve(path);
        Directory.CreateDirectory(resolvedPath);
        return Task.FromResult($"Directory created: {path}");
    }

    /// <summary>
    /// Builds a hierarchical tree view of the directory structure up to the
    /// specified depth, using Unicode connectors for readability.
    /// </summary>
    /// <param name="validator">Injected path validator that enforces the sandbox.</param>
    /// <param name="path">Root directory to render.</param>
    /// <param name="depth">Maximum depth to traverse.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>An indented text representation of the directory tree.</returns>
    [McpServerTool(ReadOnly = true, Destructive = false, Idempotent = true), Description(
        "Get a hierarchical tree view of a directory structure. " +
        "Returns an indented text representation with files and subdirectories.")]
    public static Task<string> DirectoryTree(
        IPathValidator validator,
        [Description("Root path to generate the tree from")]
        string path,
        [Description("Maximum depth to traverse (default: 3)")]
        int depth = 3,
        CancellationToken cancellationToken = default)
    {
        var resolvedPath = validator.ValidateAndResolve(path);

        if (!Directory.Exists(resolvedPath))
            throw new DirectoryNotFoundException($"Directory not found: {path}");

        var sb = new StringBuilder();
        sb.AppendLine(Path.GetFileName(resolvedPath) + "/");
        BuildTree(resolvedPath, sb, "", depth, 0);
        return Task.FromResult(sb.ToString().TrimEnd());
    }

    /// <summary>
    /// Recursively builds an indented tree representation of the directory structure.
    /// </summary>
    private static void BuildTree(string dirPath, StringBuilder sb, string indent, int maxDepth, int currentDepth)
    {
        if (currentDepth >= maxDepth)
            return;

        try
        {
            var dirInfo = new DirectoryInfo(dirPath);
            var entries = dirInfo.GetFileSystemInfos()
                .OrderBy(e => e is not DirectoryInfo)
                .ThenBy(e => e.Name)
                .ToList();

            for (var i = 0; i < entries.Count; i++)
            {
                var isLast = i == entries.Count - 1;
                var connector = isLast ? "└── " : "├── ";
                var childIndent = indent + (isLast ? "    " : "│   ");

                if (entries[i] is DirectoryInfo subDir)
                {
                    sb.AppendLine($"{indent}{connector}{subDir.Name}/");
                    BuildTree(subDir.FullName, sb, childIndent, maxDepth, currentDepth + 1);
                }
                else
                {
                    sb.AppendLine($"{indent}{connector}{entries[i].Name}");
                }
            }
        }
        catch (UnauthorizedAccessException)
        {
            sb.AppendLine($"{indent}└── [access denied]");
        }
    }

}
