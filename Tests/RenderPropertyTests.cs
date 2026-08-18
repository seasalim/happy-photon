using HappyPhoton.Models;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

internal static class RenderPropertyCases
{
    public const int Seed = 147_000;
    public const int DrawCount = 64;
    public const int ContrastMinimum = -100;
    public const int ContrastMaximum = 100;
    public const int Q16Tolerance = 2;

    public static IEnumerable<ushort[]> AchromaticImages()
    {
        var random = new Random(Seed);
        for (var draw = 0; draw < DrawCount; draw++)
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
        for (var draw = 3; draw < DrawCount; draw++)
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
    public void Contrast_PreservesCurrentDisplayPivotAnalogue()
    {
        var linearDisplayPivot = DecodeSrgb(0.5);
        var source = (ushort)Math.Round(
            linearDisplayPivot * ushort.MaxValue,
            MidpointRounding.AwayFromZero);
        var samples = Enumerable.Repeat(source, 9 * 9 * 3).ToArray();
        var expected = (ushort)Math.Round(
            0.5 * ushort.MaxValue,
            MidpointRounding.AwayFromZero);
        var pipeline = new RenderPipeline();

        foreach (var contrast in RenderPropertyCases.Contrasts())
        {
            using var baseImage = RenderPipelineTestSupport.CreateBase(
                samples,
                height: 9);
            var settings = new EditSettings
            {
                BaseLook = false,
                Contrast = contrast,
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
                expected - RenderPropertyCases.Q16Tolerance,
                expected + RenderPropertyCases.Q16Tolerance));
        }
    }

    private static double DecodeSrgb(double value) => value <= 0.04045
        ? value / 12.92
        : Math.Pow((value + 0.055) / 1.055, 2.4);
}
