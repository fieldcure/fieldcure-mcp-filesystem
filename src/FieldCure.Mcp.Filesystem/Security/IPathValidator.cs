namespace FieldCure.Mcp.Filesystem.Security;

/// <summary>
/// Validates and resolves file paths against allowed directories.
/// Prevents directory traversal, symlink escapes, and access outside sandbox.
/// </summary>
public interface IPathValidator
{
    /// <summary>
    /// Resolves the given path to a full, canonical path and validates
    /// that it falls within an allowed directory.
    /// </summary>
    /// <param name="path">The raw path from the tool invocation.</param>
    /// <returns>The resolved, validated absolute path.</returns>
    /// <exception cref="UnauthorizedAccessException">
    /// Thrown when the resolved path is outside all allowed directories.
    /// </exception>
    string ValidateAndResolve(string path);

    /// <summary>
    /// Returns the list of currently allowed directories.
    /// </summary>
    IReadOnlyList<string> AllowedDirectories { get; }

    /// <summary>
    /// Updates the allowed directories at runtime.
    /// Called when the client sends a roots/list_changed notification.
    /// </summary>
    void UpdateDirectories(IEnumerable<string> directories);
}
