using System.Text;

namespace FieldCure.Mcp.Filesystem.Utilities;

/// <summary>
/// Detects whether a file is text or binary, and identifies its encoding via BOM.
/// </summary>
public static class EncodingDetector
{
    private const int SampleSize = 8192;

    /// <summary>
    /// Determines whether the file at the given path is a binary file.
    /// Reads up to the first 8KB and checks for NUL bytes.
    /// </summary>
    public static async Task<bool> IsBinaryAsync(string path, CancellationToken cancellationToken = default)
    {
        var buffer = new byte[SampleSize];
        int bytesRead;

        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, SampleSize, useAsync: true);
        bytesRead = await stream.ReadAsync(buffer.AsMemory(0, SampleSize), cancellationToken);

        for (var i = 0; i < bytesRead; i++)
        {
            if (buffer[i] == 0)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Detects the encoding of a file by examining its BOM (Byte Order Mark).
    /// Falls back to UTF-8 if no BOM is found.
    /// </summary>
    public static Encoding DetectEncoding(byte[] bom)
    {
        if (bom.Length >= 3 && bom[0] == 0xEF && bom[1] == 0xBB && bom[2] == 0xBF)
            return Encoding.UTF8;

        if (bom.Length >= 2 && bom[0] == 0xFF && bom[1] == 0xFE)
        {
            if (bom.Length >= 4 && bom[2] == 0x00 && bom[3] == 0x00)
                return Encoding.UTF32; // UTF-32 LE

            return Encoding.Unicode; // UTF-16 LE
        }

        if (bom.Length >= 2 && bom[0] == 0xFE && bom[1] == 0xFF)
            return Encoding.BigEndianUnicode; // UTF-16 BE

        return Encoding.UTF8; // Default
    }
}
