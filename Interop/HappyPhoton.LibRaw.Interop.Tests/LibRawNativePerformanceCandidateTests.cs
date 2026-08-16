using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Xunit;

namespace HappyPhoton.LibRaw.Interop.Tests;

public sealed class LibRawNativePerformanceCandidateTests
{
    private const int Samples = 3;

    [Fact]
    public async Task CurrentRid_WritesPairedHarnessMeasurements()
    {
        var output = Environment.GetEnvironmentVariable("HAPPY_PHOTON_NATIVE_PERF_OUTPUT");
        var runtimeDirectory = Environment.GetEnvironmentVariable("HAPPY_PHOTON_LIBRAW_BRIDGE_DIR");
        Assert.SkipWhen(Environment.GetEnvironmentVariable("HAPPY_PHOTON_NATIVE_PERF") != "1",
            "Set HAPPY_PHOTON_NATIVE_PERF=1 to run the native performance harness.");
        Assert.True(Environment.GetEnvironmentVariable("HAPPY_PHOTON_NATIVE_PERF_ISOLATED") == "1",
            "Run this test alone in its own process.");
        Assert.True(!string.IsNullOrWhiteSpace(runtimeDirectory) && Directory.Exists(runtimeDirectory));
        Assert.False(string.IsNullOrWhiteSpace(output));
        var fixture = FindFixture();
        Decode(fixture, "linear16-preview");
        Decode(fixture, "srgb8-full");
        var measurements = new List<CandidatePerfMeasurement>();
        foreach (var configuration in new[] { "linear16-preview", "srgb8-full" })
            for (var sample = 1; sample <= Samples; sample++)
                measurements.Add(await MeasureAsync(fixture, configuration, sample));
        var runtime = LibRawContext.Runtime;
        var report = new CandidatePerfReport(2, "candidate", RuntimeInformation.RuntimeIdentifier,
            $"0x{runtime.LibRawVersionNumber:X6}", runtime.LibRawVersion, Path.GetFileName(fixture),
            10, measurements);
        await File.WriteAllTextAsync(Path.GetFullPath(output!),
            JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }) + "\n",
            TestContext.Current.CancellationToken);
    }

    private static async Task<CandidatePerfMeasurement> MeasureAsync(
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

    private static CandidatePerfDecode Decode(string fixture, string configuration)
    {
        using var context = LibRawContext.Open(fixture);
        context.Unpack();
        context.ConfigureOutput(configuration == "linear16-preview"
            ? LibRawOutputConfiguration.Linear(LibRawHighlightMode.Clip, LibRawFbddMode.Off, true)
            : LibRawOutputConfiguration.FullDecodeSrgb());
        context.Process();
        using var image = context.MakeProcessedImage();
        var data = image.CopyData();
        var process = Process.GetCurrentProcess();
        process.Refresh();
        return new((int)image.Description.Width, (int)image.Description.Height,
            (int)image.Description.BitsPerSample, (int)image.Description.Channels,
            data.LongLength, Convert.ToHexString(SHA256.HashData(data)),
            process.PrivateMemorySize64);
    }

    private static string FindFixture()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "Tests", "assets",
                "canon-eos-350d.cr2");
            if (File.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }
        throw new FileNotFoundException("Could not locate the performance fixture.");
    }
}

internal sealed record CandidatePerfReport(int Schema, string Runtime, string Rid,
    string VersionNumber, string Version, string Fixture, int SamplingIntervalMilliseconds,
    IReadOnlyList<CandidatePerfMeasurement> Measurements);
internal sealed record CandidatePerfMeasurement(string Configuration, int Sample,
    double ElapsedMilliseconds, long HostBaselinePrivateBytes, long PeakPrivateBytes,
    long PeakPrivateDeltaBytes, int Width, int Height,
    int Bits, int Channels, long Bytes, string Sha256);
internal sealed record CandidatePerfDecode(int Width, int Height, int Bits, int Channels,
    long Bytes, string Sha256, long PrivateBytesAfterCopy);
