using System.Diagnostics;
using System.Text.Json;
using HappyPhoton.Models;
using HappyPhoton.Services;
using Xunit;
using static HappyPhoton.Tests.FinishingGateSupport;

namespace HappyPhoton.Tests;

[Collection(AvaloniaTestCollection.Name)]
public sealed partial class FinishingGateTests(ITestOutputHelper output)
{
    private void Report(string gate, FinishingCandidate candidate, string fixture, object values) =>
        output.WriteLine("FINISHING " + JsonSerializer.Serialize(new
        {
            gate, candidate = candidate.Id, fixture, pid = Environment.ProcessId,
            cpu = Environment.ProcessorCount, candidateSha256 = candidate.LoadedSha256,
            assemblySha256 = AssemblyHash, productionSha256 = ProductionHash, values
        }));

    private static double Time(Action action)
    {
        var start = Stopwatch.GetTimestamp();
        action();

        return Stopwatch.GetElapsedTime(start).TotalMilliseconds;
    }

    [Fact]
    public void G1Tick()
    {
        OptIn();
        using var pair = Loader().LoadPreviewBaseWithOutcome(
            LocalFile(GoldenTestPaths.Asset(Fixture)), BaseDecodeSettings.Default, default).Pair;
        Assert.NotNull(pair);

        foreach (var candidate in Candidates())
        {
            var settings = FinishingLookHarness.Apply(new(), candidate.Settings);

            void Tick(EditSettings edits)
            {
                using var result = new RenderPipeline().Render(
                    new(pair.Interactive, edits, RenderIntent.Preview, 1600, new(false, false)));
            }

            for (var warm = 0; warm < 10; warm++)
            {
                Tick(new());
                Tick(settings);
            }

            var off = new double[5];
            var on = new double[5];

            for (var sample = 0; sample < 5; sample++)
            {
                for (var step = 0; step < 2; step++)
                {
                    var active = (sample + step) % 2 != 0;
                    (active ? on : off)[sample] = Time(() => Tick(active ? settings : new()));
                }
            }

            Report("G1", candidate, Fixture, new
            {
                control = Median(off), active = Median(on), increment = Median(on.Zip(off, (a, b) => a - b)),
                controlMin = pair.Interactive.Info.IsRawSource ? 22 : 15,
                controlMax = pair.Interactive.Info.IsRawSource ? 40 : 30,
                activeLimit = 150, incrementLimit = 60, off, on, warmPairs = 10, pairs = 5
            });
        }

        // RunFinishingGates gates the medians across five fresh processes.
    }

    [WindowsFact]
    public async Task G2Export()
    {
        OptIn();
        Assert.NotEqual("1", Environment.GetEnvironmentVariable("HAPPY_PHOTON_TEST_SKIA_ONLY"));
        var file = LocalFile(GoldenTestPaths.Asset(Fixture));
        using var preview = Loader().LoadPreviewBase(file, BaseDecodeSettings.Default, default);
        Assert.NotNull(preview);
        var raw = preview.Info.IsRawSource;
        using var directory = new TemporaryDirectory();
        var pipeline = new RenderPipeline();
        var renders = 0;
        var service = new ImageExportService(pipeline, Loader(), new ExportMetadataService(),
            new DcpProfileService(new SourceAvailabilityService()), request =>
            {
                renders++;

                return pipeline.RenderDisplayRec2020(request);
            });
        var export = new ExportSettings
        {
            OutputFolder = directory.Path, Format = ExportFormat.Jpeg, Quality = 85,
            OutputColorSpace = OutputColorSpace.Srgb, ExportWeb = raw, ExportSmall = raw,
            WebMaxSize = 2048, SmallMaxSize = 1024,
            OutputSharpening = raw ? OutputSharpeningMode.Screen : OutputSharpeningMode.Off
        };
        var allPass = true;

        foreach (var candidate in Candidates())
        {
            var off = new double[5];
            var on = new double[5];

            for (var sample = -1; sample < 5; sample++)
            {
                // OPS-WP1: control first for the warm-up and even samples.
                for (var step = 0; step < 2; step++)
                {
                    var active = sample % 2 == 1 ? step == 0 : step == 1;
                    renders = 0;
                    file.EditSettings = active ? FinishingLookHarness.Apply(new(), candidate.Settings) : new();
                    export.NamingPattern = "{name}-finishing-" + Guid.NewGuid().ToString("N");
                    using var memory = new BrushPrivateMemorySampler();
                    var start = Stopwatch.GetTimestamp();
                    var result = await service.ExportBatchAsync([file], export);
                    var ms = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                    memory.Finish();
                    Assert.True(result.ExportedCount == 1,
                        string.Join(';', result.FailedTargets.Select(target => target.FailureReason)));
                    Assert.Equal(1, renders);

                    if (sample >= 0)
                    {
                        (active ? on : off)[sample] = ms;
                    }
                }
            }

            var control = Median(off);
            var increment = Median(on.Zip(off, (a, b) => a - b));
            var limit = Math.Max(.10 * control, 900);
            var valid = control >= (raw ? 1850 : 700) && control <= (raw ? 2900 : 1200);
            var pass = valid && increment <= limit;
            allPass &= pass;
            Report("G2", candidate, Fixture, new
            {
                control, increment, limit, valid, pass, off, on, warmPairs = 1, pairs = 5,
                controlMin = raw ? 1850 : 700, controlMax = raw ? 2900 : 1200,
                variants = raw ? 3 : 1, backend = "Windows/WIC"
            });
        }

        Assert.True(allPass, "G2 miss or invalid control; retain all observations");
    }
}
