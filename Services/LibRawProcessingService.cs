using System.Diagnostics;
using ImageMagick;
using HappyPhoton.Models;
using Sdcb.LibRaw;
using Sdcb.LibRaw.Natives;
using static HappyPhoton.Services.ImageServiceHelpers;

namespace HappyPhoton.Services;

/// <summary>
/// RAW processing service using LibRaw for fast decoding.
/// Falls back gracefully when LibRaw native libraries are not available.
/// </summary>
public class LibRawProcessingService : IRawProcessingService
{
    private const string ServiceName = "LibRaw";

    private readonly bool _isAvailable;

    public LibRawProcessingService()
    {
        _isAvailable = CheckAvailability();
    }

    public bool IsAvailable => _isAvailable;

    private static bool CheckAvailability()
    {
        var available = LibRawNativeSupport.IsAvailable;
        LogDebug(
            ServiceName,
            available
                ? "Native decoder is available"
                : "Native decoder is unavailable; using fallback");
        return available;
    }

    public RawThumbnailData? ExtractThumbnail(string filePath)
    {
        if (!_isAvailable) return null;

        var sw = Stopwatch.StartNew();
        try
        {
            using var ctx = RawContext.OpenFile(filePath);
            var data = RawThumbnailReader.Read(ctx);
            if (data == null)
            {
                LogDebug(ServiceName, "Thumbnail extraction returned no data", filePath);
                return null;
            }

            LogPerformance(ServiceName, "ExtractThumbnail", sw.ElapsedMilliseconds, filePath);
            return new RawThumbnailData(data, ctx.Width, ctx.Height);
        }
        catch (Exception ex)
        {
            LogDebug(ServiceName, $"Thumbnail extraction failed: {ex.Message}", filePath);
            return null;
        }
    }

    public MagickImage? DecodeFull(string filePath)
    {
        if (!_isAvailable) return null;

        var sw = Stopwatch.StartNew();
        var modeName = "DecodeFull";
        try
        {
            using var ctx = RawContext.OpenFile(filePath);

            // Unpack the RAW data
            ctx.Unpack();

            // Process with half-size for faster decode (4x fewer pixels), or full resolution
            ctx.DcrawProcess(c =>
            {
                c.UseCameraWb = true;
                c.OutputBps = 8;
                // Output color space: sRGB (with camera color matrix applied)
                c.OutputColor = (LibRawColorSpace)1;
                // sRGB gamma curve: power 1/2.4, toe slope 12.92
                c.Gamma[0] = 1.0f / 2.4f;
                c.Gamma[1] = 12.92f;
                // Let LibRaw auto-rotate based on EXIF (default behavior)
            });

            using var processed = ctx.MakeDcrawMemoryImage();
            var imageData = processed.AsSpan<byte>().ToArray();
            var width = processed.Width;
            var height = processed.Height;

            LogDebug(ServiceName, $"Decoded: {width}x{height}, {processed.Bits} bits, {imageData.Length} bytes", filePath);

            // LibRaw returns raw RGB pixel data - create PPM with header
            var ppmData = CreatePpmImage(imageData, width, height);
            var image = new MagickImage(ppmData);

            // Apply default tone curve for better contrast (like Lightroom's default rendering)
            ApplyDefaultToneCurve(image);

            // Full decode needs subtle saturation boost to match half-size decode appearance
            // Half-size demosaicing produces slightly more vibrant colors due to 2x2 averaging
            image.Modulate(new Percentage(100), new Percentage(105), new Percentage(100));

            LogPerformance(ServiceName, modeName, sw.ElapsedMilliseconds, filePath);
            return image;
        }
        catch (LibRawException ex)
        {
            LogDebug(ServiceName, $"{modeName} failed: {ex.Message}", filePath);
            return null;
        }
        catch (Exception ex)
        {
            LogDebug(ServiceName, $"Unexpected error decoding: {ex.Message}", filePath);
            return null;
        }
    }

    public RawMetadata? ExtractMetadata(string filePath)
    {
        if (!_isAvailable) return null;

        try
        {
            using var ctx = RawContext.OpenFile(filePath);

            // Access metadata through ImageParams and ImageOtherParams
            var iParams = ctx.ImageParams;
            var other = ctx.ImageOtherParams;

            var metadata = CreateMetadata(
                iParams,
                other,
                ctx.LensInfo,
                ctx.Width,
                ctx.Height);

            LogDebug(ServiceName, $"Metadata: {metadata.CameraMake} {metadata.CameraModel}, ISO {metadata.Iso}", filePath);
            return metadata;
        }
        catch (Exception ex)
        {
            LogDebug(ServiceName, $"Metadata extraction failed: {ex.Message}", filePath);
            return null;
        }
    }

    internal static RawMetadata CreateMetadata(
        LibRawImageParams imageParams,
        LibRawImageOtherParams other,
        LibRawLensInfo lensInfo,
        int pixelWidth,
        int pixelHeight)
    {
        var lens = lensInfo.Lens?.Trim();
        var metadata = new RawMetadata
        {
            CameraMake = imageParams.Make?.Trim(),
            CameraModel = imageParams.Model?.Trim(),
            PixelWidth = pixelWidth,
            PixelHeight = pixelHeight,
            Iso = other.IsoSpeed > 0 ? (int)other.IsoSpeed : null,
            FNumber = other.Aperture > 0 ? other.Aperture : null,
            ExposureTime = other.Shutter > 0 ? other.Shutter : null,
            FocalLength = other.FocalLength > 0 ? other.FocalLength : null,
            FocalLengthIn35mmFilm = lensInfo.FocalLengthIn35mmFormat > 0
                ? lensInfo.FocalLengthIn35mmFormat
                : null,
            LensModel = string.IsNullOrEmpty(lens) ? null : lens
        };

        if (other.Timestamp > 0)
        {
            // LibRaw parses the camera's local capture time via mktime, so
            // converting back must also use the machine-local zone.
            metadata.DateTaken = DateTimeOffset
                .FromUnixTimeSeconds(other.Timestamp)
                .LocalDateTime;
        }

        ApplyGps(metadata, other.ParsedGPS);
        return metadata;
    }

    private static void ApplyGps(RawMetadata metadata, LibRawGPS gps)
    {
        if (gps.GPSParsed == 0) return;

        // A parsed GPS block can still be partial (no fix, altitude-only).
        // The reference bytes are only set for fields the camera wrote, so
        // they gate presence; never fabricate a 0,0 or 0 m value.
        var latitude = ToCoordinate(
            gps.LatitudeDegrees,
            gps.LatitudeMinutes,
            gps.LatitudeSeconds,
            gps.LatitudeReference,
            (byte)'N',
            (byte)'S');
        var longitude = ToCoordinate(
            gps.LongitudeDegrees,
            gps.LongitudeMinutes,
            gps.LongitudeSeconds,
            gps.LongitudeReference,
            (byte)'E',
            (byte)'W');
        if (latitude is >= -90 and <= 90 && longitude is >= -180 and <= 180)
        {
            metadata.GpsLatitude = latitude;
            metadata.GpsLongitude = longitude;
        }

        if (float.IsFinite(gps.Altitude) &&
            (metadata.GpsLatitude.HasValue || gps.Altitude != 0))
        {
            metadata.GpsAltitude = gps.AltitudeReference == 1
                ? -Math.Abs(gps.Altitude)
                : gps.Altitude;
        }
    }

    private static double? ToCoordinate(
        float degrees,
        float minutes,
        float seconds,
        byte reference,
        byte positiveReference,
        byte negativeReference)
    {
        if (reference != positiveReference && reference != negativeReference)
        {
            return null;
        }

        if (!float.IsFinite(degrees) ||
            !float.IsFinite(minutes) ||
            !float.IsFinite(seconds))
        {
            return null;
        }

        var value = Math.Abs(degrees) +
            Math.Abs(minutes) / 60.0 +
            Math.Abs(seconds) / 3600.0;
        return reference == negativeReference ? -value : value;
    }

    /// <summary>
    /// Applies a subtle S-curve tone curve to give RAW images more contrast and "punch",
    /// similar to Lightroom's default camera profile rendering.
    /// </summary>
    private static void ApplyDefaultToneCurve(MagickImage image)
    {
        // Build a lookup table for a Lightroom-like tone curve
        // Combines: shadow lift (toe), S-curve contrast, highlight rolloff (shoulder)
        var lut = new byte[256];

        // Tunable parameters
        const double shadowLift = 0.012;    // Subtle black lift for "film" look
        const double contrast = 0.10;       // S-curve strength (adds actual contrast)
        const double highlightRolloff = 0.03; // Soft shoulder to protect highlights

        for (int i = 0; i < 256; i++)
        {
            double x = i / 255.0;

            // 1. Shadow lift (toe): lifts blacks slightly, tapers toward midtones
            //    Uses (1-x)^3 to concentrate effect in shadows
            double toe = shadowLift * Math.Pow(1 - x, 3);

            // 2. S-curve contrast: sin(2πx) creates proper S-shape
            //    - Negative at x<0.5 (darkens shadows)
            //    - Positive at x>0.5 (brightens highlights)
            //    Multiplied by 4*x*(1-x) to taper at endpoints (preserve black/white)
            double sCurve = -contrast * Math.Sin(2 * Math.PI * x) * (4 * x * (1 - x));

            // 3. Highlight rolloff (shoulder): gentle compression in highlights
            //    Uses x^3 to concentrate effect in bright areas
            double shoulder = -highlightRolloff * Math.Pow(x, 3);

            // Combine all components
            double y = x + toe + sCurve + shoulder;

            lut[i] = (byte)Math.Clamp((int)(y * 255), 0, 255);
        }

        // Create a 256x1 CLUT image
        using var clutImage = new MagickImage(MagickColors.Black, 256, 1);
        using var clutPixels = clutImage.GetPixels();

        for (int i = 0; i < 256; i++)
        {
            var value16 = (ushort)(lut[i] * 257);
            clutPixels.SetPixel(i, 0, new ushort[] { value16, value16, value16 });
        }

        // Apply the CLUT
        image.Clut(clutImage, PixelInterpolateMethod.Bilinear, Channels.RGB);
    }

    private static byte[] CreatePpmImage(byte[] rgbData, int width, int height)
    {
        var header = $"P6\n{width} {height}\n255\n";
        var headerBytes = System.Text.Encoding.ASCII.GetBytes(header);

        var ppmData = new byte[headerBytes.Length + rgbData.Length];
        Array.Copy(headerBytes, 0, ppmData, 0, headerBytes.Length);
        Array.Copy(rgbData, 0, ppmData, headerBytes.Length, rgbData.Length);

        return ppmData;
    }
}
