using System.Diagnostics;
using HappyPhoton.Models;
using HappyPhoton.Services;
using ImageMagick;
using Xunit;
using Xunit.Abstractions;

namespace HappyPhoton.Tests;

public sealed class RenderDetailPerformanceTests
{
    private readonly ITestOutputHelper _output;

    public RenderDetailPerformanceTests(ITestOutputHelper output) =>
        _output = output;

    [SkippableFact]
    public async Task FullResolutionBandedRender_ReportsLatencyAndPeakMemory()
    {
        Skip.If(
            Environment.GetEnvironmentVariable("HAPPY_PHOTON_PERF") != "1",
            "Set HAPPY_PHOTON_PERF=1 to run detail performance diagnostics.");
#if DEBUG
        Skip.If(true, "Run detail performance diagnostics in Release.");
#endif

        using (var warmup = CreateImage(256, 256))
        {
            for (var iteration = 0; iteration < 32; iteration++)
            {
                RenderDetail.Apply(
                    warmup,
                    CreateInfo(256, 256),
                    new DetailSettings { ChromaNr = 100 });
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

        var render = Task.Run(() => RenderDetail.Apply(
            image,
            CreateInfo(width, height),
            new DetailSettings { ChromaNr = 100 }));
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
            $"Banded chroma NR 100 at {width}x{height}: " +
            $"{stopwatch.Elapsed.TotalMilliseconds:F1} ms, " +
            $"peak private-memory delta " +
            $"{(peak - baseline) / 1048576.0:F1} MiB.");
    }

    [SkippableFact]
    public async Task FullResolutionCaptureSharpen_ReportsLatencyAndPeakMemory()
    {
        Skip.If(
            Environment.GetEnvironmentVariable("HAPPY_PHOTON_PERF") != "1",
            "Set HAPPY_PHOTON_PERF=1 to run detail performance diagnostics.");
#if DEBUG
        Skip.If(true, "Run detail performance diagnostics in Release.");
#endif

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
}
