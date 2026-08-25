using HappyPhoton.Models;
using HappyPhoton.Services;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class RestingRenderExecutionTests
{
    [Theory]
    [InlineData(false, 1)]
    [InlineData(false, 2)]
    [InlineData(true, 1)]
    [InlineData(true, 2)]
    public void RestingExecution_IsBitIdenticalAtEachWorkerCap(
        bool isRaw,
        int workerCap)
    {
        using var baseImage = CreatePatternBase(isRaw);
        var settings = new EditSettings
        {
            Exposure = 0.45,
            Brightness = isRaw ? 0 : 12,
            Contrast = 28,
            Highlights = -31,
            Shadows = 24,
            Saturation = 19,
            Vibrance = 16,
            Detail = new DetailSettings
            {
                CaptureSharpen = 80,
                LuminanceNr = 55,
                ChromaNr = 65
            },
            Effects = new EffectsSettings
            {
                Vignette = -37,
                Midpoint = 61,
                Grain = 42,
                GrainSize = GrainSize.Coarse
            },
            HorizonRotation = 3,
            Geometry = new GeometrySettings
            {
                Vertical = 35,
                Horizontal = -28,
                Aspect = 22,
                Distortion = -45
            },
            Curve = CreateCurve(0.5, 0.55),
            CurveRed = CreateCurve(0.4, 0.47),
            CurveGreen = CreateCurve(0.5, 0.52),
            CurveBlue = CreateCurve(0.6, 0.56)
        };
        var request = new RenderRequest(
            baseImage,
            settings,
            RenderIntent.Preview,
            MaxDimension: null,
            new RenderOptions(false, false));
        var pipeline = new RenderPipeline();

        using var unrestricted = pipeline.Render(request);
        using var resting = pipeline.RenderResting(
            request,
            RenderExecutionOptions.Resting(
                CancellationToken.None,
                workerCap));

        Assert.Equal(unrestricted.Image.Width, resting.Image.Width);
        Assert.Equal(unrestricted.Image.Height, resting.Image.Height);
        Assert.Equal(
            RenderPipelineTestSupport.ReadPixels(unrestricted.Image),
            RenderPipelineTestSupport.ReadPixels(resting.Image));
    }

    [Fact]
    public void RestingExecution_ReportsLuminanceNrBeforeCaptureSharpen()
    {
        using var baseImage = CreatePatternBase(isRaw: false);
        var stages = new List<string>();
        var request = CreateRequest(
            baseImage,
            new EditSettings
            {
                Detail = new DetailSettings
                {
                    LuminanceNr = 70,
                    CaptureSharpen = 80
                }
            });

        using var result = new RenderPipeline().RenderResting(
            request,
            RenderExecutionOptions.Resting(
                CancellationToken.None,
                stageStarted: stages.Add));

        Assert.True(stages.IndexOf("luminance-nr") <
            stages.IndexOf("capture-sharpen"));
        Assert.True(stages.IndexOf("capture-sharpen") <
            stages.IndexOf("detail"));
    }

    [Fact]
    public void StandardRender_AppliesHueSatProfileAndMatchesResting()
    {
        var table = DcpProfileReaderTests.CreateTable(6, 3, 2, 8, 1.1f, 0.92f);
        var map = new DcpHueSatMap(6, 3, 2, true, table, null, 0);
        using var plain = CreatePatternBase(isRaw: true);
        using var profiled = CreatePatternBase(isRaw: true, map);
        var settings = new EditSettings { Exposure = 0.45, Contrast = 28 };
        var pipeline = new RenderPipeline();

        using var baseline = pipeline.Render(CreateRequest(plain, settings));
        using var standard = pipeline.Render(CreateRequest(profiled, settings));
        using var resting = pipeline.RenderResting(
            CreateRequest(profiled, settings),
            RenderExecutionOptions.Resting(CancellationToken.None, 2));

        Assert.NotEqual(
            RenderPipelineTestSupport.ReadPixels(baseline.Image),
            RenderPipelineTestSupport.ReadPixels(standard.Image));
        Assert.Equal(
            RenderPipelineTestSupport.ReadPixels(standard.Image),
            RenderPipelineTestSupport.ReadPixels(resting.Image));
    }

    [Fact]
    public void RestingExecution_ActiveEffectsObserveCancellationAtStageEntry()
    {
        using var baseImage = CreatePatternBase(isRaw: false);
        using var cancellation = new CancellationTokenSource();
        var request = new RenderRequest(
            baseImage,
            new EditSettings
            {
                Effects = new EffectsSettings
                {
                    Vignette = -40,
                    Grain = 35
                }
            },
            RenderIntent.Preview,
            MaxDimension: null,
            new RenderOptions(false, false));
        var execution = RenderExecutionOptions.Resting(
            cancellation.Token,
            maxDegreeOfParallelism: 2,
            stageStarted: stage =>
            {
                if (stage == "effects") cancellation.Cancel();
            });

        Assert.Throws<OperationCanceledException>(() =>
            new RenderPipeline().RenderResting(request, execution));
    }

    [Fact]
    public void RestingExecution_ActiveChromaObservesCancellationAtStageEntry()
    {
        using var baseImage = CreatePatternBase(isRaw: true);
        using var cancellation = new CancellationTokenSource();
        var request = new RenderRequest(
            baseImage,
            new EditSettings { Saturation = 100, Vibrance = 100 },
            RenderIntent.Preview,
            MaxDimension: null,
            new RenderOptions(false, false));
        var execution = RenderExecutionOptions.Resting(
            cancellation.Token,
            maxDegreeOfParallelism: 2,
            stageStarted: stage =>
            {
                if (stage == "chroma") cancellation.Cancel();
            });

        Assert.Throws<OperationCanceledException>(() =>
            new RenderPipeline().RenderResting(request, execution));
    }

    [Fact]
    public void RestingToneLoop_ObservesCancellationBetweenChunkPixels()
    {
        // Two chunks at worker cap 1; the third cancellation observation is
        // the in-loop periodic check at pixel 0 (the first two are the method
        // entry and post-GetArea checks). Canceling there makes a later
        // periodic check throw mid-loop, proving the loop does not run to
        // completion once the token trips.
        const int width = 181;
        const int height = 91;
        using var image = new MagickImage(
            MagickColors.Gray,
            width,
            height);
        var lut = new double[ToneLut.Length];
        for (var index = 0; index < lut.Length; index++)
        {
            lut[index] = index / (double)(ToneLut.Length - 1);
        }
        using var cancellation = new CancellationTokenSource();
        var observed = 0;
        var execution = RenderExecutionOptions.Resting(
            cancellation.Token,
            maxDegreeOfParallelism: 1,
            cancellationObserved: () =>
            {
                if (Interlocked.Increment(ref observed) == 3)
                {
                    cancellation.Cancel();
                }
            });

        Assert.ThrowsAny<OperationCanceledException>(() =>
        {
            ToneLutApplicator.ApplyResting(image, lut, execution);
        });
        Assert.True(observed >= 3);
    }

    private static CurveData CreateCurve(double x, double y)
    {
        var curve = new CurveData();
        curve.AddPointAndReturnIndex(x, y);
        return curve;
    }

    private static RenderRequest CreateRequest(
        BaseImage baseImage,
        EditSettings settings) => new(
            baseImage,
            settings,
            RenderIntent.Preview,
            MaxDimension: null,
            new RenderOptions(false, false));

    private static BaseImage CreatePatternBase(
        bool isRaw,
        DcpHueSatMap? hueSatMap = null)
    {
        // 16,471 pixels: above the 8,192-pixel chunk threshold so the chunked
        // per-pixel loops split into two unequal ranges, and prime so no
        // worker count divides it evenly.
        const int width = 181;
        const int height = 91;
        var random = new Random(159);
        var samples = new ushort[width * height * 3];
        for (var index = 0; index < samples.Length; index++)
        {
            samples[index] = checked((ushort)random.Next(512, 64000));
        }

        return RenderPipelineTestSupport.CreateBase(
            samples,
            isRaw,
            height,
            hueSatMap: hueSatMap);
    }
}
