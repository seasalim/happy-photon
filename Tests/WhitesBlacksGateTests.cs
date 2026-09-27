using System.Diagnostics;
using System.Text;
using System.Text.Json;
using HappyPhoton.Models;
using HappyPhoton.Services;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

[Collection(AvaloniaTestCollection.Name)]
public sealed partial class WhitesBlacksGateTests(ITestOutputHelper output)
{
    private static string Fixture => Environment.GetEnvironmentVariable("HAPPY_PHOTON_OPS_FIXTURE") == "standard"
        ? "iphone-14-pro-iso-1000.heic" : "canon-eos-6d-iso-6400.cr2";
    private static string Folder => Directory.CreateDirectory(Path.Combine(GoldenTestPaths.RepositoryRoot, "artifacts", "ops-wp2")).FullName;
    private static IBaseImageLoader Loader() => new GatedBaseImageLoader(new BaseLoaderRouter(new RawBaseLoader(), new StandardBaseLoader()), new SourceAvailabilityService());
    private static ImageFile File()
    {
        var path = GoldenTestPaths.Asset(Fixture);
        Assert.Equal(SourceAvailability.AvailableLocally, new SourceAvailabilityService().GetAvailability(path));
        return new(path);
    }
    private static void OptIn()
    {
        Assert.SkipWhen(Environment.GetEnvironmentVariable("HAPPY_PHOTON_PERF") != "1", "Opt-in OPS-WP2 qualification");
        PerfEnvironment.AssertFullCpu();
#if DEBUG
        Assert.Fail("Use Release");
#endif
    }
    private void Report(string gate, object values) => output.WriteLine("OPS_WP2 " + JsonSerializer.Serialize(new
    { gate, fixture = Fixture, pid = Environment.ProcessId, values }));
    private static double Time(Action action) { var start = Stopwatch.GetTimestamp(); action(); return Stopwatch.GetElapsedTime(start).TotalMilliseconds; }
    private static double Median(IEnumerable<double> values) { var sorted = values.Order().ToArray(); return sorted[sorted.Length / 2]; }
    private static EditSettings Points(int amount) => new() { Whites = amount, Blacks = amount };
    private static MagickImage Render(BaseImage basis, EditSettings settings, RenderIntent intent) =>
        new CurrentPipelineGoldenRenderer().Render(basis, settings, intent, (int)Math.Max(basis.Pixels.Width, basis.Pixels.Height));

    [Fact]
    public void G1G2Ticks()
    {
        OptIn(); using var pair = Loader().LoadPreviewBaseWithOutcome(File(), BaseDecodeSettings.Default, CancellationToken.None).Pair;
        Assert.NotNull(pair); var basis = pair.Interactive;
        foreach (var amount in new[] { 60, -60, 0 })
        {
            var controlSettings = amount == 0 ? HealWorkloads.LH8() : new EditSettings();
            var active = amount == 0 ? controlSettings.Clone() : Points(amount);
            if (amount == 0) foreach (var local in active.Locals!) { local.Whites = 30; local.Blacks = -20; }
            void Control() { using var result = new RenderPipeline().Render(new(basis, controlSettings, RenderIntent.Preview, 1600, new(false, false))); }
            void Active() { using var result = new RenderPipeline().Render(new(basis, active, RenderIntent.Preview, 1600, new(false, false))); }
            for (var i = 0; i < 10; i++) { Control(); Active(); }
            var off = new double[5]; var on = new double[5];
            for (var i = 0; i < 5; i++)
                if (i % 2 == 0) { off[i] = Time(Control); on[i] = Time(Active); }
                else { on[i] = Time(Active); off[i] = Time(Control); }
            Report(amount == 0 ? "G2" : "G1", new { amount, control = Median(off), active = Median(on),
                increment = Median(on.Zip(off, (a, b) => a - b)), off, on });
        }
        // Qualification compares medians over five fresh processes, including same-session control ranges.
    }

    [WindowsFact]
    public async Task G3Export()
    {
        OptIn(); using var directory = new TemporaryDirectory();
        var file = File(); var service = new ImageExportService(new RenderPipeline(), Loader(), new ExportMetadataService());
        var raw = Fixture.EndsWith("cr2");
        var export = new ExportSettings { OutputFolder = directory.Path, Format = ExportFormat.Jpeg, Quality = 85,
            OutputColorSpace = OutputColorSpace.Srgb, ExportWeb = raw, ExportSmall = raw, WebMaxSize = 2048,
            SmallMaxSize = 1024, OutputSharpening = raw ? OutputSharpeningMode.Screen : OutputSharpeningMode.Off };
        foreach (var amount in new[] { 60, -60 })
        {
            var off = new double[5]; var on = new double[5];
            for (var sample = -1; sample < 5; sample++)
            {
                double before, after;
                if (sample % 2 != 1) { before = await Run(0); after = await Run(amount); }
                else { after = await Run(amount); before = await Run(0); }
                if (sample >= 0) { off[sample] = before; on[sample] = after; }
            }
            var control = Median(off); var delta = Median(on.Zip(off, (a, b) => a - b));
            Report("G3", new { amount, control, delta, off, on, limit = Math.Max(.05 * control, 500) });
            Assert.InRange(control, raw ? 1850 : 700, raw ? 2900 : 1200);
            Assert.True(delta <= Math.Max(.05 * control, 500));
        }
        async Task<double> Run(int amount)
        {
            file.EditSettings = Points(amount); export.NamingPattern = "{name}-" + Guid.NewGuid().ToString("N");
            var start = Stopwatch.GetTimestamp(); var result = await service.ExportBatchAsync([file], export);
            Assert.Equal(1, result.ExportedCount); return Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        }
    }

    [Fact]
    public void G4Parity()
    {
        OptIn(); var loader = Loader(); var file = File();
        using var pair = loader.LoadPreviewBaseWithOutcome(file, BaseDecodeSettings.Default, CancellationToken.None).Pair;
        using var full = loader.LoadFullBase(file, BaseDecodeSettings.Default, CancellationToken.None);
        Assert.NotNull(pair?.Large); Assert.NotNull(full);
        using var controlFull = Render(full, new(), RenderIntent.Export);
        var pass = true;
        foreach (var basis in new[] { pair.Interactive, pair.Large })
        {
            using var control = Render(basis, new(), RenderIntent.Preview);
            var baseline = Compare(controlFull, control);
            foreach (var amount in new[] { -100, -60, 60, 100 })
            {
                using var expected = Render(full, Points(amount), RenderIntent.Export);
                using var actual = Render(basis, Points(amount), RenderIntent.Preview);
                var metric = Compare(expected, actual);
                var ok = metric.MeanDeltaE <= baseline.MeanDeltaE + .5 && metric.P99DeltaE <= baseline.P99DeltaE + 2;
                var reportOnly = amount != -100;
                Report("G4", new { amount, reportOnly, width = basis.Pixels.Width, height = basis.Pixels.Height,
                    ownerRuling = "2026-09-27: assert -100; report-only -60, +60 and +100",
                    controlMean = baseline.MeanDeltaE, controlP99 = baseline.P99DeltaE,
                    mean = metric.MeanDeltaE, p99 = metric.P99DeltaE, pass = ok });
                if (!reportOnly) pass &= ok;
            }
        }
        Assert.True(pass, "G4 relative parity miss");
    }
    private static GoldenComparison Compare(MagickImage full, MagickImage preview)
    {
        using var aligned = new MagickImage(full); WysiwygTests.AlignForComparison(aligned, preview);
        return GoldenImageComparer.Compare(aligned, preview, GoldenComparisonDomain.DisplaySrgb);
    }

    [Fact]
    public void ProductionReviewSheets()
    {
        OptIn(); using var pair = Loader().LoadPreviewBaseWithOutcome(File(), BaseDecodeSettings.Default, CancellationToken.None).Pair;
        Assert.NotNull(pair); var folder = Directory.CreateDirectory(Path.Combine(Folder, "sheets", Fixture)).FullName;
        var html = new StringBuilder("<!doctype html><meta charset=utf-8><title>Whites and Blacks</title><h1>OPS-WP2 production look</h1><p>Operators off at left, production treatment at right. Owner approval pending.</p>");
        using var control = Render(pair.Interactive, new(), RenderIntent.Preview);
        foreach (var amount in new[] { -100, -60, 60, 100 })
        foreach (var mode in new[] { "whites", "blacks", "combined", "local" })
        {
            var settings = mode switch
            {
                "whites" => new EditSettings { Whites = amount }, "blacks" => new() { Blacks = amount },
                "local" => new() { Locals = [new() { Whites = amount, Blacks = -amount }] }, _ => Points(amount)
            };
            using var actual = Render(pair.Interactive, settings, RenderIntent.Preview);
            var slug = $"{mode}-{amount}";
            foreach (var crop in new[] { false, true })
            {
                using var images = new MagickImageCollection();
                foreach (var source in new[] { control, actual })
                {
                    var image = new MagickImage(source);
                    if (crop) { image.Crop(new MagickGeometry((int)(image.Width - 400) / 2, (int)(image.Height - 400) / 2, 400, 400)); image.ResetPage(); }
                    else image.Resize(500, 0);
                    images.Add(image);
                }
                using var sheet = images.AppendHorizontally(); sheet.Depth = 8;
                var name = slug + (crop ? "-crop" : "") + ".png"; sheet.Write(Path.Combine(folder, name));
                html.Append("<h2>").Append(slug).Append("</h2><img src=\"").Append(name).Append("\">");
            }
        }
        System.IO.File.WriteAllText(Path.Combine(folder, "index.html"), html.ToString());
        Report("sheets", new { folder, ownerApproved = false });
    }
}
