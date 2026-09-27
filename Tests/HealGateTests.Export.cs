using System.Diagnostics;
using HappyPhoton.Models;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public sealed partial class HealGateTests
{
    [Fact]
    public async Task Wp3ExportDelta()
    {
        OptIn(); var file = LocalFile(); var raw = Fixture == Raw;
        using var directory = new TemporaryDirectory();
        var settings = HealWorkloads.LH8();
        var repaired = settings.Clone(); repaired.Repairs = HealWorkloads.Repairs(HealWorkloads.S64());
        var export = new ExportSettings { OutputFolder = directory.Path, Format = ExportFormat.Jpeg, Quality = 85,
            OutputColorSpace = OutputColorSpace.Srgb, ExportWeb = raw, ExportSmall = raw, WebMaxSize = 2048,
            SmallMaxSize = 1024, OutputSharpening = raw ? OutputSharpeningMode.Screen : OutputSharpeningMode.Off };
        var pipeline = new RenderPipeline();
        long frameBytes = 0; var renders = 0;
        var service = new ImageExportService(pipeline, Loader(), new ExportMetadataService(),
            new DcpProfileService(new SourceAvailabilityService()), request =>
            {
                renders++; frameBytes = (long)request.Base.Pixels.Width * request.Base.Pixels.Height * 6;
                return pipeline.RenderDisplayRec2020(request);
            });
        var times = Enumerable.Range(0, 3).Select(_ => new double[5]).ToArray();
        var peaks = Enumerable.Range(0, 3).Select(_ => new double[5]).ToArray();
        for (var sample = -1; sample < 5; sample++) for (var step = 0; step < 3; step++)
        {
            var arm = (step + Math.Max(0, sample)) % 3;
            file.EditSettings = arm switch { 0 => new(), 1 => settings, _ => repaired };
            export.NamingPattern = $"{{name}}-heal-{arm}-{sample + 1}";
            renders = 0;
            using var memory = new BrushPrivateMemorySampler();
            var start = Stopwatch.GetTimestamp();
            var result = await service.ExportBatchAsync([file], export);
            var ms = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            memory.Finish();
            Assert.Equal(1, result.ExportedCount); Assert.Equal(1, renders);
            if (sample >= 0) { times[arm][sample] = ms; peaks[arm][sample] = memory.Peak; }
            Report("WP3-export-sample", new { sample, arm, ms, memory.Baseline, memory.Peak, frameBytes });
        }
        var control = Median(times[0]);
        var localDelta = Median(times[1].Zip(times[0], (a, b) => a - b));
        var repairDelta = Median(times[2].Zip(times[1], (a, b) => a - b));
        var localPeak = Median(peaks[1].Zip(peaks[0], (a, b) => a - b));
        var repairPeak = Median(peaks[2].Zip(peaks[1], (a, b) => a - b));
        var limit = Math.Max(Median(times[1]) * .05, 500);
        Report("WP3-G3G4", new { control, localDelta, repairDelta, localPeak, repairPeak, frameBytes,
            timeLimit = limit, peakLimit = 64 * 1048576, times, peaks });
        Assert.InRange(control, raw ? 1850 : 760, raw ? 2900 : 1200);
        Assert.InRange(localDelta, 75, raw ? 350 : 250);
        Assert.True(repairDelta <= limit, "G3 S64 export increment exceeds max(5%, 500 ms)");
        if (raw)
        {
            Assert.True(localPeak <= frameBytes, "G4 LH8 control outside approved baseline range");
            Assert.True(repairPeak <= 64 * 1048576, "G4 repair peak delta exceeds 64 MiB");
        }
    }
}
