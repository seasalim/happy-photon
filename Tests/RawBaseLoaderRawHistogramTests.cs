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
        var loader = new RawBaseLoader();
        using var clip = loader.LoadPreviewBase(FixtureFile(),
            BaseDecodeSettings.Default, CancellationToken.None);
        using var blend = loader.LoadPreviewBase(FixtureFile(),
            new BaseDecodeSettings(HlReconstructionMode.Blend, FbddMode.Full),
            CancellationToken.None);

        Assert.NotNull(clip!.Info.RawHistogram);
        Assert.NotNull(blend!.Info.RawHistogram);
        Assert.Equal(clip.Info.RawHistogram!.Red, blend.Info.RawHistogram!.Red);
        Assert.Equal(clip.Info.RawHistogram.Green, blend.Info.RawHistogram.Green);
        Assert.Equal(clip.Info.RawHistogram.Blue, blend.Info.RawHistogram.Blue);
        Assert.Equal(clip.Info.RawHistogram.Clipping, blend.Info.RawHistogram.Clipping);
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
        var stopwatch = Stopwatch.StartNew();
        var histogram = RawSensorHistogram.Sample(frame!);
        stopwatch.Stop();

        Assert.NotNull(histogram);
        _output.WriteLine(
            $"RawHistogram fixture=canon-eos-6d-iso-6400.cr2; " +
            $"photosites={histogram!.Clipping!.TotalVisibleSamples}; " +
            $"elapsed_ms={stopwatch.Elapsed.TotalMilliseconds:F1}");
    }

    private static RawBaseLoader Loader(
        Func<HappyPhoton.LibRaw.Interop.LibRawContext, CancellationToken, HistogramData?> sampler) =>
        new(isAvailable: true, rawHistogramSampler: sampler);

    private static ImageFile FixtureFile() => new(Path.Combine(
        GoldenTestPaths.AssetDirectory, "canon-eos-350d.cr2"));
}
