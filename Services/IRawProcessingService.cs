using HappyPhoton.Models;

namespace HappyPhoton.Services;

/// <summary>
/// Extracts RAW metadata and encoded previews through the pinned LibRaw runtime.
/// </summary>
public interface IRawProcessingService
{
    /// <summary>
    /// Whether this RAW processing service is available on the current platform.
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Extracts encoded preview bytes from a RAW file.
    /// This is much faster than full decoding.
    /// </summary>
    /// <param name="filePath">Path to the RAW file</param>
    /// <returns>Encoded preview data or null if extraction fails</returns>
    RawThumbnailData? ExtractThumbnail(string filePath);

    /// <summary>
    /// Extracts metadata from a RAW file.
    /// </summary>
    /// <param name="filePath">Path to the RAW file</param>
    /// <returns>Metadata or null if extraction fails</returns>
    RawMetadata? ExtractMetadata(string filePath);
}
