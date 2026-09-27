using System.Diagnostics;
using System.Text.Json;
using HappyPhoton.Models;
using HappyPhoton.Services;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

[Collection(AvaloniaTestCollection.Name)]
public sealed partial class HealGateTests(ITestOutputHelper output)
{
    private const string Raw = "canon-eos-6d-iso-6400.cr2", Heic = "iphone-14-pro-iso-1000.heic";
    private static string Fixture => Environment.GetEnvironmentVariable("HAPPY_PHOTON_HEAL_FIXTURE") == "standard" ? Heic : Raw;
    private static HealCandidate Candidate => new(
        Enum.Parse<HealFormulation>(Environment.GetEnvironmentVariable("HAPPY_PHOTON_HEAL_FORMULATION") ?? "Membrane"),
        Enum.Parse<HealDomain>(Environment.GetEnvironmentVariable("HAPPY_PHOTON_HEAL_DOMAIN") ?? "Additive"));
    private static IBaseImageLoader Loader() => new GatedBaseImageLoader(
        new BaseLoaderRouter(new RawBaseLoader(), new StandardBaseLoader()), new SourceAvailabilityService());
    private static ImageFile LocalFile(string? name = null)
    {
        var path = GoldenTestPaths.Asset(name ?? Fixture);
        Assert.Equal(SourceAvailability.AvailableLocally, new SourceAvailabilityService().GetAvailability(path));
        return new(path);
    }
    private static string Size(BaseImage image) => $"{image.Pixels.Width}x{image.Pixels.Height}";
    private void Report(string gate, object values) => output.WriteLine("HEAL " + JsonSerializer.Serialize(
        new { gate, fixture = Fixture, candidate = Candidate.ToString(), pid = Environment.ProcessId,
            cpu = Environment.ProcessorCount, values }));
    private static double Time(Action action)
    {
        var start = Stopwatch.GetTimestamp(); action(); return Stopwatch.GetElapsedTime(start).TotalMilliseconds;
    }
    private static double Median(IEnumerable<double> values) { var sorted = values.Order().ToArray(); return sorted[sorted.Length / 2]; }
    private static void OptIn()
    {
        Assert.SkipWhen(Environment.GetEnvironmentVariable("HAPPY_PHOTON_PERF") != "1", "Opt-in HEAL qualification");
        Assert.Equal(default(HealCandidate), Candidate);
        Assert.Equal("1", Environment.GetEnvironmentVariable("HAPPY_PHOTON_FULL_CPU"));
        PerfEnvironment.AssertFullCpu();
#if DEBUG
        Assert.Fail("Use Release");
#endif
    }
    private static void RequireWic()
    {
        Assert.True(OperatingSystem.IsWindows());
        Assert.NotEqual("1", Environment.GetEnvironmentVariable("HAPPY_PHOTON_TEST_SKIA_ONLY"));
    }
    private static bool[] Mask(BaseImage basis, IEnumerable<HealSpot> spots)
    {
        var w = (int)basis.Pixels.Width; var h = (int)basis.Pixels.Height;
        var mask = new bool[w * h];
        foreach (var s in spots)
        {
            var r = Math.Min(s.Radius * Math.Max(w, h), Math.Min(w, h) / 2d);
            var cx = s.U * w - .5; var cy = s.V * h - .5;
            for (var y = Math.Max(0, (int)(cy - r)); y <= Math.Min(h - 1, (int)(cy + r)); y++)
            for (var x = Math.Max(0, (int)(cx - r)); x <= Math.Min(w - 1, (int)(cx + r)); x++)
                if ((x - cx) * (x - cx) + (y - cy) * (y - cy) < r * r) mask[y * w + x] = true;
        }
        return mask;
    }
    private static GoldenComparison Compare(RenderResult full, RenderResult preview, bool[] mask)
    {
        using var aligned = new MagickImage(full.Image);
        WysiwygTests.AlignForComparison(aligned, preview.Image);
        return GoldenImageComparer.Compare(aligned, preview.Image, GoldenComparisonDomain.DisplaySrgb, mask);
    }

    [Fact]
    public void G1Candidates()
    {
        OptIn(); var file = LocalFile(); var loader = Loader(); var pipeline = new RenderPipeline();
        using var pair = loader.LoadPreviewBaseWithOutcome(file, BaseDecodeSettings.Default, CancellationToken.None).Pair;
        Assert.NotNull(pair); Assert.NotNull(pair.Large);
        using var full = loader.LoadFullBase(file, BaseDecodeSettings.Default, CancellationToken.None);
        Assert.NotNull(full);
        using var controlFull = pipeline.Render(new(full, new(), RenderIntent.Export, null, new(false, false)));
        var spots = HealWorkloads.S64();
        var repairedSettings = new EditSettings { Repairs = HealWorkloads.Repairs(spots) };
        var selectedPass = true;
        foreach (var candidate in HealCandidate.FinalOnly)
        {
            using var reference = pipeline.Render(new(full, repairedSettings, RenderIntent.Export, null, new(false, false)));
            foreach (var basis in new[] { pair.Interactive, pair.Large })
            {
                var mask = Mask(basis, spots);
                using var control = pipeline.Render(new(basis, new(), RenderIntent.Preview, null, new(false, false)));
                var baseline = Compare(controlFull, control, mask);
                var start = Stopwatch.GetTimestamp();
                using var actual = pipeline.Render(new(basis, repairedSettings, RenderIntent.Preview, null, new(false, false)));
                var repairMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                var pinned = (basis.Info.IsRawSource, basis.Pixels.Width == pair.Interactive.Pixels.Width) switch
                {
                    (true, true) => (2.283, 11.249), (true, false) => (5.148, 26.462),
                    (false, true) => (1.393, 13.723), _ => (.684, 8.766)
                };
                Assert.InRange(baseline.MeanDeltaE, pinned.Item1 * .85, pinned.Item1 * 1.15);
                Assert.InRange(baseline.P99DeltaE, pinned.Item2 * .85, pinned.Item2 * 1.15);
                var metric = Compare(reference, actual, mask);
                var pass = metric.MeanDeltaE <= baseline.MeanDeltaE + .5 && metric.P99DeltaE <= baseline.P99DeltaE + 2;
                if (candidate == Candidate) selectedPass &= pass;
                Report("G1", new { formulation = candidate.ToString(), size = Size(basis), full = Size(full),
                    provenance = full.Info.IsRawSource ? "half-decode-preview-pair" : "standard-loader-preview-pair",
                    controlMean = baseline.MeanDeltaE, controlP99 = baseline.P99DeltaE,
                    mean = metric.MeanDeltaE, p99 = metric.P99DeltaE, meanLimit = baseline.MeanDeltaE + .5,
                    p99Limit = baseline.P99DeltaE + 2, pass, repairMs, maskPixels = mask.Count(v => v) });
            }
            Quality(pair.Interactive, candidate);
        }
        Assert.True(selectedPass, "G1 selected candidate exceeds owner-approved relative bounds");
    }

    private void Quality(BaseImage basis, HealCandidate candidate)
    {
        var codes = RenderPipelineTestSupport.ReadPixels(basis.Pixels);
        var w = (int)basis.Pixels.Width; var h = (int)basis.Pixels.Height; var edge = Math.Max(w, h);
        // Rank frozen destinations by center luminance; keep the darkest and median patch.
        var ordered = HealWorkloads.S64().Where(s => !s.IsClone).OrderBy(s =>
            codes[((int)(s.V * h) * w + (int)(s.U * w)) * 3 + 1]).ToArray();
        var folder = Path.Combine(GoldenTestPaths.RepositoryRoot, "artifacts", "heal");
        Directory.CreateDirectory(folder);
        foreach (var index in new[] { 0, ordered.Length / 2 })
        {
            var s = ordered[index] with { Radius = .04 };
            using var repair = new HealProductionStage().Repair(basis, [s], candidate);
            var actual = RenderPipelineTestSupport.ReadPixels(repair.Pixels);
            double excess = 0;
            for (var k = 0; k < 64; k++)
            {
                var angle = k * Math.PI / 32;
                var inside = Offset(.9); var outside = Offset(1.1);
                for (var c = 0; c < 3; c++)
                    excess += Math.Abs((actual[inside + c] - actual[outside + c]) - (codes[inside + c] - codes[outside + c])) / 65535d;
                int Offset(double scale) => ((int)Math.Round(s.V * h - .5 + Math.Sin(angle) * s.Radius * edge * scale) * w +
                    (int)Math.Round(s.U * w - .5 + Math.Cos(angle) * s.Radius * edge * scale)) * 3;
            }
            using var before = new RenderPipeline().Render(new(basis, new(), RenderIntent.Preview, null, new(false, false)));
            using var after = new RenderPipeline().Render(new(repair, new(), RenderIntent.Preview, null, new(false, false)));
            var side = (uint)Math.Ceiling(s.Radius * edge * 2.6);
            var crop = new MagickGeometry((int)(s.U * w - side / 2), (int)(s.V * h - side / 2), side, side);
            using var images = new MagickImageCollection();
            var left = new MagickImage(before.Image); left.Crop(crop); left.ResetPage(); images.Add(left);
            var right = new MagickImage(after.Image); right.Crop(crop); right.ResetPage(); images.Add(right);
            using var montage = images.AppendHorizontally();
            var path = Path.Combine(folder, $"{Fixture}-{candidate}-{(index == 0 ? "shadow" : "mid")}.png");
            montage.Write(path);
            Report("seam", new { formulation = candidate.ToString(), patch = index == 0 ? "shadow" : "mid",
                s.U, s.V, seamExcess = excess / 192, path });
        }
    }
}
