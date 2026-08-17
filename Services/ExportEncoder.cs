using HappyPhoton.Models;
using ImageMagick;
using ImageMagick.Formats;

namespace HappyPhoton.Services;

internal static class ExportEncoder
{
    private const uint PngAdaptiveFilterQuality = 85;

    public static void Write(
        MagickImage image,
        ExportSettings settings,
        string path)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var quality = Math.Clamp(settings.Quality, 1, 100);
        image.Format = GetFormat(settings.Format);
        image.Quality = (uint)quality;
        image.SetProfile(ColorProfiles.SRGB);

        switch (settings.Format)
        {
            case ExportFormat.Png:
                WritePng(image, path);
                break;
            case ExportFormat.Webp:
                image.Settings.SetDefine(MagickFormat.WebP, "lossless", false);
                image.Write(path);
                break;
            default:
                image.Settings.Interlace = Interlace.NoInterlace;
                image.Settings.SetDefine(
                    MagickFormat.Jpeg,
                    "sampling-factor",
                    quality >= 90 ? "4:4:4" : "4:2:0");
                image.Write(path);
                break;
        }
    }

    private static void WritePng(MagickImage image, string path)
    {
        // Depth is deliberately left alone: setting it to 8 quantizes toward zero,
        // while the writer's BitDepth define rounds to the nearest level like the
        // display path (OUTPUT.md §1).
        image.Quality = PngAdaptiveFilterQuality;
        image.RemoveArtifact("png:exclude-chunk");
        image.Settings.RemoveDefine(MagickFormat.Png, "exclude-chunk");
        image.Write(path, CreatePngWriteDefines());
    }

    internal static PngWriteDefines CreatePngWriteDefines() =>
        new()
        {
            BitDepth = 8,
            CompressionLevel = 3,
            CompressionStrategy = PngCompressionStrategy.Adaptive,
            PreserveiCCP = true,
            ExcludeChunks = PngChunkFlags.sRGB
        };

    private static MagickFormat GetFormat(ExportFormat format) => format switch
    {
        ExportFormat.Png => MagickFormat.Png,
        ExportFormat.Webp => MagickFormat.WebP,
        _ => MagickFormat.Jpeg
    };
}
