using System.Buffers;
using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

// An unlocked, single-process development probe, never a substitute for the G5 runner.
public sealed class HorizonDetectionProfileTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData("canon-eos-6d-iso-6400.cr2")]
    [InlineData("nikon-d300-colorchecker.nef")]
    [InlineData("iphone-14-pro-iso-1000.heic")]
    [InlineData("fbm")]
    public void ProfileStages(string name)
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable("HAPPY_PHOTON_HORIZON_PROFILE") == "1",
            "Opt-in unlocked horizon stage probe");
        using var pair = name == "fbm" ? null : StraightenGateFixtures.Load(name);
        using var synthetic = name == "fbm" ? StraightenGateNegativeScenes.Create(name, false) : null;
        var image = synthetic ?? pair!.Interactive.Pixels;
        var longEdge = (int)typeof(HorizonDetection).GetField("LongEdge",
            BindingFlags.Static | BindingFlags.NonPublic)!.GetRawConstantValue()!;
        var scale = Math.Min(1, longEdge / (double)Math.Max(image.Width, image.Height));
        var width = (int)Math.Round(image.Width * scale);
        var height = (int)Math.Round(image.Height * scale);
        var read = Method("ReadLuminance");
        var gradients = Method("Gradients");
        var canny = Method("Canny");
        var lines = Method("FindLines");
        var skyline = Method("Skyline");
        var orientation = Method("Orientation");
        var samples = new List<double[]>();
        string[] hashes = [];

        for (var iteration = 0; iteration < 14; iteration++)
        {
            var plane = ArrayPool<double>.Shared.Rent(width * height);
            var gx = ArrayPool<double>.Shared.Rent(width * height);
            var gy = ArrayPool<double>.Shared.Rent(width * height);
            var magnitude = ArrayPool<double>.Shared.Rent(width * height);

            try
            {
                var start = Stopwatch.GetTimestamp();
                read.Invoke(null, [image, width, height, plane]);
                var afterRead = Stopwatch.GetTimestamp();
                gradients.Invoke(null, [plane, width, height, gx, gy]);
                var afterGradients = Stopwatch.GetTimestamp();
                var edges = canny.Invoke(null, [gx, gy, width, height, magnitude])!;
                var afterCanny = Stopwatch.GetTimestamp();
                var fitted = lines.Invoke(null, [edges, width, height, scale, width / (double)image.Width,
                    height / (double)image.Height, new HorizonDetection.StageDiagnostics(), null])!;
                var afterLines = Stopwatch.GetTimestamp();
                var boundary = skyline.Invoke(null, [plane, width, height,
                    image.Width / (double)width, image.Height / (double)height])!;
                var afterSkyline = Stopwatch.GetTimestamp();
                var oriented = orientation.Invoke(null, [gx, gy, magnitude, width, height,
                    image.Width / (double)width, image.Height / (double)height])!;
                var afterOrientation = Stopwatch.GetTimestamp();

                if (iteration >= 3)
                {
                    samples.Add([Elapsed(start, afterRead), Elapsed(afterRead, afterGradients),
                        Elapsed(afterGradients, afterCanny), Elapsed(afterCanny, afterLines),
                        Elapsed(afterLines, afterSkyline), Elapsed(afterSkyline, afterOrientation)]);
                }

                if (iteration == 13)
                {
                    // Restore only the unused borders for comparable pre-pooling hashes.
                    var originalX = plane.Take(width * height).ToArray();
                    var originalY = new double[width * height];

                    for (var y = 16; y < height - 16; y++)
                    {
                        Array.Copy(gx, y * width + 16, originalX, y * width + 16, width - 32);
                        Array.Copy(gy, y * width + 16, originalY, y * width + 16, width - 32);
                    }

                    hashes = [Hash(originalX), Hash(originalY), Hash(edges), Hash(fitted), Hash(boundary), Hash(oriented)];
                }
            }
            finally
            {
                ArrayPool<double>.Shared.Return(plane);
                ArrayPool<double>.Shared.Return(gx);
                ArrayPool<double>.Shared.Return(gy);
                ArrayPool<double>.Shared.Return(magnitude);
            }
        }

        var whole = new List<double>();

        for (var iteration = 0; iteration < 14; iteration++)
        {
            var start = Stopwatch.GetTimestamp();
            HorizonDetection.Detect(image);
            if (iteration >= 3) whole.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        }

        output.WriteLine("HORIZON-PROFILE " + JsonSerializer.Serialize(new { name, width, height,
            cpu = Environment.ProcessorCount, lanes = System.Numerics.Vector<double>.Count,
            avx2 = System.Runtime.Intrinsics.X86.Avx2.IsSupported,
            stageNames = new[] { "read", "gradients", "canny", "lines", "skyline", "orientation" },
            stages = Enumerable.Range(0, 6).Select(i => FinishingGateSupport.Median(samples.Select(s => s[i]).ToArray())),
            total = FinishingGateSupport.Median(whole), hashes }));
    }

    private static MethodInfo Method(string name) => typeof(HorizonDetection).GetMethod(name,
        BindingFlags.Static | BindingFlags.NonPublic)!;

    private static double Elapsed(long start, long end) => Stopwatch.GetElapsedTime(start, end).TotalMilliseconds;

    private static string Hash(object value) => Convert.ToHexString(SHA256.HashData(
        JsonSerializer.SerializeToUtf8Bytes(value, value.GetType())));
}
