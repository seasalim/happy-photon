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
        var performanceTrace = new RawPreviewPerformanceTrace(
            stopwatch,
            file.FilePath,
            preview);
        MagickImage? pixels = null;
        MagickImage? interactivePixels = null;
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
            performanceTrace.Mark("Open");
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
            var lensResult = isMonochrome
                ? LensPrescriptionReadResult.None
                : ReadLensPrescription(file, rawMetadata, dimensions);
            var lensPrescription = lensResult.Prescription;
            var applyLens = lensPrescription != null &&
                (decode.Distortion && lensPrescription.HasDistortion ||
                 decode.ChromaticAberration && lensPrescription.HasChromaticAberration ||
                 decode.Vignetting && lensPrescription.HasVignetting);
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
            performanceTrace.Mark("HeadersAndThumbnail");

            context.Unpack(cancellationToken);
            performanceTrace.Mark("Unpack");
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
            performanceTrace.Mark("SensorAnalysis");
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
            performanceTrace.Mark("DecodeSetup");
            context.Process(cancellationToken);
            performanceTrace.Mark("Process");

            ushort[]? previewGray = null;
            var previewGrayWidth = 0;
            var previewGrayHeight = 0;
            var lensOrientation = orientation;
            LensCorrectionReferenceFrame? lensReferenceFrame = null;
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
                lensOrientation = ResolveLensOrientation(
                    processedWidth,
                    processedHeight,
                    fullWidth,
                    fullHeight,
                    orientation);
                var lensReferenceSize = lensOrientation == orientation
                    ? (Width: fullWidth, Height: fullHeight)
                    : (Width: fullHeight, Height: fullWidth);
                if (applyLens)
                {
                    var canonicalOutput = LensCorrectionProcessor.GetOutputSize(
                        fullWidth, fullHeight, orientation, maxDimension: null,
                        lensPrescription);
                    lensReferenceFrame = new LensCorrectionReferenceFrame(
                        lensReferenceSize.Width,
                        lensReferenceSize.Height,
                        canonicalOutput.Width,
                        canonicalOutput.Height);
                }
                if (applyLens &&
                    !LensCorrectionProcessor.CanApply(
                        processedWidth, processedHeight, lensOrientation,
                        lensPrescription!, decode, lensReferenceFrame))
                {
                    ImageServiceHelpers.LogDebug(
                        nameof(RawBaseLoader),
                        "Lens prescription rejected because its warp cannot cover the frame.",
                        file.FilePath);
                    applyLens = false;
                    lensPrescription = null;
                }
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
                else if (applyLens && preview)
                {
                    var interactiveSize = LensCorrectionProcessor.GetOutputSize(
                        processedWidth,
                        processedHeight,
                        lensOrientation,
                        BaseImage.InteractivePreviewMaxDimension,
                        lensPrescription,
                        lensReferenceFrame);
                    var largeSize = LensCorrectionProcessor.GetOutputSize(
                        processedWidth,
                        processedHeight,
                        lensOrientation,
                        BaseImage.LargePreviewMaxDimension,
                        lensPrescription,
                        lensReferenceFrame);
                    interactivePixels = LensCorrectionProcessor.ImportCorrected(
                        processed.AsSpan(), processedWidth, processedHeight,
                        interactiveSize.Width, interactiveSize.Height,
                        lensOrientation, characterization!, lensPrescription!, decode,
                        cancellationToken, lensReferenceFrame);
                    pixels = LensCorrectionProcessor.ImportCorrected(
                        processed.AsSpan(), processedWidth, processedHeight,
                        largeSize.Width, largeSize.Height,
                        lensOrientation, characterization!, lensPrescription!, decode,
                        cancellationToken, lensReferenceFrame);
                }
                else if (applyLens)
                {
                    var fullSize = LensCorrectionProcessor.GetOutputSize(
                        processedWidth, processedHeight, lensOrientation, maxDimension: null,
                        lensPrescription, lensReferenceFrame);
                    pixels = LensCorrectionProcessor.ImportCorrected(
                        processed.AsSpan(), processedWidth, processedHeight,
                        fullSize.Width, fullSize.Height, lensOrientation,
                        characterization!, lensPrescription!, decode,
                        cancellationToken, lensReferenceFrame);
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
            performanceTrace.Mark("Import");
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

            if (!applyLens)
            {
                ApplyOrientation(
                    decodedPixels,
                    orientation,
                    fullWidth,
                    fullHeight);
            }
            var sourceSaturation = applyLens && sensorSaturation != null
                ? LensCorrectionProcessor.WarpMask(
                    sensorSaturation,
                    checked((int)(interactivePixels ?? decodedPixels).Width),
                    checked((int)(interactivePixels ?? decodedPixels).Height),
                    lensOrientation,
                    lensPrescription!,
                    decode,
                    lensReferenceFrame)
                : sensorSaturation?.OrientAndResize(
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
            performanceTrace.Mark("PostProcess");

            var orientedFullSize = applyLens
                ? LensCorrectionProcessor.GetOutputSize(
                    fullWidth, fullHeight, orientation, maxDimension: null,
                    lensPrescription)
                : GetOrientedSize(fullWidth, fullHeight, orientation);
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
                        rawMetadata.NormalizedModel ?? rawMetadata.Model),
                LensPrescription = lensPrescription,
                LensPrescriptionSummary = lensPrescription?.Summary
            };
            PreviewBasePair? pair = null;
            BaseImage? full = null;
            var analysis = PreviewSourceAnalysis.Empty;
            if (preview)
            {
                if (applyLens)
                {
                    pair = new PreviewBasePair(
                        new BaseImage(interactivePixels!, info),
                        new BaseImage(decodedPixels, info));
                    interactivePixels = null;
                    pixels = null;
                }
                else
                {
                    pair = PreviewBasePairFactory.Create(
                        decodedPixels,
                        info,
                        cancellationToken);
                }
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
            performanceTrace.Mark("PairConstruction");

            ImageServiceHelpers.LogPerformance(
                nameof(RawBaseLoader),
                preview ? nameof(LoadPreviewBaseWithOutcome) : nameof(LoadFullBase),
                stopwatch.ElapsedMilliseconds,
                file.FilePath,
                preview
                    ? $"size={pair!.Interactive.Pixels.Width}x{pair.Interactive.Pixels.Height};" +
                      $"large={pair.Large!.Pixels.Width}x{pair.Large.Pixels.Height};" +
                      $"lens={applyLens};dcp={dcp?.IsActive == true};" +
                      $"decode={effectiveDecode.CacheKey}"
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
            interactivePixels?.Dispose();
            pixels?.Dispose();
        }
    }

}
