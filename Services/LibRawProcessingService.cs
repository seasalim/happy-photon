using System.Diagnostics;
using HappyPhoton.LibRaw.Interop;
using HappyPhoton.Models;
using ImageMagick;
using static HappyPhoton.Services.ImageServiceHelpers;

namespace HappyPhoton.Services;

/// <summary>
/// RAW processing service using the pinned Happy Photon LibRaw bridge.
/// Falls back gracefully when the native runtime is not available.
/// </summary>
public class LibRawProcessingService : IRawProcessingService
{
    private const string ServiceName = "LibRaw";
    private readonly bool _isAvailable;

    public LibRawProcessingService()
        : this(LibRawNativeSupport.Health)
    {
    }

    internal LibRawProcessingService(LibRawRuntimeHealth health)
    {
        ArgumentNullException.ThrowIfNull(health);
        _isAvailable = health.IsHealthy;
        if (_isAvailable)
        {
            LogDebug(ServiceName, "Native decoder is available");
        }
    }

    public bool IsAvailable => _isAvailable;

    public RawThumbnailData? ExtractThumbnail(string filePath)
    {
        if (!_isAvailable) return null;
        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var context = LibRawContext.Open(filePath);
            var dimensions = context.GetDimensions();
            var data = RawThumbnailReader.Read(context);
            if (data == null)
            {
                LogDebug(ServiceName, "Thumbnail extraction returned no data", filePath);
                return null;
            }

            LogPerformance(ServiceName, nameof(ExtractThumbnail),
                stopwatch.ElapsedMilliseconds, filePath);
            return new RawThumbnailData(data,
                checked((int)dimensions.VisibleWidth),
                checked((int)dimensions.VisibleHeight));
        }
        catch (Exception exception)
        {
            LogDebug(ServiceName, $"Thumbnail extraction failed: {exception.Message}", filePath);
            return null;
        }
    }

    public MagickImage? DecodeFull(string filePath)
    {
        if (!_isAvailable) return null;
        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var context = LibRawContext.Open(filePath);
            context.Unpack();
            context.ConfigureOutput(LibRawOutputConfiguration.FullDecodeSrgb());
            context.Process();
            using var processed = context.MakeProcessedImage();
            var shape = processed.Description;
            if (shape.BitsPerSample != 8 || shape.Channels != 3 ||
                shape.Width == 0 || shape.Height == 0) return null;

            using var pixels = RawBaseLoader.ImportRgb8(
                processed.AsSpan(), checked((int)shape.Width), checked((int)shape.Height));
            var image = (MagickImage)pixels.Clone();
            LogPerformance(ServiceName, nameof(DecodeFull),
                stopwatch.ElapsedMilliseconds, filePath);
            return image;
        }
        catch (Exception exception)
        {
            LogDebug(ServiceName, $"DecodeFull failed: {exception.Message}", filePath);
            return null;
        }
    }

    public RawMetadata? ExtractMetadata(string filePath)
    {
        if (!_isAvailable) return null;
        try
        {
            using var context = LibRawContext.Open(filePath);
            var result = CreateMetadata(context.GetMetadata(), context.GetDimensions());
            LogDebug(ServiceName,
                $"Metadata: {result.CameraMake} {result.CameraModel}, ISO {result.Iso}",
                filePath);
            return result;
        }
        catch (Exception exception)
        {
            LogDebug(ServiceName, $"Metadata extraction failed: {exception.Message}", filePath);
            return null;
        }
    }

    internal static RawMetadata CreateMetadata(
        LibRawMetadata source,
        LibRawDimensions dimensions)
    {
        var result = new RawMetadata
        {
            CameraMake = source.Make?.Trim(),
            CameraModel = source.Model?.Trim(),
            PixelWidth = checked((int)dimensions.VisibleWidth),
            PixelHeight = checked((int)dimensions.VisibleHeight),
            Iso = source.Iso is > 0 ? checked((int)source.Iso.Value) : null,
            FNumber = source.Aperture is > 0 ? source.Aperture : null,
            ExposureTime = source.Shutter is > 0 ? source.Shutter : null,
            FocalLength = source.FocalLength is > 0 ? source.FocalLength : null,
            FocalLengthIn35mmFilm = source.FocalLength35mm is > 0
                ? source.FocalLength35mm
                : null,
            LensModel = string.IsNullOrWhiteSpace(source.Lens) ? null : source.Lens.Trim(),
            GpsLatitude = source.Gps.Latitude,
            GpsLongitude = source.Gps.Longitude,
            GpsAltitude = source.Gps.Altitude
        };
        if (source.Timestamp is > 0)
        {
            result.DateTaken = DateTimeOffset
                .FromUnixTimeSeconds(source.Timestamp.Value)
                .LocalDateTime;
        }
        return result;
    }
}
