using System.Diagnostics;
using System.Runtime.InteropServices;
using ImageMagick;
using HappyPhoton.Models;
using Sdcb.LibRaw;
using static HappyPhoton.Services.ImageServiceHelpers;

namespace HappyPhoton.Services;

/// <summary>
/// RAW processing service using LibRaw for fast decoding on Windows and Linux.
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
        // LibRaw native packages only available on Windows and Linux
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            LogDebug(ServiceName, "Not available on macOS - will use fallback");
            return false;
        }

        // On Windows/Linux, assume LibRaw is available (NuGet package includes native libs)
        // Actual failures will be caught when processing files
        LogDebug(ServiceName, "Should be available on this platform");
        return true;
    }

    public byte[]? ExtractThumbnail(string filePath)
    {
        if (!_isAvailable) return null;

        var sw = Stopwatch.StartNew();
        try
        {
            using var ctx = RawContext.OpenFile(filePath);

            // Unpack thumbnail data (required before ExportThumbnail)
            ctx.UnpackThumbnail();

            // Use MakeDcrawMemoryThumbnail which properly formats the thumbnail
            // (handles both JPEG and bitmap thumbnails correctly)
            using var thumbnail = ctx.MakeDcrawMemoryThumbnail();
            
            // Check if it's already JPEG (starts with FFD8)
            var data = thumbnail.AsSpan<byte>().ToArray();
            if (data.Length > 2 && data[0] == 0xFF && data[1] == 0xD8)
            {
                // Already JPEG, return as-is
                LogPerformance(ServiceName, "ExtractThumbnail", sw.ElapsedMilliseconds, filePath);
                LogDebug(ServiceName, $"Thumbnail: {data.Length / 1024}KB (JPEG)", filePath);
                return data;
            }

            // It's a bitmap - add PPM header so ImageMagick can decode it
            var width = thumbnail.Width;
            var height = thumbnail.Height;

            if (width == 0 || height == 0)
            {
                LogDebug(ServiceName, $"Thumbnail has zero dimensions: {width}x{height}", filePath);
                return null;
            }

            LogDebug(ServiceName, $"Thumbnail: {data.Length / 1024}KB (bitmap {width}x{height})", filePath);
            var ppmData = CreatePpmImage(data, width, height);
            LogPerformance(ServiceName, "ExtractThumbnail", sw.ElapsedMilliseconds, filePath);
            return ppmData;
        }
        catch (LibRawException ex)
        {
            LogDebug(ServiceName, $"Thumbnail extraction failed: {ex.Message}", filePath);
            return null;
        }
        catch (Exception ex)
        {
            LogDebug(ServiceName, $"Unexpected error extracting thumbnail: {ex.Message}", filePath);
            return null;
        }
    }

    private MagickImage? DecodeInternal(string filePath, bool halfSize)
    {
        if (!_isAvailable) return null;

        var sw = Stopwatch.StartNew();
        var modeName = halfSize ? "DecodeHalfSize" : "DecodeFull";
        try
        {
            using var ctx = RawContext.OpenFile(filePath);

            // Unpack the RAW data
            ctx.Unpack();

            // Process with half-size for faster decode (4x fewer pixels), or full resolution
            ctx.DcrawProcess(c =>
            {
                c.HalfSize = halfSize;
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
            if (!halfSize)
            {
                image.Modulate(new Percentage(100), new Percentage(105), new Percentage(100));
            }

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

    public MagickImage? DecodeHalfSize(string filePath) => DecodeInternal(filePath, halfSize: true);

    public MagickImage? DecodeFull(string filePath) => DecodeInternal(filePath, halfSize: false);

    public RawMetadata? ExtractMetadata(string filePath)
    {
        if (!_isAvailable) return null;

        try
        {
            using var ctx = RawContext.OpenFile(filePath);

            // Access metadata through ImageParams and ImageOtherParams
            var iParams = ctx.ImageParams;
            var other = ctx.ImageOtherParams;

            var metadata = new RawMetadata
            {
                CameraMake = iParams.Make?.Trim(),
                CameraModel = iParams.Model?.Trim(),
                PixelWidth = ctx.RawWidth,
                PixelHeight = ctx.RawHeight,
                Iso = (int)other.IsoSpeed,
                FNumber = other.Aperture,
                ExposureTime = other.Shutter,
                FocalLength = other.FocalLength,
            };

            var lens = ctx.LensInfo.Lens?.Trim();
            metadata.LensModel = string.IsNullOrEmpty(lens) ? null : lens;

            // Parse timestamp
            if (other.Timestamp > 0)
            {
                metadata.DateTaken = DateTimeOffset.FromUnixTimeSeconds(other.Timestamp).DateTime;
            }

            LogDebug(ServiceName, $"Metadata: {metadata.CameraMake} {metadata.CameraModel}, ISO {metadata.Iso}", filePath);
            return metadata;
        }
        catch (Exception ex)
        {
            LogDebug(ServiceName, $"Metadata extraction failed: {ex.Message}", filePath);
            return null;
        }
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
