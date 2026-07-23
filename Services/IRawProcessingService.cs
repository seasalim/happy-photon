using ImageMagick;
using HappyPhoton.Models;

namespace HappyPhoton.Services;

/// <summary>
/// Interface for RAW image processing services.
/// Enables platform-specific implementations (LibRaw on Windows/Linux, Magick.NET fallback on macOS).
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
    /// Decodes the RAW file at half resolution for faster preview generation.
    /// Typically 4x faster than full decode.
    /// </summary>
    /// <param name="filePath">Path to the RAW file</param>
    /// <returns>MagickImage at half resolution or null if decode fails</returns>
    MagickImage? DecodeHalfSize(string filePath);

    /// <summary>
    /// Decodes the RAW file at full resolution.
    /// </summary>
    /// <param name="filePath">Path to the RAW file</param>
    /// <returns>MagickImage at full resolution or null if decode fails</returns>
    MagickImage? DecodeFull(string filePath);

    /// <summary>
    /// Extracts metadata from a RAW file.
    /// </summary>
    /// <param name="filePath">Path to the RAW file</param>
    /// <returns>Metadata or null if extraction fails</returns>
    RawMetadata? ExtractMetadata(string filePath);
}
