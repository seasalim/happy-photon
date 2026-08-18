using System.Diagnostics;
using HappyPhoton.Models;
using ImageMagick;

namespace HappyPhoton.Services;

public sealed class RenderPipeline
{
    public const int Version = 8;

    public RenderResult Render(RenderRequest request) =>
        Render(request, RenderDetail.DefaultBandPixelLimit);

    internal RenderResult Render(
        RenderRequest request,
        int detailBandPixelLimit)
    {
        Validate(request);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(detailBandPixelLimit);

        var stopwatch = Stopwatch.StartNew();
        var rawNearClip = request.Options.ComputeStats ||
            request.Options.ComputeOverlayMasks
            ? ClippingStatsCalculator.CalculateRawNearClip(request.Base)
            : 0;

        MagickImage? working = null;
        MagickImage? display = null;
        MagickImage? overlay = null;
        long previousElapsed = 0;
        long cloneElapsed = 0;
        long toneElapsed = 0;
        long chromaElapsed = 0;
        long detailElapsed = 0;
        long resizeElapsed = 0;
        long statsElapsed = 0;
        try
        {
            working = (MagickImage)request.Base.Pixels.Clone();
            RenderGeometry.Apply(working, request.Settings);
            cloneElapsed = Lap(stopwatch, ref previousElapsed);

            var chromatic = RenderChromaticStage.CreateNormalizedMatrix(
                request.Base.Info,
                request.Settings);
            var baseLookEnabled = request.Settings.BaseLook ??
                request.Base.Info.IsRawSource;
            var tone = ToneLut.Compose(new ToneParams(
                request.Settings.Exposure +
                    request.Base.Info.SourceExposureBiasEv,
                chromatic.Fold,
                request.Settings.Brightness,
                request.Settings.Contrast,
                request.Settings.Shadows,
                request.Settings.Highlights,
                baseLookEnabled,
                request.Settings.Curve));
            ToneLutApplicator.Apply(working, chromatic.Matrix, tone);
            RenderColorEncoding.RetagAsSrgb(working);
            toneElapsed = Lap(stopwatch, ref previousElapsed);
            RenderChromaStage.Apply(working, request.Settings);
            chromaElapsed = Lap(stopwatch, ref previousElapsed);
            RenderSharpening.ApplyCapture(
                working,
                request.Base.Info,
                request.Settings.Detail);
            RenderDetail.Apply(
                working,
                request.Base.Info,
                request.Settings.Detail,
                detailBandPixelLimit);
            detailElapsed = Lap(stopwatch, ref previousElapsed);

            if (request.MaxDimension is { } maxDimension)
            {
                RenderColorEncoding.ResizeInLinearLight(
                    working,
                    maxDimension);
            }
            resizeElapsed = Lap(stopwatch, ref previousElapsed);

            display = working;
            working = null;

            var createOverlay =
                request.Intent == RenderIntent.Preview &&
                request.Options.ComputeOverlayMasks;
            var analyze = request.Options.ComputeStats || createOverlay;
            var analysis = analyze
                ? ClippingStatsCalculator.Analyze(
                    display,
                    rawNearClip,
                    createOverlay)
                : new ClippingAnalysis(ClippingStats.Empty, null);
            overlay = analysis.OverlayMask;
            statsElapsed = Lap(stopwatch, ref previousElapsed);

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
                $"size={result.Image.Width}x{result.Image.Height};" +
                $"clone={cloneElapsed};tone={toneElapsed};" +
                $"chroma={chromaElapsed};detail={detailElapsed};" +
                $"resize={resizeElapsed};" +
                $"stats={statsElapsed}");
            return result;
        }
        finally
        {
            overlay?.Dispose();
            display?.Dispose();
            working?.Dispose();
        }
    }

    private static long Lap(Stopwatch stopwatch, ref long previousElapsed)
    {
        var elapsed = stopwatch.ElapsedMilliseconds;
        var result = elapsed - previousElapsed;
        previousElapsed = elapsed;
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
    }
}
