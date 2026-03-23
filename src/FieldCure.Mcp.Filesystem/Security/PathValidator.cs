using System.Runtime.InteropServices;

namespace FieldCure.Mcp.Filesystem.Security;

/// <summary>
/// Validates and resolves file paths against a set of allowed directories.
/// Implements sandboxing to prevent access outside permitted boundaries.
/// </summary>
public sealed partial class PathValidator : IPathValidator
{
    private static readonly bool IsWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
    private static readonly StringComparison PathComparison =
        IsWindows ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private static readonly HashSet<string> WindowsReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM0", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT0", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    private readonly ReaderWriterLockSlim _lock = new();
    private List<string> _allowedDirectories;

    public PathValidator(IEnumerable<string> allowedDirectories)
    {
        _allowedDirectories = [.. allowedDirectories
            .Select(d => NormalizePath(Path.GetFullPath(d)))];
    }

    public IReadOnlyList<string> AllowedDirectories
    {
        get
        {
            _lock.EnterReadLock();
            try
            {
                return _allowedDirectories;
            }
            finally
            {
                _lock.ExitReadLock();
            }
        }
    }

    /// <inheritdoc />
    public void UpdateDirectories(IEnumerable<string> directories)
    {
        var newDirs = directories
            .Select(d => NormalizePath(Path.GetFullPath(d)))
            .ToList();

        _lock.EnterWriteLock();
        try
        {
            _allowedDirectories = newDirs;
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    public string ValidateAndResolve(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (IsWindows)
        {
            ValidateWindowsSpecific(path);
        }

        // Resolve to absolute path, normalizing . and ..
        var fullPath = Path.GetFullPath(path);
        fullPath = NormalizePath(fullPath);

        // If path exists, resolve symlinks to get the real path
        string resolvedPath;
        if (Path.Exists(fullPath))
        {
            resolvedPath = ResolveSymlinks(fullPath);
        }
        else
        {
            // For non-existent paths (write_file, create_directory),
            // walk up to the nearest existing parent and validate that
            resolvedPath = ResolveNonExistentPath(fullPath);
        }

        _lock.EnterReadLock();
        try
        {
            if (!IsPathUnderAllowed(resolvedPath))
            {
                throw new UnauthorizedAccessException(
                    $"Access denied: path '{path}' resolves to '{resolvedPath}' which is outside all allowed directories.");
            }
        }
        finally
        {
            _lock.ExitReadLock();
        }

        return resolvedPath;
    }

    /// <summary>
    /// Validates Windows-specific path restrictions (NTFS ADS and reserved names).
    /// </summary>
    private static void ValidateWindowsSpecific(string path)
    {
        // Block NTFS Alternate Data Streams (e.g., file.txt:hidden)
        // Allow drive letter colon (C:) but block ADS colons
        var withoutDrive = path.Length >= 2 && path[1] == ':'
            ? path[2..]
            : path;

        if (withoutDrive.Contains(':'))
        {
            throw new ArgumentException(
                $"NTFS Alternate Data Streams are not permitted: '{path}'");
        }

        // Block Windows reserved names
        var fileName = Path.GetFileNameWithoutExtension(path);
        if (!string.IsNullOrEmpty(fileName) && WindowsReservedNames.Contains(fileName))
        {
            throw new ArgumentException(
                $"Windows reserved name is not permitted: '{fileName}'");
        }
    }

    /// <summary>
    /// Checks whether the candidate path falls under any allowed directory.
    /// Must be called while holding the read lock.
    /// </summary>
    private bool IsPathUnderAllowed(string candidatePath)
    {
        foreach (var allowed in _allowedDirectories)
        {
            // Exact match: the path IS the allowed directory
            if (candidatePath.Equals(allowed, PathComparison))
                return true;

            // Prefix match with directory separator to prevent
            // /allowed matching /allowed-secret
            var prefix = allowed.EndsWith(Path.DirectorySeparatorChar)
                ? allowed
                : allowed + Path.DirectorySeparatorChar;

            if (candidatePath.StartsWith(prefix, PathComparison))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Resolves symlinks to their final target using .NET 8+ APIs.
    /// </summary>
    private static string ResolveSymlinks(string path)
    {
        try
        {
            var linkTarget = File.ResolveLinkTarget(path, returnFinalTarget: true);
            if (linkTarget is not null)
                return NormalizePath(linkTarget.FullName);

            // Not a symlink — also check if it's a directory symlink
            if (Directory.Exists(path))
            {
                var dirLinkTarget = Directory.ResolveLinkTarget(path, returnFinalTarget: true);
                if (dirLinkTarget is not null)
                    return NormalizePath(dirLinkTarget.FullName);
            }
        }
        catch (IOException)
        {
            // Not a symlink or cannot resolve — use original path
        }

        return NormalizePath(path);
    }

    /// <summary>
    /// For non-existent paths, walks up to the nearest existing ancestor,
    /// resolves any symlinks on that ancestor, then reconstructs the full path.
    /// </summary>
    private static string ResolveNonExistentPath(string fullPath)
    {
        var current = fullPath;

        while (!string.IsNullOrEmpty(current))
        {
            var parent = Path.GetDirectoryName(current);

            if (parent is null)
                break;

            if (Path.Exists(parent))
            {
                var resolvedParent = ResolveSymlinks(parent);
                var remaining = Path.GetRelativePath(parent, fullPath);
                return NormalizePath(Path.GetFullPath(Path.Combine(resolvedParent, remaining)));
            }

            current = parent;
        }

        // If we can't find any existing ancestor, return the original
        return fullPath;
    }

    /// <summary>
    /// Normalizes a path by unifying separators, removing trailing separators,
    /// and capitalizing Windows drive letters.
    /// </summary>
    private static string NormalizePath(string path)
    {
        // Normalize directory separators
        path = path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);

        // Remove trailing separator (unless root like C:\ or /)
        if (path.Length > 1 && path.EndsWith(Path.DirectorySeparatorChar))
        {
            // Keep root paths intact: C:\ on Windows, / on Unix
            var isRoot = IsWindows
                ? path.Length == 3 && path[1] == ':'
                : path.Length == 1;

            if (!isRoot)
                path = path.TrimEnd(Path.DirectorySeparatorChar);
        }

        // Capitalize drive letter on Windows for consistency
        if (IsWindows && path.Length >= 2 && path[1] == ':' && char.IsLower(path[0]))
        {
            path = char.ToUpperInvariant(path[0]) + path[1..];
        }

        return path;
    }
}
