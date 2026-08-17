using HappyPhoton.Models;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class RenderDeterminismTests
{
    [Fact]
    public void RepeatedRender_IsBitIdentical()
    {
        using var baseImage = RenderPipelineTestSupport.CreateBase(
            CreateGradient(128, 72),
            isRaw: true,
            height: 72);
        var settings = CreateSettings();
        var request = new RenderRequest(
            baseImage,
            settings,
            RenderIntent.Export,
            null,
            new RenderOptions(false, false));
        var pipeline = new RenderPipeline();

        using var first = pipeline.Render(request);
        using var second = pipeline.Render(request);

        Assert.Equal(
            RenderPipelineTestSupport.ReadPixels(first.Image),
            RenderPipelineTestSupport.ReadPixels(second.Image));
    }

    [Fact]
    public void ByteIdenticalBurstPair_RendersBitIdentically()
    {
        var firstAsset = new GoldenAssetCase(
            "burst-1",
            "nikon-d70-burst-1.nef",
            true,
            false,
            [GoldenTestCases.Identity]);
        var secondAsset = firstAsset with
        {
            Slug = "burst-2",
            FileName = "nikon-d70-burst-2.nef"
        };
        var renderer = new CurrentPipelineGoldenRenderer();

        using var firstBase = renderer.LoadBase(firstAsset);
        using var secondBase = renderer.LoadBase(secondAsset);
        using var first = renderer.Render(
            firstBase,
            GoldenTestCases.Identity.CreateSettings());
        using var second = renderer.Render(
            secondBase,
            GoldenTestCases.Identity.CreateSettings());

        Assert.Equal(
            RenderPipelineTestSupport.ReadPixels(first),
            RenderPipelineTestSupport.ReadPixels(second));
    }

    [Fact]
    public void SettingsHash_MatchesPinnedCanonicalValue()
    {
        const string expected =
            "5948efdbced6f059dc6654051a1b230c4729cff531cfc56c84096545198cb3a5";
        var actual = RenderSettingsHash.Compute(CreateSettings());

        Assert.True(
            actual == expected,
            $"Expected settings hash {expected}, actual {actual}.");
    }

    private static EditSettings CreateSettings()
    {
        var curve = new CurveData();
        curve.AddPointAndReturnIndex(0.25, 0.20);
        curve.AddPointAndReturnIndex(0.75, 0.82);
        return new EditSettings
        {
            Exposure = 1,
            Brightness = 10,
            Contrast = 25,
            Shadows = 35,
            Highlights = -50,
            Curve = curve
        };
    }

    private static ushort[] CreateGradient(int width, int height)
    {
        var samples = new ushort[width * height * 3];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var offset = (y * width + x) * 3;
                samples[offset] = (ushort)(x * ushort.MaxValue / (width - 1));
                samples[offset + 1] =
                    (ushort)(y * ushort.MaxValue / (height - 1));
                samples[offset + 2] =
                    (ushort)((x + y) * ushort.MaxValue /
                        (width + height - 2));
            }
        }

        return samples;
    }
}
