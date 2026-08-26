using System.Diagnostics;
using HappyPhoton.Models;
using ImageMagick;

namespace HappyPhoton.Services;

public sealed class RenderPipeline
{
    public const int Version = 11;

    public RenderResult Render(RenderRequest request) =>
        Render(request, RenderDetail.DefaultBandPixelLimit);

    internal RenderResult Render(
        RenderRequest request,
        int detailBandPixelLimit)
    {
        Validate(request);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(detailBandPixelLimit);

        var stopwatch = Stopwatch.StartNew();
        var requestedOverlaySides = request.Options.OverlaySides;

        MagickImage? displayRec2020 = null;
        MagickImage? display = null;
        ClippingMask? overlay = null;
        try
        {
            var createOverlay =
                request.Intent == RenderIntent.Preview &&
                request.Options.ComputeOverlayMasks &&
                requestedOverlaySides != ClippingOverlaySide.None;
            var analyze = request.Options.ComputeStats || createOverlay;
            displayRec2020 = RenderDisplayRec2020Core(
                request,
                detailBandPixelLimit,
                out var geometry);
            display = RenderFinalizer.FinalizeOwned(
                Take(ref displayRec2020),
                request.MaxDimension,
                request.Intent == RenderIntent.Preview
                    ? OutputColorSpace.Srgb
                    : request.OutputColorSpace,
                request.Intent == RenderIntent.Preview
                    ? OutputSharpeningMode.Off
                    : request.OutputSharpening,
                wasResized: false,
                detailBandPixelLimit,
                request.Settings.Effects);
            var histogram = request.Options.ComputeHistogram ||
                request.Options.ComputeWaveform
                ? new HistogramData()
                : null;
            byte[]? previewPixels = null;
            var analysis = new ClippingAnalysis(ClippingStats.Empty, null);
            if (analyze || histogram != null ||
                request.Options.PreparePreviewPixels)
            {
                Parallel.Invoke(
                    () =>
                    {
                        if (analyze)
                        {
                            var sourceSaturation =
                                SourceSaturationMaskProjector.Project(
                                    request.SourceSaturation,
                                    request.Settings,
                                    geometry,
                                    checked((int)display.Width),
                                    checked((int)display.Height));
                            analysis = ClippingStatsCalculator.Analyze(
                                display,
                                sourceSaturation,
                                createOverlay,
                                requestedOverlaySides);
                        }
                    },
                    () =>
                    {
                        if (request.Options.PreparePreviewPixels ||
                            histogram != null)
                        {
                            var pixels = BitmapConversionService
                                .CopyBgraPixels(display);
                            if (histogram != null)
                            {
                                HistogramService.CalculatePreviewHistogram(
                                    pixels,
                                    checked((int)display.Width),
                                    checked((int)display.Height),
                                    histogram,
                                    request.Options.ComputeWaveform);
                            }
                            if (request.Options.PreparePreviewPixels)
                            {
                                previewPixels = pixels;
                            }
                        }
                        else
                        {
                            previewPixels = null;
                        }
                    });
            }
            overlay = analysis.OverlayMask;

            var result = new RenderResult(
                display,
                analysis.Stats,
                overlay,
                histogram,
                previewPixels);
            display = null;
            overlay = null;
            ImageServiceHelpers.LogPerformance(
                nameof(RenderPipeline),
                nameof(Render),
                stopwatch.ElapsedMilliseconds,
                $"intent={request.Intent}",
                $"size={result.Image.Width}x{result.Image.Height}");
            return result;
        }
        finally
        {
            overlay?.Dispose();
            display?.Dispose();
            displayRec2020?.Dispose();
        }
    }

    internal RenderResult RenderResting(
        RenderRequest request,
        RenderExecutionOptions execution)
    {
        Validate(request);
        execution.ThrowIfCancellationRequested();

        MagickImage? displayRec2020 = null;
        MagickImage? display = null;
        try
        {
            displayRec2020 = RenderDisplayRec2020Resting(request, execution);
            execution.ThrowIfCancellationRequested();
            execution.ReportStage("finalization");
            display = RenderFinalizer.FinalizeOwnedResting(
                Take(ref displayRec2020),
                request.MaxDimension,
                request.Intent == RenderIntent.Preview
                    ? OutputColorSpace.Srgb
                    : request.OutputColorSpace,
                request.Settings.Effects,
                execution);
            execution.ThrowIfCancellationRequested();
            var result = new RenderResult(
                display,
                ClippingStats.Empty,
                overlayMask: null);
            display = null;
            return result;
        }
        finally
        {
            display?.Dispose();
            displayRec2020?.Dispose();
        }
    }

    private static MagickImage RenderDisplayRec2020Resting(
        RenderRequest request,
        RenderExecutionOptions execution)
    {
        MagickImage? working = null;
        try
        {
            execution.ThrowIfCancellationRequested();
            execution.ReportStage("geometry");
            working = RenderGeometry.Apply(
                request.Base.Pixels,
                request.Settings,
                out _);
            execution.ThrowIfCancellationRequested();
            if (request.Base.Info.IsRawSource)
            {
                execution.ReportStage("raw-crossing");
                var whiteBalance = RenderChromaticStage.CreateWhiteBalanceMatrix(
                    request.Base.Info,
                    request.Settings);
                var crossing = new AgxCrossing(
                    new AgxToneParameters(
                        request.Settings.Exposure,
                        request.Base.Info.SourceExposureBiasEv,
                        request.Settings.Contrast,
                        request.Settings.Highlights,
                        request.Settings.Shadows,
                        request.Settings.Curve,
                        ColorCurve(request, request.Settings.CurveRed),
                        ColorCurve(request, request.Settings.CurveGreen),
                        ColorCurve(request, request.Settings.CurveBlue)),
                    whiteBalance,
                    request.Base.Info.IsMonochrome
                        ? null
                        : request.Base.Info.DcpProfile?.HueSatMap,
                    execution);
                crossing.Apply(working, execution);
            }
            else
            {
                execution.ReportStage("standard-tone");
                ApplyCrossingOffToneResting(working, request, execution);
            }
            execution.ThrowIfCancellationRequested();
            execution.ReportStage("color-encoding");
            RenderColorEncoding.RetagAsSrgb(working);
            if (!request.Base.Info.IsMonochrome)
            {
                execution.ReportStage("chroma");
                RenderChromaStage.Apply(working, request.Settings, execution);
            }
            execution.ThrowIfCancellationRequested();
            execution.ReportStage("luminance-nr");
            RenderNoiseReduction.ApplyResting(
                working,
                request.Base.Info,
                request.Settings.Detail,
                execution);
            execution.ThrowIfCancellationRequested();
            execution.ReportStage("capture-sharpen");
            RenderSharpening.ApplyCaptureResting(
                working,
                request.Base.Info,
                request.Settings.Detail,
                execution);
            execution.ReportStage("detail");
            RenderDetail.ApplyResting(
                working,
                request.Base.Info,
                request.Settings.Detail,
                execution);
            execution.ThrowIfCancellationRequested();
            var result = working;
            working = null;
            return result;
        }
        finally
        {
            working?.Dispose();
        }
    }

    private static void ApplyCrossingOffToneResting(
        MagickImage working,
        RenderRequest request,
        RenderExecutionOptions execution)
    {
        var chromatic = RenderChromaticStage.CreateNormalizedMatrix(
            request.Base.Info,
            request.Settings);
        var tone = ToneLut.ComposeCached(new ToneParams(
            request.Settings.Exposure +
                request.Base.Info.SourceExposureBiasEv,
            chromatic.Fold,
            request.Settings.Brightness,
            request.Settings.Contrast,
            request.Settings.Shadows,
            request.Settings.Highlights,
            request.Settings.BaseLook ?? false,
            request.Settings.Curve,
            request.Settings.CurveRed,
            request.Settings.CurveGreen,
            request.Settings.CurveBlue));
        execution.ThrowIfCancellationRequested();
        ToneLutApplicator.ApplyResting(
            working,
            chromatic.Matrix,
            tone,
            execution);
    }

    internal MagickImage RenderDisplayRec2020(RenderRequest request)
    {
        Validate(request);
        if (request.MaxDimension != null)
        {
            throw new ArgumentException(
                "The shared display-Rec.2020 render must remain unresized.",
                nameof(request));
        }
        return RenderDisplayRec2020Core(
            request,
            RenderDetail.DefaultBandPixelLimit,
            out _);
    }

    private static MagickImage RenderDisplayRec2020Core(
        RenderRequest request,
        int detailBandPixelLimit,
        out RenderGeometryTrace geometry)
    {
        MagickImage? working = null;
        try
        {
            working = RenderGeometry.Apply(
                request.Base.Pixels,
                request.Settings,
                out geometry);
            if (request.Base.Info.IsRawSource)
            {
                var whiteBalance = RenderChromaticStage.CreateWhiteBalanceMatrix(
                    request.Base.Info,
                    request.Settings);
                var crossing = new AgxCrossing(
                    new AgxToneParameters(
                        request.Settings.Exposure,
                        request.Base.Info.SourceExposureBiasEv,
                        request.Settings.Contrast,
                        request.Settings.Highlights,
                        request.Settings.Shadows,
                        request.Settings.Curve,
                        ColorCurve(request, request.Settings.CurveRed),
                        ColorCurve(request, request.Settings.CurveGreen),
                        ColorCurve(request, request.Settings.CurveBlue)),
                    whiteBalance,
                    request.Base.Info.IsMonochrome
                        ? null
                        : request.Base.Info.DcpProfile?.HueSatMap);
                crossing.Apply(working);
            }
            else
            {
                ApplyCrossingOffTone(working, request);
            }
            RenderColorEncoding.RetagAsSrgb(working);
            if (!request.Base.Info.IsMonochrome)
                RenderChromaStage.Apply(working, request.Settings);
            RenderNoiseReduction.Apply(
                working,
                request.Base.Info,
                request.Settings.Detail,
                detailBandPixelLimit);
            RenderSharpening.ApplyCapture(
                working,
                request.Base.Info,
                request.Settings.Detail);
            RenderDetail.Apply(
                working,
                request.Base.Info,
                request.Settings.Detail,
                detailBandPixelLimit);
            var result = working;
            working = null;
            return result;
        }
        finally
        {
            working?.Dispose();
        }
    }

    private static void ApplyCrossingOffTone(
        MagickImage working,
        RenderRequest request)
    {
        var chromatic = RenderChromaticStage.CreateNormalizedMatrix(
            request.Base.Info,
            request.Settings);
        var tone = ToneLut.ComposeCached(new ToneParams(
            request.Settings.Exposure +
                request.Base.Info.SourceExposureBiasEv,
            chromatic.Fold,
            request.Settings.Brightness,
            request.Settings.Contrast,
            request.Settings.Shadows,
            request.Settings.Highlights,
            request.Settings.BaseLook ?? false,
            request.Settings.Curve,
            request.Settings.CurveRed,
            request.Settings.CurveGreen,
            request.Settings.CurveBlue));
        ToneLutApplicator.Apply(working, chromatic.Matrix, tone);
    }

    private static MagickImage Take(ref MagickImage? image)
    {
        var result = image ?? throw new InvalidOperationException(
            "The display render was already consumed.");
        image = null;
        return result;
    }

    private static CurveData? ColorCurve(
        RenderRequest request,
        CurveData? curve) => request.Base.Info.IsMonochrome ? null : curve;

    private static void Validate(RenderRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Base);
        ArgumentNullException.ThrowIfNull(request.Settings);
        ArgumentNullException.ThrowIfNull(request.Options);
        if (request.MaxDimension is <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                "MaxDimension must be positive when provided.");
        }
        if (!Enum.IsDefined(request.OutputColorSpace))
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                "OutputColorSpace is not supported.");
        }
        if (!Enum.IsDefined(request.OutputSharpening))
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                "OutputSharpening is not supported.");
        }
    }
}
