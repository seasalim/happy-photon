using HappyPhoton.Models;
using HappyPhoton.Services;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class PresenceTests
{
    [Theory]
    [InlineData(100, 0)]
    [InlineData(-100, 0)]
    [InlineData(0, 100)]
    [InlineData(0, -100)]
    [InlineData(40, 40)]
    public void MatchesOracleAndIsExactAcrossWorkersAndBands(int texture, int clarity)
    {
        var source = OpsKernelTests.Sentinel();
        using var basis = RenderPipelineTestSupport.CreateBase(source, height: 31);
        var settings = new EditSettings { Texture = texture, Clarity = clarity };

        foreach (var nativeEdge in new[] { 47, 161 })
        {
            var info = basis.Info with { FullWidth = nativeEdge, FullHeight = nativeEdge * 31 / 47 };
            var oracle = OpsPresenceOracle.Apply(source, 47, 31, nativeEdge,
                new("presence", texture, clarity), OpsClarity.Guided);
            using var unbounded = new MagickImage(basis.Pixels);
            RenderPresence.Apply(unbounded, info, settings);
            var reference = RenderPipelineTestSupport.ReadPixels(unbounded);
            OpsKernelTests.MaxError(oracle, reference, 1);

            foreach (var workers in new[] { 1, 2, Environment.ProcessorCount })
            foreach (var bandLimit in new[] { 1, 47 * 32, int.MaxValue })
            {
                using var image = new MagickImage(basis.Pixels);
                RenderPresence.Apply(image, info, settings, bandLimit,
                    RenderExecutionOptions.Resting(default, workers));
                var actual = RenderPipelineTestSupport.ReadPixels(image);
                OpsKernelTests.MaxError(oracle, actual, 1);
                Assert.Equal(reference, actual);
            }
        }

        Assert.Equal(source, RenderPipelineTestSupport.ReadPixels(basis.Pixels));
    }

    [Fact]
    public void SingleColumnAgreesWithAnUnboundedBandLimit()
    {
        using var basis = RenderPipelineTestSupport.CreateBase(OpsKernelTests.Sentinel(1, 31), height: 31);
        using var expected = new MagickImage(basis.Pixels);
        using var actual = new MagickImage(basis.Pixels);
        var settings = new EditSettings { Texture = 100, Clarity = -100 };
        RenderPresence.Apply(expected, basis.Info, settings, 1);
        RenderPresence.Apply(actual, basis.Info, settings, int.MaxValue);
        Assert.Equal(RenderPipelineTestSupport.ReadPixels(expected), RenderPipelineTestSupport.ReadPixels(actual));
    }

    [Fact]
    public void AreaReducedClarityMatchesPrototype()
    {
        using var basis = RenderPipelineTestSupport.CreateBase(OpsKernelTests.Sentinel(389, 259), height: 259);
        using var expected = new MagickImage(basis.Pixels);
        using var actual = new MagickImage(basis.Pixels);
        OpsPresencePrototype.Apply(expected, basis.Info, new("CL", Clarity: 100), OpsClarity.Guided);
        RenderPresence.Apply(actual, basis.Info, new() { Clarity = 100 });
        OpsKernelTests.MaxError(RenderPipelineTestSupport.ReadPixels(expected),
            RenderPipelineTestSupport.ReadPixels(actual), 1);
    }

    [Theory]
    [InlineData(47, 1, 45)]
    [InlineData(192, 25, 1)]
    public void CroppedConstantLumaSurvivesRoundedCellBoundaries(int width, int left, int cropWidth)
    {
        using var basis = RenderPipelineTestSupport.CreateBase(
            Enumerable.Repeat((ushort)24000, width * 31 * 3).ToArray(), height: 31);
        var settings = new EditSettings
        {
            Crop = new() { Left = left / (double)width, Right = (left + cropWidth) / (double)width },
            Detail = new() { CaptureSharpen = 0 }
        };
        var pipeline = new RenderPipeline();
        using var expected = pipeline.RenderDisplayRec2020(new(basis, settings, RenderIntent.Export, null, new(false)));
        settings.Clarity = 100;
        using var actual = pipeline.RenderDisplayRec2020(new(basis, settings, RenderIntent.Export, null, new(false)));
        Assert.Equal(RenderPipelineTestSupport.ReadPixels(expected), RenderPipelineTestSupport.ReadPixels(actual));
    }

    [Fact]
    public void ZeroBypassesDisposedPixels()
    {
        using var basis = RenderPipelineTestSupport.CreateBase([0, 0, 0]);
        var disposed = new MagickImage(basis.Pixels);
        disposed.Dispose();
        RenderPresence.Apply(disposed, basis.Info, new());
    }

    [Theory]
    [InlineData(40, 0)]
    [InlineData(0, 40)]
    [InlineData(40, 40)]
    public void CancellationAtNextBandLeavesPixelsUnwritten(int texture, int clarity)
    {
        using var basis = RenderPipelineTestSupport.CreateBase(OpsKernelTests.Sentinel(64, 96), height: 96);
        using var image = new MagickImage(basis.Pixels);
        using var cancellation = new CancellationTokenSource();
        var checks = 0;
        var selected = 0;
        var execution = RenderExecutionOptions.Resting(cancellation.Token, 2,
            cancellationObserved: () =>
            {
                if (selected > 0)
                {
                    checks++;
                    cancellation.Cancel();
                }
            }) with
        {
            WorkersSelected = _ =>
            {
                selected++;
            }
        };
        Assert.ThrowsAny<OperationCanceledException>(() => RenderPresence.Apply(image, basis.Info,
            new() { Texture = texture, Clarity = clarity }, 1, execution));
        Assert.Equal(1, checks);
        Assert.Equal(1, selected);
        Assert.Equal(RenderPipelineTestSupport.ReadPixels(basis.Pixels), RenderPipelineTestSupport.ReadPixels(image));
    }

    [Fact]
    public void LiveWorkerBudgetIsReadAgainBetweenBands()
    {
        using var basis = RenderPipelineTestSupport.CreateBase(OpsKernelTests.Sentinel(), height: 31);
        using var image = new MagickImage(basis.Pixels);
        var selected = new List<int>();
        var execution = RenderExecutionOptions.Resting(default, 2) with
        {
            WorkerBudget = () => selected.Count == 0 ? 2 : 1,
            WorkersSelected = selected.Add
        };
        RenderPresence.Apply(image, basis.Info, new() { Texture = 40 }, 47, execution);
        Assert.Equal(Math.Min(2, Environment.ProcessorCount), selected[0]);
        Assert.All(selected.Skip(1), count => Assert.Equal(1, count));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StageIsBetweenNrAndSharpenAndAbsentAtZero(bool raw)
    {
        using var basis = RenderPipelineTestSupport.CreateBase(OpsKernelTests.Sentinel(), raw, 31);
        var pipeline = new RenderPipeline();

        foreach (var active in new[] { false, true })
        {
            var stages = new List<string>();
            var settings = new EditSettings { Texture = active ? 40 : 0, Clarity = active ? 40 : 0 };
            using var result = pipeline.RenderResting(new(basis, settings, RenderIntent.Export, null, new(false, false)),
                RenderExecutionOptions.Resting(default, 2, stages.Add));

            if (active)
            {
                var index = stages.IndexOf("presence");
                Assert.True(index > 0);
                Assert.Equal("noise-reduction", stages[index - 1]);
                Assert.Equal("capture-sharpen", stages[index + 1]);
            }
            else
            {
                Assert.DoesNotContain("presence", stages);
            }
        }
    }

    [Fact]
    public void MonochromeRemainsEqualChannelWithActiveDetail()
    {
        var values = OpsKernelTests.Sentinel().Select((_, i) => (ushort)((i / 3 * 997) % 65536)).ToArray();
        using var source = RenderPipelineTestSupport.CreateBase(values, true, 31);
        using var basis = new BaseImage(new MagickImage(source.Pixels), source.Info with { IsMonochrome = true });
        using var result = new RenderPipeline().Render(new(basis,
            new() { Texture = 100, Clarity = -100, Detail = new() { CaptureSharpen = 40, LuminanceNr = 30 } },
            RenderIntent.Export, null, new(false, false)));
        var pixels = RenderPipelineTestSupport.ReadPixels(result.Image);

        for (var i = 0; i < pixels.Length; i += 3)
        {
            Assert.Equal(pixels[i], pixels[i + 1]);
            Assert.Equal(pixels[i], pixels[i + 2]);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ActivePipelineAgreesAcrossWorkerCapsAndBandLimits(bool raw)
    {
        using var basis = RenderPipelineTestSupport.CreateBase(OpsKernelTests.Sentinel(91, 67), raw, 67);
        var settings = new EditSettings
        {
            Texture = 40, Clarity = 40,
            Detail = new() { LuminanceNr = 30, ChromaNr = 20, CaptureSharpen = 50 }
        };
        var request = new RenderRequest(basis, settings, RenderIntent.Export, null, new(false, false));
        var pipeline = new RenderPipeline();
        using var expected = pipeline.Render(request);
        var codes = RenderPipelineTestSupport.ReadPixels(expected.Image);
        using var banded = pipeline.Render(request, 1);
        Assert.Equal(codes, RenderPipelineTestSupport.ReadPixels(banded.Image));

        foreach (var cap in new[] { 1, 2, Environment.ProcessorCount })
        {
            using var actual = pipeline.RenderResting(request, RenderExecutionOptions.Resting(default, cap));
            Assert.Equal(codes, RenderPipelineTestSupport.ReadPixels(actual.Image));
        }
    }

    [Fact]
    public void AlphaSurvivesPresence()
    {
        using var basis = RenderPipelineTestSupport.CreateBase(OpsKernelTests.Sentinel(), height: 31);
        using var image = new MagickImage(basis.Pixels);
        image.Alpha(AlphaOption.Set);
        using var pixels = image.GetPixels();
        var layout = RenderKernelSupport.GetLayout(pixels);
        var alpha = (int)pixels.GetChannelIndex(PixelChannel.Alpha)!.Value;
        var values = pixels.GetArea(0, 0, image.Width, image.Height)!;

        for (var i = 0; i < values.Length; i += layout.Channels)
        {
            values[i + alpha] = (ushort)(i * 41 % 65536);
        }

        pixels.SetArea(0, 0, image.Width, image.Height, values);
        RenderPresence.Apply(image, basis.Info, new() { Texture = 100, Clarity = 100 });
        var actual = pixels.GetArea(0, 0, image.Width, image.Height)!;

        for (var i = alpha; i < values.Length; i += layout.Channels)
        {
            Assert.Equal(values[i], actual[i]);
        }
    }

    [Fact]
    public async Task SettingsRoundTripClampTransferPresetsAndLabels()
    {
        Assert.Equal(DocumentBoundaryGoldens.Neutral, EditSettingsJson.Serialize(new()));

        var settings = new EditSettings { Texture = 50, Clarity = -50 };
        Assert.True(settings.HasEdits);
        Assert.False(settings.HasSameEdits(new()));

        var json = EditSettingsJson.Serialize(settings);
        Assert.EndsWith("\"texture\":50,\"clarity\":-50}", json);
        Assert.True(settings.HasSameEdits(EditSettingsJson.Deserialize(json, out _)));
        Assert.True(settings.HasSameEdits(settings.Clone()));

        var clamped = EditSettingsJson.Deserialize(json.Replace(":50", ":200").Replace(":-50", ":-200"), out var changed);
        Assert.True(changed);
        Assert.Equal(100, clamped.Texture);
        Assert.Equal(-100, clamped.Clarity);

        var target = new EditSettings { Locals = [new() { Exposure = 1 }] };
        var local = target.Locals[0];
        EditSettingsTransfer.ApplySubset(settings, target);
        Assert.Equal(50, target.Texture);
        Assert.Equal(-50, target.Clarity);
        Assert.Same(local, target.Locals![0]);
        Assert.True(settings.HasSameEdits(EditSettingsTransfer.CopySubset(target)));

        using var directory = new TemporaryDirectory();
        var presets = new PresetService();
        await presets.UseDirectoryAsync(directory.Path);
        await presets.SaveUserPresetAsync("Presence", target);
        var reloaded = new PresetService();
        await reloaded.UseDirectoryAsync(directory.Path);
        Assert.True(settings.HasSameEdits(Assert.Single(reloaded.UserPresets).Settings));

        Assert.Equal("Texture +50 (+50)", EditHistoryLabel.Derive(new(), new() { Texture = 50 }));
        Assert.Equal("Clarity -50 (-50)", EditHistoryLabel.Derive(new(), new() { Clarity = -50 }));
    }
}
