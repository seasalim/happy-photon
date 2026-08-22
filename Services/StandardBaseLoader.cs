using HappyPhoton.Models;
using ImageMagick;
using static HappyPhoton.Services.BitmapConversionService;
using static HappyPhoton.Services.ImageServiceHelpers;

namespace HappyPhoton.Services;

public sealed class StandardBaseLoader : IBaseImageLoader
{
    private readonly Func<string, MagickReadSettings, MagickImage> _decode;
    private readonly Func<MagickImage, ImageFile, CancellationToken,
        SourceSaturationMask?> _captureSourceSaturation;

    public StandardBaseLoader()
        : this((path, settings) => new MagickImage(path, settings))
    {
    }

    internal StandardBaseLoader(
        Func<string, MagickReadSettings, MagickImage> decode,
        Func<MagickImage, ImageFile, CancellationToken,
            SourceSaturationMask?>? captureSourceSaturation = null)
    {
        _decode = decode ?? throw new ArgumentNullException(nameof(decode));
        _captureSourceSaturation = captureSourceSaturation ??
            TryCaptureSourceSaturation;
    }

    public bool CanLoad(ImageFile file)
    {
        ArgumentNullException.ThrowIfNull(file);
        return !file.IsRaw &&
            ImageFile.SupportedExtensions.Contains(file.Extension);
    }

    public BaseImageLoadOutcome LoadPreviewBaseWithOutcome(
        ImageFile file,
        BaseDecodeSettings decode,
        CancellationToken cancellationToken)
    {
        var loaded = Load(file, decode, cancellationToken, preview: true);
        return loaded?.Pair is { } pair
            ? BaseImageLoadOutcome.Loaded(pair, loaded.Analysis)
            : BaseImageLoadOutcome.Failed(BaseImageLoadFailure.DecodeFailed);
    }

    public BaseImage? LoadFullBase(
        ImageFile file,
        BaseDecodeSettings decode,
        CancellationToken cancellationToken) =>
        Load(file, decode, cancellationToken, preview: false)?.Full;

    private LoadedBases? Load(
        ImageFile file,
        BaseDecodeSettings decode,
        CancellationToken cancellationToken,
        bool preview)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(decode);
        cancellationToken.ThrowIfCancellationRequested();

        if (file.IsRaw ||
            !ImageFile.SupportedExtensions.Contains(file.Extension))
        {
            return null;
        }

        MagickImage? image = null;
        try
        {
            var nativeGeometry = GetNativeGeometry(file, preview);
            var readSettings = CreateReadSettings(file, preview, nativeGeometry);
            cancellationToken.ThrowIfCancellationRequested();

            image = _decode(file.FilePath, readSettings);
            cancellationToken.ThrowIfCancellationRequested();

            var orientation = NormalizeOrientation(image.Orientation);
            image.AutoOrient();
            var fullWidth = checked((int)image.Width);
            var fullHeight = checked((int)image.Height);
            if (nativeGeometry is { } native)
            {
                (fullWidth, fullHeight) = GetOrientedDimensions(
                    native.Width,
                    native.Height,
                    native.Orientation);
            }

            var sourceSaturation = preview
                ? _captureSourceSaturation(image, file, cancellationToken)
                : null;
            var profile = image.GetColorProfile();
            var hadProfile = profile != null;
            var profileDescription = profile == null
                ? null
                : string.IsNullOrWhiteSpace(profile.Description)
                    ? profile.Name
                    : profile.Description;
            NormalizeColor(image, profile);
            image.Strip();
            cancellationToken.ThrowIfCancellationRequested();

            image.Depth = 16;
            cancellationToken.ThrowIfCancellationRequested();
            var info = new BaseImageInfo(
                IsHeic(file) ? BaseSourceKind.HeicPlatform : BaseSourceKind.Standard,
                false,
                decode,
                null,
                null,
                6504,
                0,
                hadProfile,
                profileDescription,
                orientation,
                fullWidth,
                fullHeight);
            if (preview)
            {
                var pair = PreviewBasePairFactory.Create(
                    image,
                    info,
                    cancellationToken);
                var analysis = sourceSaturation == null
                    ? PreviewSourceAnalysis.Empty
                    : new PreviewSourceAnalysis(
                        RawHistogram: null,
                        sourceSaturation.Resize(
                            checked((int)pair.Interactive.Pixels.Width),
                            checked((int)pair.Interactive.Pixels.Height)));
                return new LoadedBases(pair, null, analysis);
            }

            var full = new BaseImage(image, info);
            image = null;
            return new LoadedBases(
                null,
                full,
                PreviewSourceAnalysis.Empty);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            LogDebug(nameof(StandardBaseLoader), $"Failed: {ex.Message}", file.FilePath);
            HandleImageLoadError(ex, file.FilePath);
            return null;
        }
        finally
        {
            image?.Dispose();
        }
    }

    private static MagickReadSettings CreateReadSettings(
        ImageFile file,
        bool preview,
        NativeGeometry? nativeGeometry)
    {
        var settings = new MagickReadSettings();
        if (preview &&
            IsJpeg(file) &&
            nativeGeometry is { } native &&
            Math.Max(native.Width, native.Height) >
                BaseImage.LargePreviewMaxDimension)
        {
            ApplyJpegSizeHint(settings, BaseImage.LargePreviewMaxDimension);
        }

        if (file.Extension.Equals(".gif", StringComparison.OrdinalIgnoreCase))
        {
            settings.FrameIndex = 0;
            settings.FrameCount = 1;
        }

        return settings;
    }

    private static NativeGeometry? GetNativeGeometry(
        ImageFile file,
        bool preview)
    {
        if (!preview || !IsJpeg(file))
        {
            return null;
        }

        var info = new MagickImageInfo(file.FilePath);
        return new NativeGeometry(
            checked((int)info.Width),
            checked((int)info.Height),
            NormalizeOrientation(info.Orientation));
    }

    private static void NormalizeColor(MagickImage image, IColorProfile? profile)
    {
        if (profile != null)
        {
            image.TransformColorSpace(profile, WorkingSpaceIccProfile.LinearRec2020);
        }
        else if (image.ColorSpace == ColorSpace.CMYK)
        {
            image.TransformColorSpace(
                ColorProfiles.USWebCoatedSWOP,
                WorkingSpaceIccProfile.LinearRec2020);
        }
        else
        {
            WorkingSpaceColorConversion.ConvertSrgbToLinearRec2020(image);
            return;
        }

        image.SetAttribute("colorspace", "RGB");
        if (image.ColorSpace != ColorSpace.RGB)
        {
            throw new InvalidOperationException(
                "Unable to tag working-space pixels as linear RGB.");
        }
    }

    private static SourceSaturationMask? TryCaptureSourceSaturation(
        MagickImage image,
        ImageFile file,
        CancellationToken cancellationToken)
    {
        if (!IsJpeg(file) && !IsHeic(file)) return null;
        try
        {
            var encodedMaximum = IsJpeg(file)
                ? byte.MaxValue
                : MaximumForDepth(image.Depth);
            return SourceSaturationMask.CaptureEncoded(
                image,
                encodedMaximum,
                BaseImage.LargePreviewMaxDimension,
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            LogDebug(
                nameof(StandardBaseLoader),
                $"Source saturation capture failed: {exception.Message}",
                file.FilePath);
            return null;
        }
    }

    private static uint? MaximumForDepth(uint depth) =>
        depth is >= 1 and <= 16 ? (1u << checked((int)depth)) - 1 : null;

    private static (int Width, int Height) GetOrientedDimensions(
        int width,
        int height,
        int orientation) =>
        orientation is 5 or 6 or 7 or 8
            ? (height, width)
            : (width, height);

    private static int NormalizeOrientation(OrientationType orientation) =>
        orientation == OrientationType.Undefined ? 1 : (int)orientation;

    private static bool IsJpeg(ImageFile file) =>
        file.Extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
        file.Extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase);

    private static bool IsHeic(ImageFile file) =>
        file.Extension.Equals(".heic", StringComparison.OrdinalIgnoreCase) ||
        file.Extension.Equals(".heif", StringComparison.OrdinalIgnoreCase);

    private readonly record struct NativeGeometry(
        int Width,
        int Height,
        int Orientation);

    private sealed record LoadedBases(
        PreviewBasePair? Pair,
        BaseImage? Full,
        PreviewSourceAnalysis Analysis);
}
