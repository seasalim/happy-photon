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
    public void ProfileStages(string name)
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable("HAPPY_PHOTON_HORIZON_PROFILE") == "1",
            "Opt-in unlocked horizon stage probe");
        using var pair = StraightenGateFixtures.Load(name);
        var image = pair.Interactive.Pixels;
        var longEdge = (int)typeof(HorizonDetection).GetField("LongEdge",
            BindingFlags.Static | BindingFlags.NonPublic)!.GetRawConstantValue()!;
        var scale = Math.Min(1, longEdge / (double)Math.Max(image.Width, image.Height));
        var width = (int)Math.Round(image.Width * scale);
        var height = (int)Math.Round(image.Height * scale);
        var read = Method("ReadLuminance");
        var gradients = Method("Gradients");
        var canny = Method("Canny");
        var lines = Method("FindLines");
        var samples = new List<double[]>();
        string[] hashes = [];

        for (var iteration = 0; iteration < 14; iteration++)
        {
            var start = Stopwatch.GetTimestamp();
            var plane = (double[])read.Invoke(null, [image, width, height])!;
            var afterRead = Stopwatch.GetTimestamp();
            var (gx, gy) = ((double[], double[]))gradients.Invoke(null, [plane, width, height])!;
            var afterGradients = Stopwatch.GetTimestamp();
            var edges = canny.Invoke(null, [gx, gy, width, height])!;
            var afterCanny = Stopwatch.GetTimestamp();
            var fitted = lines.Invoke(null, [edges, width, height, scale, width / (double)image.Width,
                height / (double)image.Height, new HorizonDetection.StageDiagnostics(), null])!;
            var afterLines = Stopwatch.GetTimestamp();

            if (iteration >= 3)
            {
                samples.Add([Elapsed(start, afterRead), Elapsed(afterRead, afterGradients),
                    Elapsed(afterGradients, afterCanny), Elapsed(afterCanny, afterLines)]);
            }

            if (iteration == 13)
            {
                hashes = [Hash(gx), Hash(gy), Hash(edges), Hash(fitted)];
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
            stages = Enumerable.Range(0, 4).Select(i => FinishingGateSupport.Median(samples.Select(s => s[i]).ToArray())),
            total = FinishingGateSupport.Median(whole), hashes }));
    }

    private static MethodInfo Method(string name) => typeof(HorizonDetection).GetMethod(name,
        BindingFlags.Static | BindingFlags.NonPublic)!;

    private static double Elapsed(long start, long end) => Stopwatch.GetElapsedTime(start, end).TotalMilliseconds;

    private static string Hash(object value) => Convert.ToHexString(SHA256.HashData(
        JsonSerializer.SerializeToUtf8Bytes(value, value.GetType())));
}
