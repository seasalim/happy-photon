using HappyPhoton.Models;
using HappyPhoton.Services;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class RenderRepairsTests
{
    [Fact]
    public void EmptyStageNeverAccessesPixelsOrExecution()
    {
        var execution = RenderExecutionOptions.Resting(CancellationToken.None,
            stageStarted: _ => Assert.Fail("Bypass entered the stage"),
            cancellationObserved: () => Assert.Fail("Bypass performed work"));
        var stage = new RenderRepairs();
        Assert.False(stage.Apply(null!, null, execution));
        Assert.False(stage.Apply(null!, [], execution));
        Assert.Equal(0, stage.RetainedScratchBytes);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void LargeSpotIsIdenticalAcrossWorkerCapsAndPreservesAlpha(bool alpha)
    {
        using var basis = Fixture(800, 600);
        using var source = new MagickImage(basis.Pixels);
        if (alpha) source.Alpha(AlphaOption.On);
        using var originalPixels = source.GetPixels();
        var original = originalPixels.ToShortArray(alpha ? PixelMapping.RGBA : PixelMapping.RGB)!;
        ushort[]? first = null;
        foreach (var workers in new[] { 1, 2, Environment.ProcessorCount })
        {
            using var image = new MagickImage(source);
            new RenderRepairs().Apply(image, [new() { Radius = .1, Su = .2, Sv = .3 }],
                RenderExecutionOptions.Resting(CancellationToken.None, workers));
            using var pixels = image.GetPixels();
            var codes = pixels.ToShortArray(alpha ? PixelMapping.RGBA : PixelMapping.RGB)!;
            Assert.NotEqual(original, codes);
            if (first != null) Assert.Equal(first, codes);
            first = codes;
            if (alpha) for (var i = 3; i < codes.Length; i += 4) Assert.Equal(original[i], codes[i]);
        }
        using var unchanged = source.GetPixels();
        Assert.Equal(original, unchanged.ToShortArray(alpha ? PixelMapping.RGBA : PixelMapping.RGB));
    }

    [Fact]
    public async Task ConcurrentSharedStageMatchesIsolatedStagesAndRetainsLargestCapacity()
    {
        var shared = new RenderRepairs();
        var jobs = new[] { 800, 400, 600, 200 }.Select(width => Task.Run(() =>
        {
            using var basis = Fixture(width, width * 3 / 4);
            var repairs = HealWorkloads.Repairs(HealWorkloads.S64());
            var isolated = new RenderRepairs();
            using var expected = new MagickImage(basis.Pixels);
            using var actual = new MagickImage(basis.Pixels);
            isolated.Apply(expected, repairs);
            shared.Apply(actual, repairs);

            Assert.Equal(RenderPipelineTestSupport.ReadPixels(expected),
                RenderPipelineTestSupport.ReadPixels(actual));
            return isolated.RetainedScratchBytes;
        })).ToArray();

        var capacities = await Task.WhenAll(jobs);
        Assert.Equal(capacities.Max(), shared.RetainedScratchBytes);
    }

    [Fact]
    public void MidStageCancellationLeavesTheImmutableBaseUntouched()
    {
        using var basis = Fixture(800, 600);
        var before = RenderPipelineTestSupport.ReadPixels(basis.Pixels);
        using var cancellation = new CancellationTokenSource();
        var observations = 0;
        var execution = RenderExecutionOptions.Resting(cancellation.Token, 2,
            cancellationObserved: () => { if (Interlocked.Increment(ref observations) == 20) cancellation.Cancel(); });
        using var copy = new MagickImage(basis.Pixels);
        Assert.ThrowsAny<OperationCanceledException>(() => new RenderRepairs().Apply(copy,
            [new() { Radius = .1, Su = .2 }], execution));
        Assert.True(observations >= 20);
        Assert.Equal(before, RenderPipelineTestSupport.ReadPixels(basis.Pixels));
        // A subsequent render still starts from the untouched base.
        using var completed = RenderGeometry.ApplyCanonicalBase(basis.Pixels, new(),
            [new() { Radius = .1, Su = .2 }], out _);
        Assert.NotEqual(before, RenderPipelineTestSupport.ReadPixels(completed));
    }

    [Theory]
    [InlineData(1600)] [InlineData(3200)] [InlineData(4000)]
    public void StoredOutOfFrameSourceEqualsSharedClampAtEverySize(int width)
    {
        using var basis = Fixture(width, width / 2);
        var spot = new Repair { U = .6, V = .5, Su = 0, Sv = 1, Radius = .03 };
        var (u, v) = RepairGeometry.ClampSource(spot, width, width / 2);
        using var stored = new MagickImage(basis.Pixels);
        using var clamped = new MagickImage(basis.Pixels);
        var stage = new RenderRepairs();
        stage.Apply(stored, [spot]); stage.Apply(clamped, [spot with { Su = u, Sv = v }]);
        Assert.Equal(RenderPipelineTestSupport.ReadPixels(stored), RenderPipelineTestSupport.ReadPixels(clamped));
        Assert.Equal(0, spot.Su); Assert.Equal(1, spot.Sv);
    }

    [Fact]
    public void Area64PinsTheAreaAndEveryNeighbourOverlap()
    {
        var spots = HealWorkloads.SArea64(1600, 1068);
        Assert.Equal(64, spots.Length);
        Assert.All(spots, s => Assert.False(s.IsClone));
        Assert.Equal(Repair.MaximumArea, HealWorkloads.Area(spots), 12);
        foreach (var (a, b) in spots.Zip(spots.Skip(1)))
        {
            var distance = Math.Sqrt(Math.Pow((a.U - b.U) * 1600, 2) + Math.Pow((a.V - b.V) * 1068, 2)) / (a.Radius * 1600);
            var overlap = (2 * Math.Acos(distance / 2) - distance * Math.Sqrt(4 - distance * distance) / 2) / Math.PI;
            Assert.Equal(.5, overlap, 12);
        }
    }

    [Theory]
    [InlineData(0, 0)] [InlineData(90, 0)] [InlineData(270, 3)]
    public void CachedBaseRemainsImmutableAndRepairsPrecedeAllGeometry(int rotation, double horizon)
    {
        using var basis = Fixture();
        var original = RenderPipelineTestSupport.ReadPixels(basis.Pixels);
        var settings = RepairRenderEntryTests.Settings();
        settings.Rotation = rotation; settings.HorizonRotation = horizon;
        settings.Geometry = horizon == 0 ? null : new() { Vertical = 20 };
        using var oracle = RenderPipelineTestSupport.CreateBase(HealOracle.Apply(original, 160, 120,
            [new(.6, .5, .2, .3, .1, true, 0)], default), height: 120);
        var neutral = settings.Clone(); neutral.Repairs = null;
        var pipeline = new RenderPipeline();
        using var actual = pipeline.Render(new(basis, settings, RenderIntent.Preview, null, new(false, false)));
        using var expected = pipeline.Render(new(oracle, neutral, RenderIntent.Preview, null, new(false, false)));
        Assert.Equal(RenderPipelineTestSupport.ReadPixels(expected.Image), RenderPipelineTestSupport.ReadPixels(actual.Image));
        Assert.Equal(original, RenderPipelineTestSupport.ReadPixels(basis.Pixels));
        using var off = pipeline.Render(new(basis, neutral, RenderIntent.Preview, null, new(false, false)));
        Assert.NotEqual(RenderPipelineTestSupport.ReadPixels(off.Image), RenderPipelineTestSupport.ReadPixels(actual.Image));
    }

    [Fact]
    public void RepairFreeGoldenSettingsBypassBitExactly()
    {
        using var basis = Fixture();
        var pipeline = new RenderPipeline();
        Assert.Equal(14, RenderPipeline.Version);
        foreach (var asset in GoldenTestCases.Assets)
        foreach (var item in asset.SettingsCases)
        {
            var settings = item.CreateSettings();
            using var original = pipeline.Render(new(basis, settings, RenderIntent.Export, null, new(false, false)));
            settings.Repairs = [];
            using var bypass = pipeline.Render(new(basis, settings, RenderIntent.Export, null, new(false, false)));
            Assert.Equal(RenderPipelineTestSupport.ReadPixels(original.Image), RenderPipelineTestSupport.ReadPixels(bypass.Image));
        }
    }

    internal static BaseImage Fixture(int width = 160, int height = 120)
    {
        var values = new ushort[width * height * 3];
        for (var y = 0; y < height; y++) for (var x = 0; x < width; x++)
        {
            var i = (y * width + x) * 3;
            values[i] = (ushort)(5000 + x * 40000 / width);
            values[i + 1] = (ushort)(8000 + y * 30000 / height);
            values[i + 2] = (ushort)(10000 + (x + y) * 10000 / (width + height));
        }
        return RenderPipelineTestSupport.CreateBase(values, height: height);
    }
}
