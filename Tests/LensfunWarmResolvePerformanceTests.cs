using System.Diagnostics;
using HappyPhoton.LibRaw.Interop;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class LensfunWarmResolvePerformanceTests
{
    // The required 50-pass baseline had >5% spread, so the frozen gate's
    // sample-size rule raises this until the five-sample spread is stable.
    private const int PassCount = 500;
    private const int WarmupPassCount = 500;
    private const int SampleCount = 5;

    private static readonly string[] FixtureNames =
    [
        "canon-eos-350d.cr2",
        "canon-eos-6d-iso-6400.cr2",
        "fujifilm-x30.raf",
        "nikon-d300-colorchecker.nef",
        "nikon-d70-burst-1.nef",
        "nikon-d70-burst-2.nef",
        "pentax-k-r.dng"
    ];

    [Fact]
    [Trait("Category", "Performance")]
    public void WarmFixtureResolutionReportsFiveSamples()
    {
        Assert.SkipWhen(
            Environment.GetEnvironmentVariable("HAPPY_PHOTON_PERF") != "1",
            "Set HAPPY_PHOTON_PERF=1 to run the Lensfun warm-resolution gate.");
        PerfEnvironment.AssertFullCpu();

        var fixtures = FixtureNames.Select(ReadFixture).ToArray();
        var database = new LensfunDatabase(Path.Combine(
            AppContext.BaseDirectory, "data", "lensfun"));
        for (var pass = 0; pass < WarmupPassCount; pass++)
            _ = ResolvePass(database, fixtures);

        var samples = new double[SampleCount];
        for (var sample = 0; sample < samples.Length; sample++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            var stopwatch = Stopwatch.StartNew();
            var resolved = 0;
            for (var pass = 0; pass < PassCount; pass++)
                resolved += ResolvePass(database, fixtures);
            stopwatch.Stop();
            samples[sample] = stopwatch.Elapsed.TotalMilliseconds / PassCount;
            Console.Error.WriteLine(
                $"Lensfun G6 sample {sample + 1}={samples[sample]:F3}ms/pass " +
                $"resolved={resolved}/{PassCount * fixtures.Length}");
        }

        var median = samples.Order().ElementAt(samples.Length / 2);
        Console.Error.WriteLine($"Lensfun G6 median={median:F3}ms/pass");
    }

    private static FixtureTuple ReadFixture(string name)
    {
        using var context = LibRawContext.Open(GoldenTestPaths.Asset(name));
        var metadata = context.GetMetadata();
        var dimensions = context.GetDimensions();
        return new FixtureTuple(
            metadata.NormalizedMake ?? metadata.Make,
            metadata.NormalizedModel ?? metadata.Model,
            metadata.Lens,
            metadata.FocalLength ?? 0,
            metadata.Aperture,
            checked((int)dimensions.VisibleWidth),
            checked((int)dimensions.VisibleHeight));
    }

    private static int ResolvePass(
        LensfunDatabase database,
        IReadOnlyList<FixtureTuple> fixtures)
    {
        var resolved = 0;
        foreach (var fixture in fixtures)
        {
            if (database.Resolve(
                fixture.Make,
                fixture.Model,
                fixture.Lens,
                fixture.FocalLength,
                fixture.Aperture,
                fixture.Width,
                fixture.Height) is not null)
            {
                resolved++;
            }
        }

        return resolved;
    }

    private sealed record FixtureTuple(
        string? Make,
        string? Model,
        string? Lens,
        double FocalLength,
        double? Aperture,
        int Width,
        int Height);
}
