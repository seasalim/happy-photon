using HappyPhoton.Models;
using HappyPhoton.Services;
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
                ChromaNr = 65
            },
            Effects = new EffectsSettings
            {
                Vignette = -37,
                Midpoint = 61,
                Grain = 42,
                GrainSize = GrainSize.Coarse
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

    private static CurveData CreateCurve(double x, double y)
    {
        var curve = new CurveData();
        curve.AddPointAndReturnIndex(x, y);
        return curve;
    }

    private static BaseImage CreatePatternBase(bool isRaw)
    {
        const int width = 64;
        const int height = 48;
        var random = new Random(159);
        var samples = new ushort[width * height * 3];
        for (var index = 0; index < samples.Length; index++)
        {
            samples[index] = checked((ushort)random.Next(512, 64000));
        }

        return RenderPipelineTestSupport.CreateBase(
            samples,
            isRaw,
            height);
    }
}
