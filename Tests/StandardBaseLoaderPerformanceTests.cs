using System.Diagnostics;
using HappyPhoton.Models;
using HappyPhoton.Services;
using ImageMagick;
using Xunit;
using Xunit.Abstractions;

namespace HappyPhoton.Tests;

public sealed class StandardBaseLoaderPerformanceTests
{
    private readonly ITestOutputHelper _output;

    public StandardBaseLoaderPerformanceTests(ITestOutputHelper output) =>
        _output = output;

    [Fact]
    public void PreviewDecodePerformance_WhenEnabled()
    {
        if (Environment.GetEnvironmentVariable("HAPPY_PHOTON_PERF") != "1")
        {
            return;
        }

        _output.WriteLine($"Magick.NET: {MagickNET.Version}");
        _output.WriteLine($"Delegates: {MagickNET.Delegates}");
        MeasureColdAndWarm("JPEG", Asset("display-p3-reference.jpg"));

        var heic = MagickFormatInfo.Create(MagickFormat.Heic);
        _output.WriteLine(
            $"HEIC probe: read={heic?.SupportsReading}, " +
            $"module={heic?.ModuleFormat}, description={heic?.Description}");
        if (heic is { SupportsReading: true })
        {
            MeasureColdAndWarm("HEIC", Asset("reference.heic"));
        }
    }

    private void MeasureColdAndWarm(string label, ImageFile file)
    {
        var cold = Measure(file);
        WriteMeasurement(label, "cold", cold);

        var warm = Enumerable.Range(1, 3)
            .Select(_ => Measure(file))
            .ToArray();
        for (var index = 0; index < warm.Length; index++)
        {
            WriteMeasurement(label, $"warm {index + 1}", warm[index]);
        }

        var median = warm.OrderBy(value => value.ElapsedMilliseconds).ElementAt(1);
        _output.WriteLine(
            $"{label} warm median: {median.ElapsedMilliseconds:F1} ms, " +
            $"managed allocated {median.ManagedAllocatedBytes / 1024d:F1} KiB, " +
            $"managed live delta {median.ManagedLiveDeltaBytes / 1024d:F1} KiB");
    }

    private static Measurement Measure(ImageFile file)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var liveBefore = GC.GetTotalMemory(forceFullCollection: true);
        var allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
        var stopwatch = Stopwatch.StartNew();

        var result = new StandardBaseLoader().LoadPreviewBase(
            file,
            BaseDecodeSettings.Default,
            CancellationToken.None);

        stopwatch.Stop();
        var allocated = GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore;
        var dimensions = result == null
            ? "null"
            : $"{result.Pixels.Width}x{result.Pixels.Height}";
        result?.Dispose();
        var liveAfter = GC.GetTotalMemory(forceFullCollection: true);
        return new Measurement(
            stopwatch.Elapsed.TotalMilliseconds,
            allocated,
            liveAfter - liveBefore,
            dimensions);
    }

    private void WriteMeasurement(
        string label,
        string phase,
        Measurement measurement) =>
        _output.WriteLine(
            $"{label} {phase}: {measurement.ElapsedMilliseconds:F1} ms, " +
            $"managed allocated {measurement.ManagedAllocatedBytes / 1024d:F1} KiB, " +
            $"managed live delta {measurement.ManagedLiveDeltaBytes / 1024d:F1} KiB, " +
            $"pixels={measurement.Dimensions}");

    private static ImageFile Asset(string fileName) =>
        new(Path.Combine(GoldenTestPaths.AssetDirectory, fileName));

    private readonly record struct Measurement(
        double ElapsedMilliseconds,
        long ManagedAllocatedBytes,
        long ManagedLiveDeltaBytes,
        string Dimensions);
}
