using HappyPhoton.Models;

namespace HappyPhoton.Services;

/// <summary>
/// Interface for RAW image processing services.
/// Enables native LibRaw processing with a Magick.NET fallback.
/// </summary>
public interface IRawProcessingService
{
    /// <summary>
    /// Whether this RAW processing service is available on the current platform.
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Extracts the embedded thumbnail/preview JPEG from a RAW file.
    /// This is much faster than full decoding.
    /// </summary>
    /// <param name="filePath">Path to the RAW file</param>
    /// <returns>JPEG bytes or null if extraction fails</returns>
    byte[]? ExtractThumbnail(string filePath);

    /// <summary>
    /// Extracts metadata from a RAW file.
    /// </summary>
    /// <param name="filePath">Path to the RAW file</param>
    /// <returns>Metadata or null if extraction fails</returns>
    RawMetadata? ExtractMetadata(string filePath);
}
