using System.Diagnostics;
using System.Text.Json;
using HappyPhoton.Models;
using HappyPhoton.Services;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

[Collection(AvaloniaTestCollection.Name)]
public sealed partial class OpsGateTests(ITestOutputHelper output)
{
    private static string Fixture => Environment.GetEnvironmentVariable("HAPPY_PHOTON_OPS_FIXTURE") == "standard"
        ? "iphone-14-pro-iso-1000.heic" : "canon-eos-6d-iso-6400.cr2";
    private static OpsClarity Candidate => Enum.Parse<OpsClarity>(Environment.GetEnvironmentVariable("HAPPY_PHOTON_OPS_CLARITY") ?? "Guided");
    private static bool Refine => Environment.GetEnvironmentVariable("HAPPY_PHOTON_OPS_REFINE") == "1";
    private static int Workers => Environment.ProcessorCount;
    private static string Folder => Directory.CreateDirectory(Path.Combine(GoldenTestPaths.RepositoryRoot, "artifacts", "ops")).FullName;
    private static IBaseImageLoader Loader() => new GatedBaseImageLoader(new BaseLoaderRouter(new RawBaseLoader(), new StandardBaseLoader()), new SourceAvailabilityService());
    private static ImageFile LocalFile(string? name = null)
    {
        var path = GoldenTestPaths.Asset(name ?? Fixture);
        Assert.Equal(SourceAvailability.AvailableLocally, new SourceAvailabilityService().GetAvailability(path));
        return new(path);
    }
    private static void OptIn()
    {
        Assert.SkipWhen(Environment.GetEnvironmentVariable("HAPPY_PHOTON_PERF") != "1", "Opt-in OPS qualification");
        Assert.Equal("1", Environment.GetEnvironmentVariable("HAPPY_PHOTON_FULL_CPU"));
        PerfEnvironment.AssertFullCpu();
#if DEBUG
        Assert.Fail("Use Release");
#endif
    }
    private void Report(string gate, object values) => output.WriteLine("OPS " + JsonSerializer.Serialize(new
    { gate, fixture = Fixture, candidate = Candidate.ToString(), refine = Refine, pid = Environment.ProcessId, cpu = Workers, values }));
    private static string Size(BaseImage b) => $"{b.Pixels.Width}x{b.Pixels.Height}";
    private static double Time(Action action) { var start = Stopwatch.GetTimestamp(); action(); return Stopwatch.GetElapsedTime(start).TotalMilliseconds; }
    private static double Median(IEnumerable<double> values) { var sorted = values.Order().ToArray(); return sorted[sorted.Length / 2]; }
    private static GoldenComparison Compare(MagickImage full, MagickImage preview)
    {
        using var aligned = new MagickImage(full); WysiwygTests.AlignForComparison(aligned, preview);
        return GoldenImageComparer.Compare(aligned, preview, GoldenComparisonDomain.DisplaySrgb);
    }
    private static MagickImage Render(BaseImage b, OpsArm arm, RenderIntent intent = RenderIntent.Export,
        int? workers = null, int rows = 32) => OpsRenderHarness.Render(b, arm, Candidate, Refine, intent, workers ?? Workers, rows);

    [Fact]
    public void Qualification()
    {
        OptIn(); var loader = Loader(); var file = LocalFile();
        using var pair = loader.LoadPreviewBaseWithOutcome(file, BaseDecodeSettings.Default, CancellationToken.None).Pair;
        using var full = loader.LoadFullBase(file, BaseDecodeSettings.Default, CancellationToken.None);
        Assert.NotNull(pair?.Large); Assert.NotNull(full);
        foreach (var basis in new[] { pair.Interactive, pair.Large, full })
        {
            var intent = ReferenceEquals(basis, full) ? RenderIntent.Export : RenderIntent.Preview;
            using var control = new RenderPipeline().RenderDisplayRec2020(new(basis, new(), intent, null, new(false, false)));
            using var actual = OpsRenderHarness.Upstream(basis, new(), OpsArm.Off, Candidate, Refine, intent, Workers, forceFused: true);
            var a = RenderPipelineTestSupport.ReadPixels(control); var b = RenderPipelineTestSupport.ReadPixels(actual);
            var differing = a.Zip(b, (x, y) => x != y ? 1 : 0).Sum();
            Report("qualification", new { size = Size(basis), intent = intent.ToString(), differing, limit = 0 });
            Assert.Equal(0, differing);
        }
    }

    [Fact]
    public void G1Parity()
    {
        OptIn(); var loader = Loader(); var file = LocalFile();
        using var pair = loader.LoadPreviewBaseWithOutcome(file, BaseDecodeSettings.Default, CancellationToken.None).Pair;
        using var full = loader.LoadFullBase(file, BaseDecodeSettings.Default, CancellationToken.None);
        Assert.NotNull(pair?.Large); Assert.NotNull(full);
        using var controlFull = new RenderPipeline().Render(new(full, new(), RenderIntent.Export, null, new(false, false)));
        var controls = new Dictionary<BaseImage, GoldenComparison>();
        foreach (var basis in new[] { pair.Interactive, pair.Large })
        {
            using var control = new RenderPipeline().Render(new(basis, new(), RenderIntent.Preview, null, new(false, false)));
            controls[basis] = Compare(controlFull.Image, control.Image);
        }
        var allPass = true;
        foreach (var arm in OpsArm.All)
        {
            using var reference = Render(full, arm);
            foreach (var basis in new[] { pair.Interactive, pair.Large })
            {
                using var actual = Render(basis, arm, RenderIntent.Preview);
                var control = controls[basis]; var metric = Compare(reference, actual);
                var pass = metric.MeanDeltaE <= control.MeanDeltaE + .5 && metric.P99DeltaE <= control.P99DeltaE + 2;
                Report("G1", new { arm = arm.Name, size = Size(basis), full = Size(full),
                    provenance = full.Info.IsRawSource ? "half-decode-preview-pair" : "standard-loader-preview-pair",
                    controlMean = control.MeanDeltaE, controlP99 = control.P99DeltaE,
                    mean = metric.MeanDeltaE, p99 = metric.P99DeltaE,
                    meanLimit = control.MeanDeltaE + .5, p99Limit = control.P99DeltaE + 2, pass });
                allPass &= pass;
            }
        }
        Assert.True(allPass, "G1 relative parity miss; retain all observations and return to owner");
    }

    [Fact]
    public void G5Halo()
    {
        OptIn(); var pass = true;
        foreach (var edge in new[] { 1600, 5496 })
        {
            using var basis = OpsWorkloads.Skyline(edge);
            using var control = Render(basis, OpsArm.Off);
            var baseline = Halo(control); Assert.Equal(0, baseline);
            foreach (var arm in new[] { new OpsArm("CL+100", Clarity: 100), new OpsArm("DH+100", Dehaze: 100) })
            {
                using var actual = Render(basis, arm);
                var width = Halo(actual); var ok = width <= .010;
                Report("G5", new { arm = arm.Name, size = Size(basis), control = baseline, width, limit = .010, pass = ok });
                pass &= ok;
            }
        }
        Assert.True(pass, "G5 halo miss; only lattice Dehaze has a pre-approved refinement fallback");
    }

    private static double Halo(MagickImage image)
    {
        var rgb = RenderPipelineTestSupport.ReadPixels(image); var w = (int)image.Width; var y = (int)image.Height / 2;
        double Luma(int x) { var i = (y * w + x) * 3; return OpsWorkloads.Luma(rgb[i], rgb[i + 1], rgb[i + 2]) / 65535; }
        var left = Luma(w / 4); var right = Luma(w * 3 / 4); var tolerance = .02 * Math.Abs(right - left);
        var leftWidth = 0; var rightWidth = 0;
        for (var x = w / 4; x < w / 2; x++) if (Math.Abs(Luma(x) - left) > tolerance) leftWidth = Math.Max(leftWidth, w / 2 - x);
        for (var x = w / 2; x < w * 3 / 4; x++) if (Math.Abs(Luma(x) - right) > tolerance) rightWidth = Math.Max(rightWidth, x - w / 2 + 1);
        return Math.Max(leftWidth, rightWidth) / (double)Math.Max(image.Width, image.Height);
    }

    [Fact]
    public void G6Haze()
    {
        OptIn(); var loader = Loader(); var file = LocalFile("canon-eos-6d-iso-6400.cr2");
        using var full = loader.LoadFullBase(file, BaseDecodeSettings.Default, CancellationToken.None); Assert.NotNull(full);
        using var pixels = new MagickImage(full.Pixels); pixels.Resize(1600, 0);
        using var small = new BaseImage(new MagickImage(pixels), full.Info);
        // Synthetic observation is generated at each resolution from the same clear
        // full decode, unlike G1's independent real preview decode pair.
        double[]? referenceAir = null; var pass = true;
        foreach (var basis in new[] { full, small })
        {
            using var hazy = OpsWorkloads.Haze(basis);
            using var original = Render(basis, OpsArm.Off);
            using var before = Render(hazy, OpsArm.Off);
            using var after = Render(hazy, new("DH+50", Dehaze: 50));
            var baseline = Compare(original, before).MeanDeltaE; var corrected = Compare(original, after).MeanDeltaE;
            var air = OpsDehaze.Build(hazy.Pixels, ChromaticAdaptation.Identity(), Workers).Airlight;
            referenceAir ??= air;
            var relative = air.Zip(referenceAir, (a, b) => Math.Abs(a - b) / Math.Abs(b)).ToArray();
            var reduction = 1 - corrected / baseline; var valid = baseline >= 10 && baseline <= 20;
            var ok = valid && reduction >= .4 && relative.All(v => v <= .01); pass &= ok;
            Report("G6", new { size = Size(basis), constructionAirlight = OpsWorkloads.HazeAirlight,
                transmission = OpsWorkloads.HazeTransmission, variation = OpsWorkloads.HazeVariation, control = baseline, controlMin = 10, controlMax = 20,
                corrected, reduction, reductionLimit = .4, air, relative, airlightLimit = .01, valid, pass = ok });
        }
        Assert.True(pass, "G6 haze recovery or airlight agreement miss");
    }

    [Fact]
    public void G7Determinism()
    {
        OptIn(); using var pair = Loader().LoadPreviewBaseWithOutcome(LocalFile(), BaseDecodeSettings.Default, CancellationToken.None).Pair;
        Assert.NotNull(pair); var basis = pair.Interactive;
        foreach (var arm in new[] { OpsArm.Stack, OpsArm.Locals })
        {
            using var reference = Render(basis, arm, workers: 1, rows: 1);
            var codes = RenderPipelineTestSupport.ReadPixels(reference);
            foreach (var workers in new[] { 1, 2, Workers }.Distinct()) foreach (var rows in new[] { 1, 32 })
            {
                using var actual = Render(basis, arm, workers: workers, rows: rows);
                using var repeat = Render(basis, arm, workers: workers, rows: rows);
                var values = RenderPipelineTestSupport.ReadPixels(actual);
                var baseline = values.Zip(RenderPipelineTestSupport.ReadPixels(repeat), (a, b) => a != b ? 1 : 0).Sum();
                var differing = codes.Zip(values, (a, b) => a != b ? 1 : 0).Sum();
                Report("G7", new { arm = arm.Name, size = Size(basis), workers, rows, control = baseline, differing, limit = 0 });
                Assert.Equal(0, baseline); Assert.Equal(0, differing);
            }
        }
    }
}
