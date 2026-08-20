using System.Diagnostics;
using HappyPhoton.Models;
using ImageMagick;

namespace HappyPhoton.Services;

public sealed class RenderPipeline
{
    public const int Version = 9;

    public RenderResult Render(RenderRequest request) =>
        Render(request, RenderDetail.DefaultBandPixelLimit);

    internal RenderResult Render(
        RenderRequest request,
        int detailBandPixelLimit)
    {
        Validate(request);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(detailBandPixelLimit);

        var stopwatch = Stopwatch.StartNew();
        var requestedOverlaySides = request.Base.Info.IsRawSource
            ? request.Options.OverlaySides
            : request.Options.OverlaySides & ClippingOverlaySide.DisplayFloor;
        var rawNearClip = request.Options.ComputeStats ||
            request.Options.ComputeOverlayMasks
            ? ClippingStatsCalculator.CalculateRawNearClip(request.Base)
            : 0;

        MagickImage? displayRec2020 = null;
        MagickImage? display = null;
        MagickImage? overlay = null;
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
                analyze,
                createOverlay && requestedOverlaySides.HasFlag(
                    ClippingOverlaySide.SceneHighlights),
                out var sceneHighlights);
            display = RenderFinalizer.FinalizeOwned(
                Take(ref displayRec2020),
                request.MaxDimension,
                request.Intent == RenderIntent.Preview
                    ? OutputColorSpace.Srgb
                    : request.OutputColorSpace,
                outputSharpening: false,
                wasResized: false,
                detailBandPixelLimit);

            var analysis = analyze
                ? ClippingStatsCalculator.Analyze(
                    display,
                    rawNearClip,
                    createOverlay,
                    sceneHighlights,
                    requestedOverlaySides)
                : new ClippingAnalysis(ClippingStats.Empty, null);
            overlay = analysis.OverlayMask;

            var result = new RenderResult(
                display,
                analysis.Stats,
                overlay);
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
            analyzeSceneHighlights: false,
            createSceneMask: false,
            out _);
    }

    private static MagickImage RenderDisplayRec2020Core(
        RenderRequest request,
        int detailBandPixelLimit,
        bool analyzeSceneHighlights,
        bool createSceneMask,
        out SceneHighlightAnalysis? sceneHighlights)
    {
        sceneHighlights = null;
        MagickImage? working = null;
        try
        {
            working = (MagickImage)request.Base.Pixels.Clone();
            RenderGeometry.Apply(working, request.Settings);
            if (request.Base.Info.IsRawSource)
            {
                var whiteBalance = RenderChromaticStage.CreateWhiteBalanceMatrix(
                    request.Base.Info,
                    request.Settings);
                if (analyzeSceneHighlights)
                {
                    sceneHighlights =
                        ClippingStatsCalculator.AnalyzeSceneHighlights(
                            working,
                            whiteBalance,
                            request.Settings.Exposure +
                                request.Base.Info.SourceExposureBiasEv,
                            createSceneMask);
                }

                var crossing = new AgxCrossing(
                    new AgxToneParameters(
                        request.Settings.Exposure,
                        request.Base.Info.SourceExposureBiasEv,
                        request.Settings.Contrast,
                        request.Settings.Highlights,
                        request.Settings.Shadows,
                        request.Settings.Curve,
                        request.Settings.CurveRed,
                        request.Settings.CurveGreen,
                        request.Settings.CurveBlue),
                    whiteBalance);
                crossing.Apply(working);
            }
            else
            {
                ApplyCrossingOffTone(working, request);
            }
            RenderColorEncoding.RetagAsSrgb(working);
            RenderChromaStage.Apply(working, request.Settings);
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
        var tone = ToneLut.Compose(new ToneParams(
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
    }
}
