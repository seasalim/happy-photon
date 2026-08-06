using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using HappyPhoton.Services;
using ImageMagick;

namespace HappyPhoton.Tests;

internal static class RawBaseLoaderTestSupport
{
    public static string Asset(string fileName) =>
        Path.Combine(GoldenTestPaths.AssetDirectory, fileName);

    public static byte[] PixelHash(MagickImage image)
    {
        using var pixels = image.GetPixels();
        var samples = pixels.ToShortArray(PixelMapping.RGB)
            ?? throw new InvalidOperationException("Could not read RGB16 pixels.");
        return SHA256.HashData(MemoryMarshal.AsBytes(samples.AsSpan()));
    }

    public static IEnumerable<double> Flatten(double[,] values)
    {
        foreach (var value in values)
        {
            yield return value;
        }
    }

    public static async Task<DecodeMeasurement> MeasureAsync(
        Func<BaseImage?> decode)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var baseline = GC.GetTotalMemory(forceFullCollection: true);
        var peak = baseline;
        using var stopSampling = new CancellationTokenSource();
        var sampler = Task.Run(() =>
        {
            while (!stopSampling.IsCancellationRequested)
            {
                var current = GC.GetTotalMemory(forceFullCollection: false);
                if (current > peak)
                {
                    Interlocked.Exchange(ref peak, current);
                }

                Thread.Sleep(1);
            }
        });

        var stopwatch = Stopwatch.StartNew();
        var image = decode();
        stopwatch.Stop();
        stopSampling.Cancel();
        await sampler;
        return new DecodeMeasurement(
            image,
            stopwatch.Elapsed,
            Math.Max(0, peak - baseline));
    }

}

internal sealed record DecodeMeasurement(
    BaseImage? Image,
    TimeSpan Elapsed,
    long PeakManagedBytes);

internal sealed class TemporaryDirectory : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(
        System.IO.Path.GetTempPath(),
        $"happy-photon-raw-loader-{Guid.NewGuid():N}");

    public TemporaryDirectory()
    {
        Directory.CreateDirectory(Path);
    }

    public void Dispose()
    {
        Directory.Delete(Path, recursive: true);
    }
}
