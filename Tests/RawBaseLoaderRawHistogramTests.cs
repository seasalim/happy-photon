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

        var outcome = loader.LoadPreviewBaseWithOutcome(FixtureFile(),
            BaseDecodeSettings.Default, CancellationToken.None);
        using var pair = outcome.Pair;

        Assert.NotNull(pair);
        Assert.Same(expected, outcome.Analysis.RawHistogram);
    }

    [Fact]
    public void SamplerFault_DoesNotFailOtherwiseDecodableImage()
    {
        var loader = Loader((_, _) => throw new InvalidDataException("bad layout"));

        var outcome = loader.LoadPreviewBaseWithOutcome(FixtureFile(),
            BaseDecodeSettings.Default, CancellationToken.None);
        using var pair = outcome.Pair;

        Assert.NotNull(pair);
        Assert.Null(outcome.Analysis.RawHistogram);
    }

    [Fact]
    public void IndependentlyThrownSamplerCancellation_IsRethrown()
    {
        var loader = Loader((_, _) => throw new OperationCanceledException());

        Assert.Throws<OperationCanceledException>(() =>
            loader.LoadPreviewBaseWithOutcome(
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
            RawProfile = selection
        }).WithProfileResolution(
            DcpProfileResolution.Success(selection, profile));
        var loader = new RawBaseLoader();
        var clipOutcome = loader.LoadPreviewBaseWithOutcome(FixtureFile(),
            BaseDecodeSettings.Default, CancellationToken.None);
        using var clipPair = clipOutcome.Pair;
        var profiledOutcome = loader.LoadPreviewBaseWithOutcome(FixtureFile(),
            decode,
            CancellationToken.None);
        using var profiledPair = profiledOutcome.Pair;
        var clip = clipPair!.Interactive;
        var profiled = profiledPair!.Interactive;
        var clipAnalysis = clipOutcome.Analysis;
        var profiledAnalysis = profiledOutcome.Analysis;

        Assert.Equal(DcpProfileErrorCode.None, profiled.Info.ProfileStatus);
        Assert.NotNull(clipAnalysis.RawHistogram);
        Assert.NotNull(profiledAnalysis.RawHistogram);
        Assert.Equal(
            clipAnalysis.RawHistogram!.Red,
            profiledAnalysis.RawHistogram!.Red);
        Assert.Equal(
            clipAnalysis.RawHistogram.Green,
            profiledAnalysis.RawHistogram.Green);
        Assert.Equal(
            clipAnalysis.RawHistogram.Blue,
            profiledAnalysis.RawHistogram.Blue);
        Assert.Equal(
            clipAnalysis.RawHistogram.Clipping,
            profiledAnalysis.RawHistogram.Clipping);
        Assert.NotNull(clipAnalysis.SourceSaturation);
        Assert.NotNull(profiledAnalysis.SourceSaturation);
        AssertMasksEqual(
            clipAnalysis.SourceSaturation!,
            profiledAnalysis.SourceSaturation!);

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
            new RenderOptions(ComputeStats: true))
        {
            SourceSaturation = clipAnalysis.SourceSaturation
        });
        using var profiledRender = pipeline.Render(new RenderRequest(
            profiled,
            renderSettings,
            RenderIntent.Preview,
            1600,
            new RenderOptions(ComputeStats: true))
        {
            SourceSaturation = profiledAnalysis.SourceSaturation
        });
        Assert.Equal(clipRender.Clipping.High, profiledRender.Clipping.High);
        Assert.Equal(clipRender.Clipping.HighAny, profiledRender.Clipping.HighAny);
    }

    [Fact]
    public void FullBase_SkipsSourceAnalysisSampling()
    {
        var calls = 0;
        var loader = Loader((_, _) =>
        {
            calls++;
            return new HistogramData { Domain = HistogramDomain.RawSensor };
        });

        using var image = loader.LoadFullBase(
            FixtureFile(),
            BaseDecodeSettings.Default,
            CancellationToken.None);

        Assert.NotNull(image);
        Assert.Equal(0, calls);
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
