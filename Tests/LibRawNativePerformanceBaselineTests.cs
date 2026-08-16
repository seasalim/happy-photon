using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Sdcb.LibRaw;
using Sdcb.LibRaw.Natives;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class LibRawNativePerformanceBaselineTests
{
    private const int Samples = 3;

    [Fact]
    public async Task CurrentRid_WritesPairedHarnessMeasurements()
    {
        var output = Environment.GetEnvironmentVariable("HAPPY_PHOTON_NATIVE_PERF_OUTPUT");
        Assert.SkipWhen(Environment.GetEnvironmentVariable("HAPPY_PHOTON_NATIVE_PERF") != "1",
            "Set HAPPY_PHOTON_NATIVE_PERF=1 to run the native performance harness.");
        Assert.True(Environment.GetEnvironmentVariable("HAPPY_PHOTON_NATIVE_PERF_ISOLATED") == "1",
            "Run this test alone in its own process.");
        Assert.False(string.IsNullOrWhiteSpace(output));
        var fixture = Path.Combine(GoldenTestPaths.AssetDirectory, "canon-eos-350d.cr2");
        Decode(fixture, "linear16-preview");
        Decode(fixture, "srgb8-full");
        var measurements = new List<NativePerfMeasurement>();
        foreach (var configuration in new[] { "linear16-preview", "srgb8-full" })
            for (var sample = 1; sample <= Samples; sample++)
                measurements.Add(await MeasureAsync(fixture, configuration, sample));
        var report = new NativePerfReport(2, "baseline", RuntimeInformation.RuntimeIdentifier,
            RawContext.VersionNumber.ToString(), RawContext.Version, Path.GetFileName(fixture),
            10, measurements);
        await File.WriteAllTextAsync(Path.GetFullPath(output!),
            JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }) + "\n",
            TestContext.Current.CancellationToken);
    }

    private static async Task<NativePerfMeasurement> MeasureAsync(
        string fixture, string configuration, int sample)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var process = Process.GetCurrentProcess();
        process.Refresh();
        var baseline = process.PrivateMemorySize64;
        var peak = baseline;
        var stopwatch = Stopwatch.StartNew();
        var work = Task.Run(() => Decode(fixture, configuration),
            TestContext.Current.CancellationToken);
        while (!work.IsCompleted)
        {
            await Task.Delay(10, TestContext.Current.CancellationToken);
            process.Refresh();
            peak = Math.Max(peak, process.PrivateMemorySize64);
        }
        var decoded = await work;
        stopwatch.Stop();
        process.Refresh();
        peak = Math.Max(peak, Math.Max(process.PrivateMemorySize64, decoded.PrivateBytesAfterCopy));
        return new(configuration, sample, stopwatch.Elapsed.TotalMilliseconds,
            baseline, peak, Math.Max(0, peak - baseline), decoded.Width, decoded.Height, decoded.Bits,
            decoded.Channels, decoded.Bytes, decoded.Sha256);
    }

    private static NativePerfDecode Decode(string fixture, string configuration)
    {
        using var context = RawContext.OpenFile(fixture);
        context.Unpack();
        context.DcrawProcess(parameters => Configure(parameters, configuration));
        using var image = context.MakeDcrawMemoryImage();
        var data = image.AsSpan<byte>().ToArray();
        var process = Process.GetCurrentProcess();
        process.Refresh();
        return new(image.Width, image.Height, image.Bits, image.Channels, data.LongLength,
            Convert.ToHexString(SHA256.HashData(data)), process.PrivateMemorySize64);
    }

    private static void Configure(OutputParams parameters, string configuration)
    {
        parameters.OutputColor = LibRawColorSpace.SRGB;
        parameters.UseCameraWb = true;
        parameters.UseAutoWb = false;
        parameters.UseCameraMatrix = true;
        if (configuration == "linear16-preview")
        {
            parameters.OutputBps = 16;
            parameters.Gamma[0] = 1;
            parameters.Gamma[1] = 1;
            parameters.NoAutoBright = true;
            parameters.HalfSize = true;
        }
        else
        {
            parameters.OutputBps = 8;
            parameters.Gamma[0] = 1.0 / 2.4;
            parameters.Gamma[1] = 12.92;
            parameters.NoAutoBright = false;
            parameters.HalfSize = false;
        }
    }
}

internal sealed record NativePerfReport(int Schema, string Runtime, string Rid,
    string VersionNumber, string Version, string Fixture, int SamplingIntervalMilliseconds,
    IReadOnlyList<NativePerfMeasurement> Measurements);
internal sealed record NativePerfMeasurement(string Configuration, int Sample,
    double ElapsedMilliseconds, long HostBaselinePrivateBytes, long PeakPrivateBytes,
    long PeakPrivateDeltaBytes, int Width, int Height,
    int Bits, int Channels, long Bytes, string Sha256);
internal sealed record NativePerfDecode(int Width, int Height, int Bits, int Channels,
    long Bytes, string Sha256, long PrivateBytesAfterCopy);
