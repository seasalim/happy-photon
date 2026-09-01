using System.Diagnostics;
using HappyPhoton.Models;
using HappyPhoton.Services;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class RenderNoiseReductionPerformanceTests
{
    private const int PreviewDiagnosticSampleCount = 15;
    private const int FullResolutionSampleCount = 5;

    private readonly ITestOutputHelper _output;

    public RenderNoiseReductionPerformanceTests(ITestOutputHelper output) =>
        _output = output;

    [Fact]
    public Task FullResolutionChromaNr100_MeetsLatencyAndMemoryGate() =>
        AssertFullResolutionGate(
            "Banded chroma NR 100",
            new DetailSettings { ChromaNr = 100 },
            maximumMedianMilliseconds: 350);

    [Fact]
    public Task FullResolutionLuminanceNr100_MeetsLatencyAndMemoryGate() =>
        AssertFullResolutionGate(
            "Banded luminance NR 100",
            new DetailSettings { LuminanceNr = 100 },
            maximumMedianMilliseconds: 200);

    [Fact]
    public Task FullResolutionCombinedNr100_MeetsLatencyAndMemoryGate() =>
        AssertFullResolutionGate(
            "Banded combined NR 100",
            new DetailSettings { LuminanceNr = 100, ChromaNr = 100 },
            maximumMedianMilliseconds: 450);

    private async Task AssertFullResolutionGate(
        string label,
        DetailSettings settings,
        double maximumMedianMilliseconds)
    {
        Assert.SkipWhen(
            Environment.GetEnvironmentVariable("HAPPY_PHOTON_PERF") != "1",
            "Set HAPPY_PHOTON_PERF=1 to run detail performance diagnostics.");
#if DEBUG
        Assert.Skip("Run detail performance diagnostics in Release.");
#endif
        PerfEnvironment.AssertFullCpu();

        using (var warmup = CreateImage(256, 256))
        {
            for (var iteration = 0; iteration < 16; iteration++)
            {
                RenderNoiseReduction.Apply(
                    warmup,
                    CreateInfo(256, 256),
                    settings);
            }
        }

        const int width = 5472;
        const int height = 3648;
        using var process = Process.GetCurrentProcess();
        var latencySamples = new double[FullResolutionSampleCount];
        var memorySamples = new double[FullResolutionSampleCount];
        for (var sample = 0; sample < FullResolutionSampleCount; sample++)
        {
            using var image = CreateImage(width, height);
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            process.Refresh();
            var baseline = process.PrivateMemorySize64;
            var peak = baseline;
            var stopwatch = Stopwatch.StartNew();
            var render = Task.Run(() => RenderNoiseReduction.Apply(
                image,
                CreateInfo(width, height),
                settings));
            while (!render.IsCompleted)
            {
                await Task.Delay(5);
                process.Refresh();
                peak = Math.Max(peak, process.PrivateMemorySize64);
            }
            await render;
            stopwatch.Stop();
            process.Refresh();
            peak = Math.Max(peak, process.PrivateMemorySize64);
            latencySamples[sample] = stopwatch.Elapsed.TotalMilliseconds;
            memorySamples[sample] = Math.Max(0, peak - baseline) / 1048576.0;
            Assert.Equal((uint)width, image.Width);
            Assert.Equal((uint)height, image.Height);
        }

        var orderedLatency = latencySamples.Order().ToArray();
        var medianLatency = orderedLatency[orderedLatency.Length / 2];
        _output.WriteLine(
            $"{label} at {width}x{height}: median {medianLatency:F1} ms " +
            $"over {FullResolutionSampleCount} runs " +
            $"[{string.Join(", ", latencySamples.Select(value => $"{value:F1}"))}]; " +
            $"peak private-memory deltas " +
            $"[{string.Join(", ", memorySamples.Select(value => $"{value:F1}"))}] MiB.");
        Assert.True(medianLatency <= maximumMedianMilliseconds,
            $"{label} median was {medianLatency:F1} ms; limit is " +
            $"{maximumMedianMilliseconds:F0} ms.");
        Assert.All(memorySamples, memoryMiB => Assert.True(memoryMiB <= 150,
            $"{label} used {memoryMiB:F1} MiB peak private memory."));
    }

    [Fact]
    public async Task LuminanceNrPreviewScaleShapes_ReportStageCost()
    {
        Assert.SkipWhen(
            Environment.GetEnvironmentVariable("HAPPY_PHOTON_PERF") != "1",
            "Set HAPPY_PHOTON_PERF=1 to run detail performance diagnostics.");
#if DEBUG
        Assert.Skip("Run detail performance diagnostics in Release.");
#endif
        PerfEnvironment.AssertFullCpu();

        var shapes = new[]
        {
            new PreviewShape(1600, 1067, 5472, 3648, 2),
            new PreviewShape(1600, 1200, 4032, 3024, 3),
            new PreviewShape(1600, 1200, 1600, 1200, 4)
        };
        foreach (var shape in shapes)
        {
            using var source = CreateImage(shape.Width, shape.Height);
            var info = CreateInfo(shape.FullWidth, shape.FullHeight);
            Assert.Equal(shape.ScaleCount,
                RenderNoiseReduction.ResolveScales(source, info, 1).Length);
            using (var warmup = new MagickImage(source))
            {
                RenderNoiseReduction.Apply(warmup, info,
                    new DetailSettings { LuminanceNr = 50 });
            }

            var samples = new double[PreviewDiagnosticSampleCount];
            for (var index = 0; index < samples.Length; index++)
            {
                using var candidate = new MagickImage(source);
                var stopwatch = Stopwatch.StartNew();
                RenderNoiseReduction.Apply(candidate, info,
                    new DetailSettings { LuminanceNr = 50 });
                stopwatch.Stop();
                samples[index] = stopwatch.Elapsed.TotalMilliseconds;
            }

            using var memoryCandidate = new MagickImage(source);
            using var process = Process.GetCurrentProcess();
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            process.Refresh();
            var baseline = process.PrivateMemorySize64;
            var peak = baseline;
            var render = Task.Run(() => RenderNoiseReduction.Apply(
                memoryCandidate, info,
                new DetailSettings { LuminanceNr = 50 }));
            while (!render.IsCompleted)
            {
                await Task.Delay(1);
                process.Refresh();
                peak = Math.Max(peak, process.PrivateMemorySize64);
            }
            await render;
            process.Refresh();
            peak = Math.Max(peak, process.PrivateMemorySize64);

            Array.Sort(samples);
            _output.WriteLine(
                $"Luminance NR 50 stage {shape.Width}x{shape.Height} from " +
                $"{shape.FullWidth}x{shape.FullHeight} ({shape.ScaleCount} scales): " +
                $"median {samples[samples.Length / 2]:F2} ms over " +
                $"{PreviewDiagnosticSampleCount} iterations, peak private delta " +
                $"{Math.Max(0, peak - baseline) / 1048576.0:F1} MiB.");
        }
    }

    [Fact]
    public async Task FullResolutionCaptureSharpen_ReportsLatencyAndPeakMemory()
    {
        Assert.SkipWhen(
            Environment.GetEnvironmentVariable("HAPPY_PHOTON_PERF") != "1",
            "Set HAPPY_PHOTON_PERF=1 to run detail performance diagnostics.");
#if DEBUG
        Assert.Skip("Run detail performance diagnostics in Release.");
#endif
        PerfEnvironment.AssertFullCpu();

        using (var warmup = CreateImage(256, 256))
        {
            for (var iteration = 0; iteration < 32; iteration++)
            {
                RenderSharpening.ApplyCapture(
                    warmup,
                    CreateInfo(256, 256, isRaw: true),
                    new DetailSettings());
            }
        }
        await Task.Delay(100);

        const int width = 5472;
        const int height = 3648;
        using var image = CreateImage(width, height);
        var process = Process.GetCurrentProcess();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        process.Refresh();
        var baseline = process.PrivateMemorySize64;
        var peak = baseline;
        var stopwatch = Stopwatch.StartNew();

        var render = Task.Run(() =>
            RenderSharpening.ApplyCapture(
                image,
                CreateInfo(width, height, isRaw: true),
                new DetailSettings()));
        while (!render.IsCompleted)
        {
            await Task.Delay(5);
            process.Refresh();
            peak = Math.Max(peak, process.PrivateMemorySize64);
        }
        await render;
        stopwatch.Stop();
        process.Refresh();
        peak = Math.Max(peak, process.PrivateMemorySize64);

        Assert.Equal((uint)width, image.Width);
        Assert.Equal((uint)height, image.Height);
        _output.WriteLine(
            $"Banded capture sharpen 25 at {width}x{height}: " +
            $"{stopwatch.Elapsed.TotalMilliseconds:F1} ms, " +
            $"peak private-memory delta " +
            $"{(peak - baseline) / 1048576.0:F1} MiB.");
    }

    private static MagickImage CreateImage(int width, int height)
    {
        var image = new MagickImage(
            MagickColors.Black,
            (uint)width,
            (uint)height)
        {
            ColorSpace = ColorSpace.sRGB
        };
        using var pixels = image.GetPixels();
        var channels = checked((int)pixels.Channels);
        var row = new ushort[checked(width * channels)];
        for (var y = 0; y < height; y++)
        {
            for (var index = 0; index < row.Length; index++)
            {
                var sample = checked((long)y * row.Length + index);
                var mixed = unchecked(
                    (uint)sample * 747_796_405u + 2_891_336_453u);
                row[index] = (ushort)(mixed ^ mixed >> 16);
            }
            pixels.SetArea(0, y, (uint)width, 1, row);
        }
        return image;
    }

    private static BaseImageInfo CreateInfo(
        int width,
        int height,
        bool isRaw = false) =>
        new(
            isRaw ? BaseSourceKind.RawLibRaw : BaseSourceKind.Standard,
            isRaw,
            BaseDecodeSettings.Default,
            null,
            null,
            6504,
            0,
            false,
            null,
            1,
            width,
            height);

    private sealed record PreviewShape(
        int Width,
        int Height,
        int FullWidth,
        int FullHeight,
        int ScaleCount);
}
