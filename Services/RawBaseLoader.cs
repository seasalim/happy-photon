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
    private readonly Func<LibRawContext, CancellationToken, HistogramData?>?
        _rawHistogramSampler;
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
        _rawHistogramSampler = rawHistogramSampler;
    }

    public bool CanLoad(ImageFile file)
    {
        ArgumentNullException.ThrowIfNull(file);
        return _isAvailable && file.IsRaw;
    }

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
            ? BaseImageLoadOutcome.Loaded(pair, loaded.Analysis)
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
            var requestedResolution = decode.ProfileResolution ??
                (decode.ProfileSelection == null
                    ? DcpProfileResolution.BuiltIn
                    : DcpProfileResolution.Rejected(
                        decode.ProfileSelection,
                        DcpProfileErrorCode.UnsupportedVariant,
                        "The selected profile was not resolved for this decode."));
            using var context = LibRawContext.Open(file.FilePath, cancellationToken);
            var sensorIdentity = context.GetSensorIdentity(cancellationToken);
            var isMonochrome = IsMonochromeSensor(sensorIdentity);
            var cameraData = DcpCameraData.Defaults;
            if (!isMonochrome && requestedResolution.IsActive &&
                file.Extension.Equals(".dng", StringComparison.OrdinalIgnoreCase))
            {
                var cameraResult = TryReadDngCameraData(file.FilePath);
                cameraData = cameraResult.Data;
                if (cameraResult.Error != null &&
                    requestedResolution.Selection != null)
                {
                    requestedResolution = DcpProfileResolution.Rejected(
                        requestedResolution.Selection,
                        DcpProfileErrorCode.Corrupt,
                        cameraResult.Error);
                }
            }
            var dimensions = context.GetDimensions(cancellationToken);
            var rawMetadata = context.GetMetadata(cancellationToken);
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
            HistogramData? rawHistogram = null;
            SourceSaturationMask? sensorSaturation = null;
            if (preview)
            {
                var histogramStopwatch = Stopwatch.StartNew();
                try
                {
                    if (_rawHistogramSampler != null)
                    {
                        rawHistogram = _rawHistogramSampler(
                            context,
                            cancellationToken);
                    }
                    else
                    {
                        var sensorArtifacts = RawSensorHistogram
                            .SampleWithSaturation(
                                context,
                                Math.Max(1, (fullWidth + 1) / 2),
                                Math.Max(1, (fullHeight + 1) / 2),
                                cancellationToken);
                        rawHistogram = sensorArtifacts?.Histogram;
                        sensorSaturation = sensorArtifacts?.SourceSaturation;
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    ImageServiceHelpers.LogDebug(nameof(RawBaseLoader),
                        $"RAW histogram failed: {exception.Message}", file.FilePath);
                }
                ImageServiceHelpers.LogPerformance(
                    nameof(RawBaseLoader),
                    "RawHistogram",
                    histogramStopwatch.ElapsedMilliseconds,
                    file.FilePath);
            }
            cancellationToken.ThrowIfCancellationRequested();
            var asShot = WhiteBalanceModel.EstimateAsShot(
                cameraFacts.CamMul,
                cameraFacts.CamToSrgb,
                cameraFacts.PreMul);
            DcpCharacterizationResult? dcp = null;
            CameraRgbCharacterization? characterization = null;
            if (!isMonochrome)
            {
                dcp = DcpMatrixCalculator.Create(
                    requestedResolution,
                    cameraData,
                    cameraFacts,
                    asShot.kelvin);
                characterization = dcp.IsActive
                    ? CameraRgbCharacterization.CreateProfile(dcp.CameraToRec2020!)
                    : CameraRgbCharacterization.Create(cameraFacts);
            }

            context.ConfigureOutput(
                ConfigureOutput(decode, preview, isMonochrome),
                cancellationToken);
            context.Process(cancellationToken);

            ushort[]? previewGray = null;
            var previewGrayWidth = 0;
            var previewGrayHeight = 0;
            using (var processed = context.MakeProcessedImage(cancellationToken))
            {
                var description = processed.Description;
                if (description.BitsPerSample != 16 ||
                    !HasExpectedProcessedLayout(
                        isMonochrome,
                        description.Channels) ||
                    description.Width == 0 || description.Height == 0)
                {
                    return null;
                }

                var processedWidth = checked((int)description.Width);
                var processedHeight = checked((int)description.Height);
                if (isMonochrome && preview)
                {
                    previewGray = MonochromeRawImporter.AreaAverageToMaxDimension(
                        processed.AsSpan(),
                        processedWidth,
                        processedHeight,
                        BaseImage.LargePreviewMaxDimension,
                        cancellationToken,
                        out previewGrayWidth,
                        out previewGrayHeight);
                }
                else if (isMonochrome)
                {
                    pixels = MonochromeRawImporter.ImportGray16(
                        processed.AsSpan(),
                        processedWidth,
                        processedHeight,
                        cancellationToken);
                }
                else
                {
                    pixels = characterization!.ImportRgb16(
                        processed.AsSpan(),
                        processedWidth,
                        processedHeight,
                        cancellationToken);
                }
            }
            context.Recycle(cancellationToken);
            if (previewGray != null)
            {
                pixels = MonochromeRawImporter.ImportGray16(
                    previewGray,
                    previewGrayWidth,
                    previewGrayHeight,
                    cancellationToken);
            }
            cancellationToken.ThrowIfCancellationRequested();
            var decodedPixels = pixels ?? throw new InvalidOperationException(
                "LibRaw produced no decoded pixels.");

            ApplyOrientation(
                decodedPixels,
                orientation,
                fullWidth,
                fullHeight);
            var sourceSaturation = sensorSaturation?.OrientAndResize(
                orientation,
                checked((int)decodedPixels.Width),
                checked((int)decodedPixels.Height));
            var estimateStopwatch = Stopwatch.StartNew();
            var sourceExposureBiasEv = PreviewExposureEstimator.Estimate(
                thumbnailBytes,
                decodedPixels,
                metadataExposureBiasEv,
                file.FilePath);
            var estimateElapsed = estimateStopwatch.ElapsedMilliseconds;
            ImageServiceHelpers.LogPerformance(
                nameof(RawBaseLoader),
                "SourceExposureBias",
                thumbnailElapsed + estimateElapsed,
                file.FilePath,
                $"thumbnail={thumbnailElapsed};estimate={estimateElapsed}");
            decodedPixels.Depth = 16;
            decodedPixels.Strip();
            cancellationToken.ThrowIfCancellationRequested();

            var orientedFullSize = GetOrientedSize(
                fullWidth,
                fullHeight,
                orientation);
            var effectiveResolution = isMonochrome
                ? DcpProfileResolution.BuiltIn
                : requestedResolution.Selection == null ||
                dcp!.Status == DcpProfileErrorCode.None
                ? requestedResolution
                : DcpProfileResolution.Rejected(
                    requestedResolution.Selection,
                    dcp!.Status,
                    dcp.Message ?? "The selected camera profile was rejected.") with
                {
                    Token = dcp.Token
                };
            var effectiveDecode = decode.WithProfileResolution(effectiveResolution);
            var info = new BaseImageInfo(
                BaseSourceKind.RawLibRaw,
                IsRawSource: true,
                effectiveDecode,
                isMonochrome ? null : cameraFacts.CamMul,
                isMonochrome ? null : cameraFacts.CamToSrgb,
                AsShotKelvin: asShot.kelvin,
                AsShotTint: asShot.tint,
                HadIccProfile: false,
                IccDescription: null,
                ExifOrientationApplied: orientation,
                orientedFullSize.Width,
                orientedFullSize.Height,
                SourceExposureBiasEv: sourceExposureBiasEv)
            {
                IsMonochrome = isMonochrome,
                DcpProfile = dcp?.Payload,
                ProfileToken = dcp?.Token ?? string.Empty,
                ProfileStatus = dcp?.Status ?? DcpProfileErrorCode.None,
                ProfileMessage = dcp?.Message,
                CameraIdentity = isMonochrome
                    ? null
                    : new CameraIdentity(
                        rawMetadata.NormalizedMake ?? rawMetadata.Make,
                        rawMetadata.NormalizedModel ?? rawMetadata.Model)
            };
            PreviewBasePair? pair = null;
            BaseImage? full = null;
            var analysis = PreviewSourceAnalysis.Empty;
            if (preview)
            {
                pair = PreviewBasePairFactory.Create(
                    decodedPixels,
                    info,
                    cancellationToken);
                analysis = rawHistogram == null && sourceSaturation == null
                    ? PreviewSourceAnalysis.Empty
                    : new PreviewSourceAnalysis(
                        rawHistogram,
                        sourceSaturation?.Resize(
                            checked((int)pair.Interactive.Pixels.Width),
                            checked((int)pair.Interactive.Pixels.Height)));
            }
            else
            {
                full = new BaseImage(decodedPixels, info);
                pixels = null;
            }

            ImageServiceHelpers.LogPerformance(
                nameof(RawBaseLoader),
                preview ? nameof(LoadPreviewBaseWithOutcome) : nameof(LoadFullBase),
                stopwatch.ElapsedMilliseconds,
                file.FilePath,
                preview
                    ? $"size={pair!.Interactive.Pixels.Width}x{pair.Interactive.Pixels.Height};" +
                      $"large={pair.Large!.Pixels.Width}x{pair.Large.Pixels.Height}"
                    : $"size={full!.Pixels.Width}x{full.Pixels.Height}");
            return new LoadedBases(pair, full, analysis);
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

    private static (DcpCameraData Data, string? Error) TryReadDngCameraData(
        string path)
    {
        try
        {
            return (new DcpProfileReader().ReadCameraData(path), null);
        }
        catch (Exception exception)
        {
            ImageServiceHelpers.LogDebug(
                nameof(RawBaseLoader),
                $"DNG camera profile facts were rejected: {exception.Message}",
                path);
            return (
                DcpCameraData.Defaults,
                $"DNG camera calibration tags are invalid: {exception.Message}");
        }
    }

}
