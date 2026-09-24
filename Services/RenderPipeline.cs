using System.Diagnostics;
using HappyPhoton.Models;
using ImageMagick;
using static HappyPhoton.Services.RenderKernelSupport;

namespace HappyPhoton.Services;

public sealed class RenderPipeline
{
    public const int Version = 14;

    public RenderResult Render(RenderRequest request) =>
        RenderCore(request, DefaultBandPixelLimit, null);

    internal RenderResult Render(
        RenderRequest request,
        int detailBandPixelLimit)
        => RenderCore(request, detailBandPixelLimit, detailBandPixelLimit);

    private static RenderResult RenderCore(
        RenderRequest request,
        int detailBandPixelLimit,
        int? noiseReductionBandPixelLimit)
    {
        Validate(request);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(detailBandPixelLimit);
        if (noiseReductionBandPixelLimit is { } noiseBandPixelLimit)
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(noiseBandPixelLimit);

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
            var histogram = request.Options.ComputeHistogram || request.Options.ComputeWaveform
                ? new HistogramData()
                : null;
            var preparePixels = request.Options.PreparePreviewPixels || histogram != null;
            var retainEncodedFrame = analyze || preparePixels;
            displayRec2020 = RenderDisplayRec2020Core(
                request,
                noiseReductionBandPixelLimit,
                null,
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
                retainEncodedFrame,
                out var encodedFrame,
                detailBandPixelLimit,
                request.Settings.Effects);
            byte[]? previewPixels = null;
            var analysis = new ClippingAnalysis(ClippingStats.Empty, null);
            if (encodedFrame is { } frame)
            {
                var probe = RenderStageProbe.Begin();
                var width = checked((int)display.Width);
                var height = checked((int)display.Height);
                var pixels = preparePixels
                    ? GC.AllocateUninitializedArray<byte>(checked(width * height * 4)) : null;
                var sourceSaturation = analyze
                    ? SourceSaturationMaskProjector.Project(
                        request.SourceSaturation, request.Settings, geometry, width, height)
                    : null;
                analysis = ClippingStatsCalculator.Analyze(
                    frame, width, height, sourceSaturation, createOverlay,
                    requestedOverlaySides, pixels, analyze);
                if (histogram != null)
                {
                    HistogramService.CalculatePreviewHistogram(
                        pixels!, width, height, histogram, request.Options.ComputeWaveform,
                        scaleWorkersWithFrame: true);
                }
                if (request.Options.PreparePreviewPixels) previewPixels = pixels;
                RenderStageProbe.End(probe, "analysis", display);
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
            displayRec2020 = RenderDisplayRec2020Core(
                request, null, execution, out _);
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
            null,
            null,
            out _);
    }

    private static MagickImage RenderDisplayRec2020Core(
        RenderRequest request,
        int? noiseReductionBandPixelLimit,
        RenderExecutionOptions? execution,
        out RenderGeometryTrace geometry)
    {
        MagickImage? working = null;
        try
        {
            execution?.ThrowIfCancellationRequested();
            execution?.ReportStage("geometry");
            var probe = RenderStageProbe.Begin();
            working = RenderGeometry.Apply(
                request.Base.Pixels,
                request.Settings,
                out geometry);
            RenderStageProbe.End(probe, "geometry", working);
            execution?.ThrowIfCancellationRequested();
            probe = RenderStageProbe.Begin();
            var locals = RenderLocals.Create(request.Settings, geometry,
                (int)working.Width, (int)working.Height, request.LocalsFrameOverride, request.Base.Info);
            RenderStageProbe.End(probe, "locals", working);
            probe = RenderStageProbe.Begin();
            if (request.Base.Info.IsRawSource)
            {
                execution?.ReportStage("raw-crossing");
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
                    execution, locals);
                RenderStageProbe.End(probe, "raw-crossing-setup", working);
                probe = RenderStageProbe.Begin();
                crossing.Apply(working, execution);
                RenderStageProbe.End(probe, "raw-crossing", working);
            }
            else
            {
                execution?.ReportStage("standard-tone");
                ApplyCrossingOffTone(working, request, execution, locals);
                RenderStageProbe.End(probe, "standard-tone", working);
            }
            execution?.ThrowIfCancellationRequested();
            execution?.ReportStage("color-encoding");
            RenderColorEncoding.RetagAsSrgb(working);
            if (!request.Base.Info.IsMonochrome)
            {
                execution?.ReportStage("chroma");
                probe = RenderStageProbe.Begin();
                RenderChromaStage.Apply(working, request.Settings, execution);
                RenderStageProbe.End(probe, "chroma", working);
            }
            execution?.ThrowIfCancellationRequested();
            execution?.ReportStage("noise-reduction");
            probe = RenderStageProbe.Begin();
            RenderNoiseReduction.Apply(
                working,
                request.Base.Info,
                request.Settings.Detail,
                noiseReductionBandPixelLimit,
                execution);
            RenderStageProbe.End(probe, "noise-reduction", working);
            execution?.ThrowIfCancellationRequested();
            execution?.ReportStage("capture-sharpen");
            probe = RenderStageProbe.Begin();
            RenderSharpening.ApplyCapture(
                working,
                request.Base.Info,
                request.Settings.Detail,
                request.Intent,
                execution: execution);
            RenderStageProbe.End(probe, "capture-sharpen", working);
            execution?.ThrowIfCancellationRequested();
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
        RenderRequest request,
        RenderExecutionOptions? execution, RenderLocals? locals)
    {
        var chromatic = RenderChromaticStage.CreateNormalizedMatrix(
            request.Base.Info,
            request.Settings);
        var parameters = new ToneParams(
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
            request.Settings.CurveBlue);
        var probe = RenderStageProbe.Begin();
        var tone = ToneLut.ComposeCached(parameters);
        RenderStageProbe.End(probe, "standard-tone-lut", working);
        execution?.ThrowIfCancellationRequested();
        if (locals == null) ToneLutApplicator.Apply(working, chromatic.Matrix, tone, execution);
        else ToneLutApplicator.ApplyLocals(working, chromatic.Matrix, tone, parameters, locals, execution);
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
