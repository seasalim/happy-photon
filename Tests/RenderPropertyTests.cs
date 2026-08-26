using HappyPhoton.Models;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

internal static class RenderPropertyCases
{
    public const int Seed = 147_000;
    public const int AchromaticDrawCount = 64;
    public const int ContrastDrawCount = 16;
    public const int ContrastMinimum = -100;
    public const int ContrastMaximum = 100;
    public const int Q16Tolerance = 2;

    public static IEnumerable<ushort[]> AchromaticImages()
    {
        var random = new Random(Seed);
        for (var draw = 0; draw < AchromaticDrawCount; draw++)
        {
            var values = new ushort[13 * 11 * 3];
            for (var pixel = 0; pixel < values.Length / 3; pixel++)
            {
                var grey = (ushort)random.Next(ushort.MinValue, ushort.MaxValue + 1);
                values[pixel * 3] = grey;
                values[pixel * 3 + 1] = grey;
                values[pixel * 3 + 2] = grey;
            }
            yield return values;
        }
    }

    public static IEnumerable<int> Contrasts()
    {
        var random = new Random(Seed);
        yield return ContrastMinimum;
        yield return 0;
        yield return ContrastMaximum;
        // The extrema and zero are explicit; thirteen seeded interior draws
        // retain broad slider sampling without multiplying four full renders.
        for (var draw = 3; draw < ContrastDrawCount; draw++)
        {
            yield return random.Next(ContrastMinimum, ContrastMaximum + 1);
        }
    }
}

public sealed class RenderPropertyTests
{
    [Theory]
    [InlineData(OutputColorSpace.Srgb)]
    [InlineData(OutputColorSpace.DisplayP3)]
    public void RawDefaults_PreserveAchromaticInputs(
        OutputColorSpace outputColorSpace)
    {
        var pipeline = new RenderPipeline();
        var draw = 0;
        foreach (var samples in RenderPropertyCases.AchromaticImages())
        {
            using var baseImage = RenderPipelineTestSupport.CreateBase(
                samples,
                isRaw: true,
                height: 11);
            var settings = new EditSettings
            {
                Wb = new WhiteBalanceSettings
                {
                    Mode = WbMode.Picked,
                    Gains = [1, 1, 1]
                }
            };
            using var result = pipeline.Render(new RenderRequest(
                baseImage,
                settings,
                RenderIntent.Export,
                MaxDimension: null,
                new RenderOptions(false, false),
                outputColorSpace));
            var actual = RenderPipelineTestSupport.ReadPixels(result.Image);
            for (var pixel = 0; pixel < actual.Length / 3; pixel++)
            {
                Assert.True(
                    actual[pixel * 3] == actual[pixel * 3 + 1] &&
                    actual[pixel * 3 + 1] == actual[pixel * 3 + 2],
                    $"Seed {RenderPropertyCases.Seed}, draw {draw}, pixel {pixel} " +
                    "did not remain achromatic through RAW defaults.");
            }
            draw++;
        }
    }

    [Fact]
    public void RawContrast_PreservesPostGainSceneMiddleGrey()
    {
        var expected = (ushort)Math.Round(
            ToneLut.SrgbEncode(AgxToneEngine.MiddleGrey) * ushort.MaxValue,
            MidpointRounding.AwayFromZero);
        var pipeline = new RenderPipeline();

        foreach (var sourceExposure in new[] { -2.0, -0.75, 0.625, 2.0 })
        foreach (var contrast in RenderPropertyCases.Contrasts())
        {
            var source = (ushort)Math.Round(
                AgxToneEngine.MiddleGrey * Math.Pow(2, -sourceExposure) *
                ushort.MaxValue,
                MidpointRounding.AwayFromZero);
            var samples = Enumerable.Repeat(source, 9 * 9 * 3).ToArray();
            using var baseImage = RenderPipelineTestSupport.CreateBase(
                samples,
                isRaw: true,
                height: 9,
                sourceBiasEv: sourceExposure);
            var settings = new EditSettings
            {
                Contrast = contrast,
                Detail = new DetailSettings { CaptureSharpen = 0 },
                Wb = new WhiteBalanceSettings
                {
                    Mode = WbMode.Picked,
                    Gains = [1, 1, 1]
                }
            };
            using var result = pipeline.Render(new RenderRequest(
                baseImage,
                settings,
                RenderIntent.Export,
                MaxDimension: null,
                new RenderOptions(false, false)));
            var actual = RenderPipelineTestSupport.ReadPixels(result.Image);
            Assert.All(actual, value => Assert.InRange(
                value,
                expected - 3,
                expected + 3));
        }
    }
}
