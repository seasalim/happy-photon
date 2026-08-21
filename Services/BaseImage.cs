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
               settings.Detail.NoiseReduction == FbddMode.Off &&
               settings.RawProfile == null
            ? Default
            : new BaseDecodeSettings(
                settings.HlReconstruction,
                settings.Detail.NoiseReduction)
            {
                ProfileSelection = settings.RawProfile?.Clone()
            };
    }

    internal RawProfileSelection? ProfileSelection { get; init; }

    internal DcpProfileResolution? ProfileResolution { get; init; }

    internal BaseDecodeSettings WithProfileResolution(
        DcpProfileResolution resolution) => resolution.Selection == null
            ? this with
            {
                ProfileResolution = null,
                ProfileSelection = null
            }
            : this with
            {
                ProfileResolution = resolution,
                ProfileSelection = resolution.Selection.Clone()
            };

    public string CacheKey =>
        $"base-v{BaseImage.Version};hl={GetHighlightKey()};fbdd={GetNoiseReductionKey()}" +
        GetProfileKey();

    private string GetProfileKey()
    {
        var token = ProfileResolution?.Token ?? ProfileSelection?.CacheToken;
        return string.IsNullOrEmpty(token) ? string.Empty : $";dcp={token}";
    }

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
    double SourceExposureBiasEv = 0,
    // HistogramData is mutable; record equality compares this loader fact by reference.
    HistogramData? RawHistogram = null)
{
    internal DcpProfilePayload? DcpProfile { get; init; }
    internal string ProfileToken { get; init; } = string.Empty;
    internal DcpProfileErrorCode ProfileStatus { get; init; }
    internal string? ProfileMessage { get; init; }
    internal CameraIdentity? CameraIdentity { get; init; }
}

/// <summary>
/// Owns one decoded linear Rec.2020/D65 image. Ownership transfers at construction
/// and callers must dispose the base after all renders using it have completed.
/// </summary>
public sealed class BaseImage : IDisposable
{
    public const int Version = 11;
    public const int InteractivePreviewMaxDimension = 1600;
    public const int LargePreviewMaxDimension = 3200;

    private MagickImage? _pixels;

    public MagickImage Pixels =>
        _pixels ?? throw new ObjectDisposedException(nameof(BaseImage));

    public BaseImageInfo Info { get; }

    internal SourceSaturationMask? SourceSaturation { get; }

    public BaseImage(MagickImage pixels, BaseImageInfo info)
        : this(pixels, info, sourceSaturation: null)
    {
    }

    internal BaseImage(
        MagickImage pixels,
        BaseImageInfo info,
        SourceSaturationMask? sourceSaturation)
    {
        ArgumentNullException.ThrowIfNull(pixels);
        ArgumentNullException.ThrowIfNull(info);
        ArgumentNullException.ThrowIfNull(info.Decode);
        _pixels = pixels;
        Info = info;
        SourceSaturation = sourceSaturation;
    }

    public void Dispose()
    {
        Interlocked.Exchange(ref _pixels, null)?.Dispose();
    }
}
