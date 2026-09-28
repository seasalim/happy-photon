using System.Diagnostics;
using HappyPhoton.Models;
using HappyPhoton.Services;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

public sealed partial class PresenceGateTests
{
    [WindowsFact]
    public async Task G3Export() => await Export(false);

    [WindowsFact]
    public async Task G4Memory() => await Export(true);

    private async Task Export(bool memoryGate)
    {
        OptIn();
        Assert.NotEqual("1", Environment.GetEnvironmentVariable("HAPPY_PHOTON_TEST_SKIA_ONLY"));
        using var preview = Loader().LoadPreviewBase(LocalFile(), BaseDecodeSettings.Default, default);
        Assert.NotNull(preview);
        var raw = preview.Info.IsRawSource;
        using var directory = new TemporaryDirectory();
        var file = LocalFile();
        long pixelCount = 0;
        var renders = 0;
        var pipeline = new RenderPipeline();
        var service = new ImageExportService(pipeline, Loader(), new ExportMetadataService(),
            new DcpProfileService(new SourceAvailabilityService()), request =>
            {
                renders++;
                pixelCount = (long)request.Base.Pixels.Width * request.Base.Pixels.Height;

                return pipeline.RenderDisplayRec2020(request);
            });
        var export = new ExportSettings
        {
            OutputFolder = directory.Path, Format = ExportFormat.Jpeg, Quality = 85,
            OutputColorSpace = OutputColorSpace.Srgb, ExportWeb = raw, ExportSmall = raw,
            WebMaxSize = 2048, SmallMaxSize = 1024,
            OutputSharpening = raw ? OutputSharpeningMode.Screen : OutputSharpeningMode.Off
        };
        var pass = true;

        foreach (var arm in memoryGate ? new[] { new OpsArm("CL+", Clarity: 60) } : Arms)
        {
            var off = new double[5];
            var on = new double[5];
            var controlMemory = new double[5];
            var peakDelta = new double[5];

            for (var sample = -1; sample < 5; sample++)
            {
                (double Ms, long Peak, long Delta) before;
                (double Ms, long Peak, long Delta) after;

                if (sample % 2 != 1)
                {
                    before = await Run(OpsArm.Off);
                    after = await Run(arm);
                }
                else
                {
                    after = await Run(arm);
                    before = await Run(OpsArm.Off);
                }

                if (sample < 0) continue;

                off[sample] = before.Ms;
                on[sample] = after.Ms;
                controlMemory[sample] = before.Delta;
                peakDelta[sample] = after.Peak - before.Peak;
            }

            var control = Median(off);
            var increment = Median(on.Zip(off, (a, b) => a - b));
            var controlBytes = Median(controlMemory);
            var deltaBytes = Median(peakDelta);
            var timeLimit = arm.Name == "both" ? Math.Max(control * .08, 700) : Math.Max(control * .05, 500);
            var byteLimit = pixelCount * 6;
            var valid = memoryGate
                ? controlBytes >= (raw ? 400e6 : 110e6) && controlBytes <= (raw ? 720e6 : 280e6)
                : control >= (raw ? 1850 : 700) && control <= (raw ? 2900 : 1200);
            var ok = valid && (memoryGate ? deltaBytes <= byteLimit : increment <= timeLimit);
            pass &= ok;
            Report(memoryGate ? "G4" : "G3", new { arm = arm.Name, control, increment, timeLimit,
                controlBytes, deltaBytes, byteLimit, off, on, controlMemory, peakDelta, valid, pass = ok });
        }

        Assert.True(pass, "Export gate miss or invalid control.");

        async Task<(double Ms, long Peak, long Delta)> Run(OpsArm arm)
        {
            renders = 0;
            file.EditSettings = Settings(arm);
            export.NamingPattern = "{name}-presence-" + Guid.NewGuid().ToString("N");
            using var memory = new BrushPrivateMemorySampler();
            var start = Stopwatch.GetTimestamp();
            var result = await service.ExportBatchAsync([file], export);
            var ms = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            memory.Finish();
            Assert.True(result.ExportedCount == 1, string.Join(';', result.FailedTargets.Select(t => t.FailureReason)));
            Assert.Equal(1, renders);

            return (ms, memory.Peak, memory.Peak - memory.Baseline);
        }
    }

    [WindowsFact]
    public async Task G6ProofExport()
    {
        OptIn();
        using var directory = new TemporaryDirectory();
        var file = LocalFile();
        var pipeline = new RenderPipeline();

        foreach (var active in new[] { false, true })
        {
            file.EditSettings = new()
            {
                Texture = active ? 40 : 0,
                Clarity = active ? 40 : 0,
                Detail = new() { CaptureSharpen = 25 }
            };
            var calls = 0;
            var service = new ImageExportService(pipeline, Loader(), new ExportMetadataService(),
                new DcpProfileService(new SourceAvailabilityService()), request =>
                {
                    calls++;
                    Assert.Equal(RenderIntent.Export, request.Intent);
                    Assert.True(request.Settings.Detail.ResolveCaptureSharpen(request.Base.Info.IsRawSource) > 0);
                    var proofUpstream = pipeline.RenderDisplayRec2020(new(request.Base, request.Settings,
                        RenderIntent.Export, null, new(false, false)));
                    using var proof = RenderFinalizer.FinalizeOwnedProof(proofUpstream, null,
                        OutputColorSpace.Srgb, OutputSharpeningMode.Off, request.Settings.Effects);
                    var upstream = pipeline.RenderDisplayRec2020(request);

                    try
                    {
                        using var canonical = RenderFinalizer.Finalize(upstream, null,
                            OutputColorSpace.Srgb, OutputSharpeningMode.Off, false, effects: request.Settings.Effects);
                        var left = RenderPipelineTestSupport.ReadPixels(proof);
                        var right = RenderPipelineTestSupport.ReadPixels(canonical);
                        Assert.Equal(left.Length, right.Length);
                        var differing = left.Zip(right).LongCount(pair => pair.First != pair.Second);
                        Report("G6", new { active, differing, codes = left.Length });
                        Assert.Equal(0, differing);

                        return upstream;
                    }
                    catch
                    {
                        upstream.Dispose();
                        throw;
                    }
                });
            var result = await service.ExportBatchAsync([file], new ExportSettings
            {
                OutputFolder = directory.Path, Format = ExportFormat.Jpeg,
                NamingPattern = "{name}-" + active, ExportHiRes = true,
                ExportWeb = false, ExportSmall = false, OutputColorSpace = OutputColorSpace.Srgb,
                OutputSharpening = OutputSharpeningMode.Off
            });
            Assert.Equal(1, result.ExportedCount);
            Assert.Equal(1, calls);
        }
    }
}
