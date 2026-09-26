using System.Diagnostics;
using HappyPhoton.Models;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public sealed partial class OpsGateTests
{
    [Fact]
    public void G3Tick()
    {
        OptIn(); using var pair = Loader().LoadPreviewBaseWithOutcome(LocalFile(), BaseDecodeSettings.Default, CancellationToken.None).Pair;
        Assert.NotNull(pair); var basis = pair.Interactive;
        OpsDehaze? analysis = null;
        var latticeBuildMs = Time(() => analysis = OpsDehaze.Build(basis.Pixels,
            RenderChromaticStage.CreateWhiteBalanceMatrix(basis.Info, new()), Workers));
        Report("G3-lattice-build", new { size = Size(basis), latticeBuildMs, reportOnly = true, builds = 1 });
        foreach (var arm in OpsArm.All)
        {
            void Control() { using var result = new RenderPipeline().Render(new(basis, new(), RenderIntent.Preview, 1600, new(false, false))); }
            void Active() { using var result = OpsRenderHarness.Render(basis, arm, Candidate, Refine, RenderIntent.Preview, Workers, analysis: analysis); }
            // Ten alternating warm-ups match the qualified production NL harness.
            for (var i = 0; i < 10; i++) { Control(); Active(); }
            var off = new double[5]; var on = new double[5];
            for (var i = 0; i < 5; i++)
            {
                if (i % 2 == 0) { off[i] = Time(Control); on[i] = Time(Active); }
                else { on[i] = Time(Active); off[i] = Time(Control); }
            }
            var control = Median(off); var increment = Median(on.Zip(off, (a, b) => a - b));
            var limit = arm.Name.StartsWith("DH") ? 20 : arm.Name == "OP" ? 60 : 25;
            Report("G3", new { arm = arm.Name, size = Size(basis), control, increment, limit,
                controlMin = basis.Info.IsRawSource ? 22 : 15, controlMax = basis.Info.IsRawSource ? 40 : 30,
                off, on, samples = 5, latticeCached = true, extraPixelCopyBytes = (long)basis.Pixels.Width * basis.Pixels.Height * 6 });
        }
        // The runner asserts the median of five fresh process pairs, never a single host.
    }

    [WindowsFact]
    public async Task G2Export() => await Export(false);

    [WindowsFact]
    public async Task G4Memory() => await Export(true);

    private async Task Export(bool memoryGate)
    {
        OptIn(); Assert.NotEqual("1", Environment.GetEnvironmentVariable("HAPPY_PHOTON_TEST_SKIA_ONLY"));
        using var preview = Loader().LoadPreviewBase(LocalFile(), BaseDecodeSettings.Default, CancellationToken.None);
        Assert.NotNull(preview); var raw = preview.Info.IsRawSource;
        using var directory = new TemporaryDirectory();
        var file = LocalFile(); var arm = OpsArm.Off; var renders = 0; long pixelCount = 0;
        var pipeline = new RenderPipeline();
        var service = new ImageExportService(pipeline, Loader(), new ExportMetadataService(),
            new DcpProfileService(new SourceAvailabilityService()), request =>
            {
                renders++; pixelCount = (long)request.Base.Pixels.Width * request.Base.Pixels.Height;
                return arm == OpsArm.Off ? pipeline.RenderDisplayRec2020(request) :
                    OpsRenderHarness.Upstream(request.Base, request.Settings, arm, Candidate, Refine,
                        RenderIntent.Export, Workers);
            });
        var export = new ExportSettings { OutputFolder = directory.Path, Format = ExportFormat.Jpeg, Quality = 85,
            OutputColorSpace = OutputColorSpace.Srgb, ExportWeb = raw, ExportSmall = raw, WebMaxSize = 2048,
            SmallMaxSize = 1024, OutputSharpening = raw ? OutputSharpeningMode.Screen : OutputSharpeningMode.Off };
        var allPass = true;
        foreach (var workload in memoryGate ? new[] { OpsArm.Stack, OpsArm.Locals } : OpsArm.All.ToArray())
        {
            var off = new double[5]; var on = new double[5]; var controlMemory = new double[5]; var peakDelta = new double[5];
            for (var sample = -1; sample < 5; sample++)
            {
                (double Ms, long Peak, long Delta) before, after;
                if (sample % 2 != 1) { before = await Run(OpsArm.Off, sample); after = await Run(workload, sample); }
                else { after = await Run(workload, sample); before = await Run(OpsArm.Off, sample); }
                if (sample < 0) continue;
                off[sample] = before.Ms; on[sample] = after.Ms;
                controlMemory[sample] = before.Delta;
                peakDelta[sample] = after.Peak - before.Peak;
            }
            var control = Median(off); var increment = Median(on.Zip(off, (a, b) => a - b));
            var controlBytes = Median(controlMemory); var deltaBytes = Median(peakDelta);
            var timeLimit = workload == OpsArm.Stack ? Math.Max(control * .10, 900) : Math.Max(control * .05, 500);
            var byteLimit = pixelCount * (workload.Local ? 10 : 6);
            var valid = memoryGate ? controlBytes >= (raw ? 400e6 : 110e6) && controlBytes <= (raw ? 720e6 : 280e6) :
                control >= (raw ? 1850 : 700) && control <= (raw ? 2900 : 1200);
            var pass = valid && (memoryGate ? deltaBytes <= byteLimit : increment <= timeLimit); allPass &= pass;
            Report(memoryGate ? "G4" : "G2", new { arm = workload.Name, control, increment, timeLimit, controlBytes, deltaBytes, byteLimit,
                controlMin = raw ? 1850 : 700, controlMax = raw ? 2900 : 1200,
                memoryMin = raw ? 400e6 : 110e6, memoryMax = raw ? 720e6 : 280e6,
                off, on, controlMemory, peakDelta, pixelCount, extraPixelCopyBytes = pixelCount * 6,
                backend = "Windows/WIC", variants = raw ? 3 : 1, samples = 5, valid, pass });
        }
        Assert.True(allPass, memoryGate ? "G4 memory miss or invalid control" : "G2 export miss or invalid control");

        async Task<(double Ms, long Peak, long Delta)> Run(OpsArm current, int sample)
        {
            arm = current; renders = 0; file.EditSettings = OpsWorkloads.Settings(current);
            export.NamingPattern = "{name}-ops-" + current.Name + "-" + sample + "-" + Guid.NewGuid().ToString("N");
            using var memory = new BrushPrivateMemorySampler();
            var start = Stopwatch.GetTimestamp(); var result = await service.ExportBatchAsync([file], export);
            var ms = Stopwatch.GetElapsedTime(start).TotalMilliseconds; memory.Finish();
            Assert.True(result.ExportedCount == 1, string.Join(';', result.FailedTargets.Select(t => t.FailureReason)));
            Assert.Equal(1, renders);
            Report("export-sample", new { arm = current.Name, sample, ms, baseline = memory.Baseline, peak = memory.Peak,
                privateDelta = memory.Peak - memory.Baseline, warmup = sample < 0 });
            return (ms, memory.Peak, memory.Peak - memory.Baseline);
        }
    }
}

