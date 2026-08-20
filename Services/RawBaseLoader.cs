using System.Diagnostics;
using HappyPhoton.LibRaw.Interop;
using HappyPhoton.Models;
using ImageMagick;

namespace HappyPhoton.Services;

public sealed partial class RawBaseLoader : IBaseImageLoader
{
    private readonly bool _isAvailable;
    internal bool IsHealthRejected { get; }
    private readonly Func<LibRawContext, byte[]?> _thumbnailReader;
    private readonly Func<LibRawContext, CancellationToken, HistogramData?> _rawHistogramSampler;
    public RawBaseLoader()
        : this(LibRawNativeSupport.Health)
    {
    }
    internal RawBaseLoader(LibRawRuntimeHealth health)
        : this(
            health?.IsHealthy ?? throw new ArgumentNullException(nameof(health)),
            healthRejected: !health.IsHealthy)
    {
    }

    internal RawBaseLoader(
        bool isAvailable,
        Func<LibRawContext, byte[]?>? thumbnailReader = null,
        bool healthRejected = false,
        Func<LibRawContext, CancellationToken, HistogramData?>? rawHistogramSampler = null)
    {
        _isAvailable = isAvailable;
        IsHealthRejected = healthRejected;
        _thumbnailReader = thumbnailReader ?? RawThumbnailReader.Read;
        _rawHistogramSampler = rawHistogramSampler ?? RawSensorHistogram.Sample;
    }

    public bool CanLoad(ImageFile file)
    {
        ArgumentNullException.ThrowIfNull(file);
        return _isAvailable && file.IsRaw;
    }

    public BaseImage? LoadPreviewBase(
        ImageFile file,
        BaseDecodeSettings decode,
        CancellationToken cancellationToken) =>
        LoadPreviewBaseWithOutcome(
            file,
            decode,
            cancellationToken).DetachInteractiveImage();

    public BaseImageLoadOutcome LoadPreviewBaseWithOutcome(
        ImageFile file,
        BaseDecodeSettings decode,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(file);
        if (!file.IsRaw)
        {
            return BaseImageLoadOutcome.Failed(
                BaseImageLoadFailure.DecodeFailed);
        }
        if (IsHealthRejected)
        {
            return BaseImageLoadOutcome.Failed(
                BaseImageLoadFailure.RawRuntimeUnavailable);
        }

        var loaded = Load(file, decode, preview: true, cancellationToken);
        return loaded?.Pair is { } pair
            ? BaseImageLoadOutcome.Loaded(pair)
            : BaseImageLoadOutcome.Failed(
                BaseImageLoadFailure.UnsupportedRaw);
    }

    public BaseImage? LoadFullBase(
        ImageFile file,
        BaseDecodeSettings decode,
        CancellationToken cancellationToken) =>
        Load(file, decode, preview: false, cancellationToken)?.Full;

    private LoadedBases? Load(
        ImageFile file,
        BaseDecodeSettings decode,
        bool preview,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(decode);
        cancellationToken.ThrowIfCancellationRequested();
        if (!CanLoad(file))
        {
            return null;
        }

        var stopwatch = Stopwatch.StartNew();
        MagickImage? pixels = null;
        try
        {
            using var context = LibRawContext.Open(file.FilePath, cancellationToken);
            var dimensions = context.GetDimensions(cancellationToken);
            var fullWidth = checked((int)dimensions.VisibleWidth);
            var fullHeight = checked((int)dimensions.VisibleHeight);
            var orientation = NormalizeOrientation(dimensions.Orientation);
            var metadataExposureBiasEv = RawExposureBias.Read(
                context,
                file.FilePath);
            var thumbnailStopwatch = Stopwatch.StartNew();
            var thumbnailBytes = ReadThumbnail(context, file.FilePath);
            var thumbnailElapsed = thumbnailStopwatch.ElapsedMilliseconds;
            cancellationToken.ThrowIfCancellationRequested();

            context.Unpack(cancellationToken);
            var cameraFacts = RawCameraFactSnapshot.Copy(
                context.GetCameraFacts(cancellationToken));
            var histogramStopwatch = Stopwatch.StartNew();
            HistogramData? rawHistogram;
            try
            {
                rawHistogram = _rawHistogramSampler(context, cancellationToken);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception exception)
            {
                ImageServiceHelpers.LogDebug(nameof(RawBaseLoader),
                    $"RAW histogram failed: {exception.Message}", file.FilePath);
                rawHistogram = null;
            }
            ImageServiceHelpers.LogPerformance(nameof(RawBaseLoader), "RawHistogram",
                histogramStopwatch.ElapsedMilliseconds, file.FilePath);
            cancellationToken.ThrowIfCancellationRequested();
            context.ConfigureOutput(ConfigureOutput(decode, preview), cancellationToken);
            context.Process(cancellationToken);

            using var processed = context.MakeProcessedImage(cancellationToken);
            var description = processed.Description;
            if (description.BitsPerSample != 16 || description.Channels != 3 ||
                description.Width == 0 || description.Height == 0)
            {
                return null;
            }

            context.Recycle(cancellationToken);
            var characterization = CameraRgbCharacterization.Create(cameraFacts);
            pixels = characterization.ImportRgb16(
                processed.AsSpan(),
                checked((int)description.Width),
                checked((int)description.Height),
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            ApplyOrientation(
                pixels,
                orientation,
                fullWidth,
                fullHeight);
            var estimateStopwatch = Stopwatch.StartNew();
            var sourceExposureBiasEv = PreviewExposureEstimator.Estimate(
                thumbnailBytes,
                pixels,
                metadataExposureBiasEv,
                file.FilePath);
            var estimateElapsed = estimateStopwatch.ElapsedMilliseconds;
            ImageServiceHelpers.LogPerformance(
                nameof(RawBaseLoader),
                "SourceExposureBias",
                thumbnailElapsed + estimateElapsed,
                file.FilePath,
                $"thumbnail={thumbnailElapsed};estimate={estimateElapsed}");
            pixels.Depth = 16;
            pixels.Strip();
            cancellationToken.ThrowIfCancellationRequested();

            var orientedFullSize = GetOrientedSize(
                fullWidth,
                fullHeight,
                orientation);
            var asShot = WhiteBalanceModel.EstimateAsShot(
                cameraFacts.CamMul,
                cameraFacts.CamToSrgb,
                cameraFacts.PreMul);
            var info = new BaseImageInfo(
                BaseSourceKind.RawLibRaw,
                IsRawSource: true,
                decode,
                cameraFacts.CamMul,
                cameraFacts.CamToSrgb,
                AsShotKelvin: asShot.kelvin,
                AsShotTint: asShot.tint,
                HadIccProfile: false,
                IccDescription: null,
                ExifOrientationApplied: orientation,
                orientedFullSize.Width,
                orientedFullSize.Height,
                SourceExposureBiasEv: sourceExposureBiasEv,
                RawHistogram: rawHistogram);
            PreviewBasePair? pair = null;
            BaseImage? full = null;
            if (preview)
            {
                pair = PreviewBasePairFactory.Create(
                    pixels,
                    info,
                    cancellationToken);
            }
            else
            {
                full = new BaseImage(pixels, info);
                pixels = null;
            }

            ImageServiceHelpers.LogPerformance(
                nameof(RawBaseLoader),
                preview ? nameof(LoadPreviewBase) : nameof(LoadFullBase),
                stopwatch.ElapsedMilliseconds,
                file.FilePath,
                preview
                    ? $"size={pair!.Interactive.Pixels.Width}x{pair.Interactive.Pixels.Height};" +
                      $"large={pair.Large!.Pixels.Width}x{pair.Large.Pixels.Height}"
                    : $"size={full!.Pixels.Width}x{full.Pixels.Height}");
            return new LoadedBases(pair, full);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            ImageServiceHelpers.LogDebug(
                nameof(RawBaseLoader),
                $"Decode failed: {exception.Message}",
                file.FilePath);
            return null;
        }
        finally
        {
            pixels?.Dispose();
        }
    }

    internal static MagickImage ImportRgb16(
        ReadOnlySpan<byte> data,
        int width,
        int height) => CameraRgbCharacterization.Passthrough.ImportRgb16(
            data,
            width,
            height);

    internal static MagickImage ImportRgb8(
        ReadOnlySpan<byte> data,
        int width,
        int height) => CameraRgbCharacterization.ImportRgb8(data, width, height);

    internal static bool ApplyOrientation(
        MagickImage image,
        int orientation,
        int sourceWidth,
        int sourceHeight)
    {
        var alreadyApplied = orientation is >= 5 and <= 8 &&
            DimensionsAreSwapped(
                (int)image.Width,
                (int)image.Height,
                sourceWidth,
                sourceHeight);
        if (orientation != 1 && !alreadyApplied)
        {
            ImageServiceHelpers.ApplyExifOrientation(image, orientation);
        }

        return alreadyApplied;
    }

    private static bool DimensionsAreSwapped(
        int decodedWidth,
        int decodedHeight,
        int sourceWidth,
        int sourceHeight)
    {
        var sameDelta = Math.Abs(
            (long)decodedWidth * sourceHeight -
            (long)decodedHeight * sourceWidth);
        var swappedDelta = Math.Abs(
            (long)decodedWidth * sourceWidth -
            (long)decodedHeight * sourceHeight);
        return swappedDelta < sameDelta;
    }

    private static (int Width, int Height) GetOrientedSize(
        int width,
        int height,
        int orientation) =>
        orientation is >= 5 and <= 8
            ? (height, width)
            : (width, height);

    private static int NormalizeOrientation(int orientation) =>
        orientation is >= 1 and <= 8 ? orientation : 1;

    private byte[]? ReadThumbnail(LibRawContext context, string filePath)
    {
        try
        {
            return _thumbnailReader(context);
        }
        catch (Exception exception)
        {
            ImageServiceHelpers.LogDebug(
                nameof(RawBaseLoader),
                $"Thumbnail read failed: {exception.Message}",
                filePath);
            return null;
        }
    }

}
