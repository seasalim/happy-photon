using System.Diagnostics;
using ImageMagick;
using HappyPhoton.Models;
using static HappyPhoton.Services.ImageServiceHelpers;

namespace HappyPhoton.Services;

/// <summary>
/// RAW processing service using Magick.NET as fallback for platforms without LibRaw native packages.
/// Used on macOS or when LibRaw fails to load.
/// </summary>
public class MagickNetRawService : IRawProcessingService
{
    private const string ServiceName = "MagickNet";

    /// <summary>
    /// MagickNet fallback is always available as it has no native dependencies beyond what's bundled.
    /// </summary>
    public bool IsAvailable => true;

    public byte[]? ExtractThumbnail(string filePath)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            // Use Ping to read metadata only (faster than full load)
            using var image = new MagickImage();
            image.Ping(filePath);

            // Try to get EXIF thumbnail
            var exifProfile = image.GetExifProfile();
            if (exifProfile != null)
            {
                var thumbnail = exifProfile.CreateThumbnail();
                if (thumbnail != null)
                {
                    using (thumbnail)
                    {
                        var data = thumbnail.ToByteArray(MagickFormat.Jpeg);
                        LogPerformance(ServiceName, "ExtractThumbnail", sw.ElapsedMilliseconds, filePath, "source=exif");
                        return data;
                    }
                }
            }

            LogDebug(ServiceName, "No EXIF thumbnail found", filePath);
            return null;
        }
        catch (Exception ex)
        {
            LogDebug(ServiceName, $"Thumbnail extraction failed: {ex.Message}", filePath);
            return null;
        }
    }

    public RawMetadata? ExtractMetadata(string filePath)
    {
        // MagickNet fallback doesn't support reliable RAW metadata extraction
        return null;
    }
}
