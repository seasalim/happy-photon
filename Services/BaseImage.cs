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
    bool Distortion = true,
    bool ChromaticAberration = true,
    bool Vignetting = false)
{
    public static BaseDecodeSettings Default { get; } =
        new(HlReconstructionMode.Clip);

    public static BaseDecodeSettings From(EditSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return settings.HlReconstruction == HlReconstructionMode.Clip &&
               settings.Lens.Distortion &&
               settings.Lens.ChromaticAberration &&
               !settings.Lens.Vignetting &&
               settings.RawProfile == null
            ? Default
            : new BaseDecodeSettings(
                settings.HlReconstruction,
                settings.Lens.Distortion,
                settings.Lens.ChromaticAberration,
                settings.Lens.Vignetting)
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
        $"base-v{BaseImage.Version};hl={GetHighlightKey()}" +
        $";lens={(Distortion ? 1 : 0)}{(ChromaticAberration ? 1 : 0)}{(Vignetting ? 1 : 0)}" +
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
    double SourceExposureBiasEv = 0)
{
    public bool IsMonochrome { get; init; }
    internal DcpProfilePayload? DcpProfile { get; init; }
    internal string ProfileToken { get; init; } = string.Empty;
    internal DcpProfileErrorCode ProfileStatus { get; init; }
    internal string? ProfileMessage { get; init; }
    internal CameraIdentity? CameraIdentity { get; init; }
    internal LensPrescription? LensPrescription { get; init; }
    public LensPrescriptionSummary? LensPrescriptionSummary { get; init; }
}

/// <summary>
/// Owns one decoded linear Rec.2020/D65 image. Ownership transfers at construction
/// and callers must dispose the base after all renders using it have completed.
/// </summary>
public sealed class BaseImage : IDisposable
{
    public const int Version = 17;
    public const int InteractivePreviewMaxDimension = 1600;
    public const int LargePreviewMaxDimension = 3200;

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
