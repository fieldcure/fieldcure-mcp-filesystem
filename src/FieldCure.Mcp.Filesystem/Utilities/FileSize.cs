namespace FieldCure.Mcp.Filesystem.Utilities;

/// <summary>
/// Provides human-readable file size formatting.
/// </summary>
internal static class FileSize
{
    /// <summary>
    /// Formats a byte count into a human-readable string (e.g., "1.5 KB", "3.2 MB").
    /// </summary>
    internal static string Format(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
        < 1024 * 1024 * 1024 => $"{bytes / (1024.0 * 1024):F1} MB",
        _ => $"{bytes / (1024.0 * 1024 * 1024):F1} GB",
    };
}
