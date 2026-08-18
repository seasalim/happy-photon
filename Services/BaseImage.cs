using HappyPhoton.Models;
using ImageMagick;

namespace HappyPhoton.Services;

public enum BaseSourceKind
{
    RawLibRaw,
    Standard,
    HeicPlatform
}

public sealed record BaseDecodeSettings(
    HlReconstructionMode HlReconstruction,
    FbddMode NoiseReduction)
{
    public static BaseDecodeSettings Default { get; } =
        new(HlReconstructionMode.Clip, FbddMode.Off);

    public static BaseDecodeSettings From(EditSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return settings.HlReconstruction == HlReconstructionMode.Clip &&
               settings.Detail.NoiseReduction == FbddMode.Off
            ? Default
            : new BaseDecodeSettings(
                settings.HlReconstruction,
                settings.Detail.NoiseReduction);
    }

    public string CacheKey =>
        $"base-v{BaseImage.Version};hl={GetHighlightKey()};fbdd={GetNoiseReductionKey()}";

    private string GetHighlightKey() => HlReconstruction switch
    {
        HlReconstructionMode.Blend => "blend",
        HlReconstructionMode.Clip => "clip",
        _ => throw new InvalidOperationException(
            $"Unsupported highlight reconstruction mode: {HlReconstruction}.")
    };

    private string GetNoiseReductionKey() => NoiseReduction switch
    {
        FbddMode.Off => "off",
        FbddMode.Light => "light",
        FbddMode.Full => "full",
        _ => throw new InvalidOperationException(
            $"Unsupported FBDD mode: {NoiseReduction}.")
    };
}

public sealed record BaseImageInfo(
    BaseSourceKind Kind,
    bool IsRawSource,
    BaseDecodeSettings Decode,
    double[]? CamMul,
    double[,]? CamToSrgb,
    double AsShotKelvin,
    double AsShotTint,
    bool HadIccProfile,
    string? IccDescription,
    int ExifOrientationApplied,
    int FullWidth,
    int FullHeight,
    double SourceExposureBiasEv = 0);

/// <summary>
/// Owns one decoded linear Rec.2020/D65 image. Ownership transfers at construction
/// and callers must dispose the base after all renders using it have completed.
/// </summary>
public sealed class BaseImage : IDisposable
{
    public const int Version = 6;
    public const int PreviewMaxDimension = 1600;

    private MagickImage? _pixels;

    public MagickImage Pixels =>
        _pixels ?? throw new ObjectDisposedException(nameof(BaseImage));

    public BaseImageInfo Info { get; }

    public BaseImage(MagickImage pixels, BaseImageInfo info)
    {
        ArgumentNullException.ThrowIfNull(pixels);
        ArgumentNullException.ThrowIfNull(info);
        ArgumentNullException.ThrowIfNull(info.Decode);
        _pixels = pixels;
        Info = info;
    }

    public void Dispose()
    {
        Interlocked.Exchange(ref _pixels, null)?.Dispose();
    }
}
