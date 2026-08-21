using System.Diagnostics;
using HappyPhoton.LibRaw.Interop;
using HappyPhoton.Models;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class RawBaseLoaderRawHistogramTests
{
    private readonly ITestOutputHelper _output;

    public RawBaseLoaderRawHistogramTests(ITestOutputHelper output) =>
        _output = output;

    [Fact]
    public void SamplerSuccess_AttachesExactLoaderFact()
    {
        var expected = new HistogramData { Domain = HistogramDomain.RawSensor };
        var loader = Loader((_, _) => expected);

        using var image = loader.LoadPreviewBase(FixtureFile(),
            BaseDecodeSettings.Default, CancellationToken.None);

        Assert.NotNull(image);
        Assert.Same(expected, image!.Info.RawHistogram);
    }

    [Fact]
    public void SamplerFault_DoesNotFailOtherwiseDecodableImage()
    {
        var loader = Loader((_, _) => throw new InvalidDataException("bad layout"));

        using var image = loader.LoadPreviewBase(FixtureFile(),
            BaseDecodeSettings.Default, CancellationToken.None);

        Assert.NotNull(image);
        Assert.Null(image!.Info.RawHistogram);
    }

    [Fact]
    public void IndependentlyThrownSamplerCancellation_IsRethrown()
    {
        var loader = Loader((_, _) => throw new OperationCanceledException());

        Assert.Throws<OperationCanceledException>(() => loader.LoadPreviewBase(
            FixtureFile(), BaseDecodeSettings.Default, CancellationToken.None));
    }

    [Fact]
    public void RealSampler_IsInvariantAcrossDecodeProcessingSettings()
    {
        using var directory = new TemporaryDirectory();
        var profilePath = SyntheticDcpFactory.WriteTemporary(
            directory.Path,
            new SyntheticDcpOptions
            {
                Name = "Source saturation invariance",
                ForwardMatrix1 = DcpProfileReaderTests.D50Forward(1)
            });
        var reader = new DcpProfileReader();
        var snapshot = reader.ReadExternalSnapshot(profilePath);
        var selection = new RawProfileSelection
        {
            Source = RawProfileSource.UserFile,
            Location = profilePath,
            ContentHash = snapshot.ContentHash
        };
        var profile = reader.ParseExternal(snapshot, "source saturation");
        var decode = BaseDecodeSettings.From(new EditSettings
        {
            HlReconstruction = HlReconstructionMode.Blend,
            Detail = new DetailSettings { NoiseReduction = FbddMode.Full },
            RawProfile = selection
        }).WithProfileResolution(
            DcpProfileResolution.Success(selection, profile));
        var loader = new RawBaseLoader();
        using var clip = loader.LoadPreviewBase(FixtureFile(),
            BaseDecodeSettings.Default, CancellationToken.None);
        using var profiled = loader.LoadPreviewBase(FixtureFile(),
            decode,
            CancellationToken.None);

        Assert.Equal(DcpProfileErrorCode.None, profiled!.Info.ProfileStatus);
        Assert.NotNull(clip!.Info.RawHistogram);
        Assert.NotNull(profiled.Info.RawHistogram);
        Assert.Equal(clip.Info.RawHistogram!.Red, profiled.Info.RawHistogram!.Red);
        Assert.Equal(clip.Info.RawHistogram.Green, profiled.Info.RawHistogram.Green);
        Assert.Equal(clip.Info.RawHistogram.Blue, profiled.Info.RawHistogram.Blue);
        Assert.Equal(
            clip.Info.RawHistogram.Clipping,
            profiled.Info.RawHistogram.Clipping);
        Assert.NotNull(clip.SourceSaturation);
        Assert.NotNull(profiled.SourceSaturation);
        AssertMasksEqual(clip.SourceSaturation!, profiled.SourceSaturation!);

        var renderSettings = new EditSettings
        {
            Detail = new DetailSettings { CaptureSharpen = 0 }
        };
        var pipeline = new RenderPipeline();
        using var clipRender = pipeline.Render(new RenderRequest(
            clip,
            renderSettings,
            RenderIntent.Preview,
            1600,
            new RenderOptions(ComputeStats: true)));
        using var profiledRender = pipeline.Render(new RenderRequest(
            profiled,
            renderSettings,
            RenderIntent.Preview,
            1600,
            new RenderOptions(ComputeStats: true)));
        Assert.Equal(clipRender.Clipping.High, profiledRender.Clipping.High);
        Assert.Equal(clipRender.Clipping.HighAny, profiledRender.Clipping.HighAny);
    }

    [Fact]
    public void ApplicationHasNoSdcbOffsetReader()
    {
        var sources = new[]
        {
            Path.Combine(GoldenTestPaths.RepositoryRoot, "Services"),
            Path.Combine(GoldenTestPaths.RepositoryRoot, "Interop",
                "HappyPhoton.LibRaw.Interop")
        }.SelectMany(root => Directory.EnumerateFiles(
            root, "*.cs", SearchOption.AllDirectories))
        .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") &&
            !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));

        Assert.DoesNotContain(sources, path =>
            File.ReadAllText(path).Contains("Sdcb.LibRaw", StringComparison.Ordinal));
    }

    [Fact]
    public void RawHistogramPerformance_Canon6d()
    {
        Assert.SkipWhen(
            Environment.GetEnvironmentVariable("HAPPY_PHOTON_PERF") != "1",
            "Set HAPPY_PHOTON_PERF=1 to measure the 20 MP RAW histogram pass.");
        var path = Path.Combine(GoldenTestPaths.AssetDirectory,
            "canon-eos-6d-iso-6400.cr2");
        using var context = LibRawContext.Open(path);
        context.Unpack();
        using var frame = RawSensorFrame.TryCreate(context);
        Assert.NotNull(frame);
        var stopwatch = Stopwatch.StartNew();
        var artifacts = RawSensorHistogram.SampleArtifacts(
            frame,
            CancellationToken.None,
            workerLimit: null,
            saturationWidth: checked((int)(frame.VisibleWidth + 1) / 2),
            saturationHeight: checked((int)(frame.VisibleHeight + 1) / 2));
        stopwatch.Stop();

        Assert.NotNull(artifacts);
        Assert.NotNull(artifacts!.SourceSaturation);
        _output.WriteLine(
            $"RawHistogram fixture=canon-eos-6d-iso-6400.cr2; " +
            $"photosites={artifacts.Histogram.Clipping!.TotalVisibleSamples}; " +
            $"elapsed_ms={stopwatch.Elapsed.TotalMilliseconds:F1}");
    }

    private static RawBaseLoader Loader(
        Func<HappyPhoton.LibRaw.Interop.LibRawContext, CancellationToken, HistogramData?> sampler) =>
        new(isAvailable: true, rawHistogramSampler: sampler);

    private static ImageFile FixtureFile() => new(Path.Combine(
        GoldenTestPaths.AssetDirectory, "canon-eos-350d.cr2"));

    private static void AssertMasksEqual(
        SourceSaturationMask expected,
        SourceSaturationMask actual)
    {
        Assert.Equal(expected.Width, actual.Width);
        Assert.Equal(expected.Height, actual.Height);
        for (var y = 0; y < expected.Height; y++)
        for (var x = 0; x < expected.Width; x++)
        {
            Assert.Equal(expected.GetFlags(x, y), actual.GetFlags(x, y));
        }
    }
}
