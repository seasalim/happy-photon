using System.Diagnostics;
using System.Reflection;
using HappyPhoton.Models;
using HappyPhoton.Services;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

public sealed partial class LocalsFusedBaselineTests
{
    [Fact]
    public async Task BrushContendedTick()
    {
        BrushOptIn(); using var full = Load(true);
        var largePixels = new MagickImage(full.Pixels);
        BitmapConversionService.ResizeToMaxDimension(largePixels, 3200);
        using var large = new BaseImage(largePixels, full.Info);
        var smallPixels = new MagickImage(large.Pixels);
        BitmapConversionService.ResizeToMaxDimension(smallPixels, 1600);
        using var small = new BaseImage(smallPixels, full.Info);
        var restingSettings = (EditSettings)typeof(RenderSequenceGoldenTests).GetMethod("Settings",
            BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, null)!;
        var control = restingSettings.Clone(); control.Locals = LocalsBrushWorkloads.Settings(true).Locals;
        var documents = LocalsBrushWorkloads.Create(true, (int)small.Pixels.Width, (int)small.Pixels.Height);
        BrushCoverage(small, control, documents, true); BrushBypass(small, control);
        var pipeline = new RenderPipeline();
        var brushOn = LocalsBrushProduction.Attach(control, documents);
        var brushOff = LocalsBrushProduction.Attach(control, null);
        void Tick(int arm)
        {
            if (arm == 0) { using var result = pipeline.Render(new(small, control, RenderIntent.Preview, 1600, new(false, false))); }
            else { using var result = pipeline.Render(new(small, arm == 2 ? brushOn : brushOff, RenderIntent.Preview, 1600, new(false, false))); }
        }
        for (var warm = 0; warm < 3; warm++) for (var arm = 0; arm < 3; arm++) Tick(arm);
        var times = new[] { new double[Samples], new double[Samples], new double[Samples] };
        var allOverlapped = true;
        for (var sample = 0; sample < Samples; sample++) for (var step = 0; step < 3; step++)
        {
            var arm = (step + sample) % 3;
            using var started = new ManualResetEventSlim();
            var execution = RenderExecutionOptions.Resting(CancellationToken.None, 2, stage =>
            { if (stage == (large.Info.IsRawSource ? "raw-crossing" : "standard-tone")) started.Set(); });
            var resting = Task.Run(() => pipeline.RenderResting(new(large, restingSettings,
                RenderIntent.Preview, 3200, new(false, false)), execution));
            try
            {
                if (!started.Wait(TestWaits.Condition)) throw new InvalidOperationException("Resting render never entered tone stage");
                var overlaps = !resting.IsCompleted; allOverlapped &= overlaps;
                times[arm][sample] = Time(() => Tick(arm));
                Print(0, $"brush_contended sample={sample} arm={new[] { "LH8", "brushes-off", "BCap" }[arm]} ms={times[arm][sample]:R} " +
                    $"overlapped_at_start={overlaps} resting={large.Pixels.Width}x{large.Pixels.Height} cap=2 ");
            }
            finally { using var result = await resting; }
        }
        Print(0, $"brush_contended samples={Samples} LH8_median_ms={Median(times[0]):R} off_median_ms={Median(times[1]):R} " +
            $"on_median_ms={Median(times[2]):R} paired_delta_ms={Median(times[2].Zip(times[1], (a, b) => a - b).ToArray()):R} " +
            $"all_overlapped={allOverlapped} approved_gate=True");
        BrushControl(Median(times[0]), small.Info.IsRawSource ? 115.9 : 117.6, "LH8 contended");
        Assert.True(allOverlapped, "Invalid run: resting render did not overlap every tick");
        Assert.True(Median(times[2]) <= 175, "BCap contended tick exceeds 175 ms");
    }

    [Fact]
    public async Task BrushExportDelta()
    {
        BrushOptIn(); using var preview = Load(false);
        var raw = preview.Info.IsRawSource;
        var edits = LocalsBrushWorkloads.Settings(true);
        var documents = LocalsBrushWorkloads.Create(true, (int)preview.Pixels.Width, (int)preview.Pixels.Height);
        BrushCoverage(preview, edits, documents, true);
        using var directory = new TemporaryDirectory();
        var export = new ExportSettings { OutputFolder = directory.Path, Format = ExportFormat.Jpeg, Quality = 85,
            OutputColorSpace = OutputColorSpace.Srgb, ExportWeb = raw, ExportSmall = raw, WebMaxSize = 2048,
            SmallMaxSize = 1024, OutputSharpening = raw ? OutputSharpeningMode.Screen : OutputSharpeningMode.Off };
        var file = new ImageFile(GoldenTestPaths.Asset(Fixture));
        var pipeline = new RenderPipeline();
        var brushOn = LocalsBrushProduction.Attach(edits, documents);
        var brushOff = LocalsBrushProduction.Attach(edits, null);
        var currentArm = 0; var renders = 0; uint renderWidth = 0, renderHeight = 0;
        var service = new ImageExportService(pipeline, Loader(), new ExportMetadataService(),
            new DcpProfileService(new SourceAvailabilityService()), request =>
            {
                renders++; renderWidth = request.Base.Pixels.Width; renderHeight = request.Base.Pixels.Height;
                return pipeline.RenderDisplayRec2020(request);
            });
        var times = Enumerable.Range(0, 4).Select(_ => new double[Samples]).ToArray();
        var peaks = Enumerable.Range(0, 4).Select(_ => new double[Samples]).ToArray();
        for (var sample = -1; sample < Samples; sample++) for (var step = 0; step < 4; step++)
        {
            currentArm = (step + Math.Max(0, sample)) % 4;
            file.EditSettings = currentArm switch { 0 => new EditSettings(), 1 => edits, 2 => brushOff, _ => brushOn };
            export.NamingPattern = "{name}-brush-" + currentArm + "-" + (sample + 1);
            renders = 0;
            using var memory = new BrushPrivateMemorySampler();
            var start = Stopwatch.GetTimestamp();
            var result = await service.ExportBatchAsync([file], export);
            var ms = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            memory.Finish();
            if (result.ExportedCount != 1) throw new InvalidOperationException(string.Join(';', result.FailedTargets.Select(t => t.FailureReason)));
            if (sample >= 0) { times[currentArm][sample] = ms; peaks[currentArm][sample] = memory.Peak; }
            Print(0, $"brush_export sample={sample} warmup={sample < 0} arm={new[] { "no-locals", "LH8", "brushes-off", "BCap" }[currentArm]} " +
                $"ms={ms:R} private_baseline_bytes={memory.Baseline} peak_process_private_bytes={memory.Peak} private_delta_bytes={memory.Peak - memory.Baseline} full_resolution=True size={renderWidth}x{renderHeight} variants={(raw ? 3 : 1)} upstream_renders={renders} ");
        }
        output.WriteLine($"brush_export_private no_locals_peak_median_bytes={Median(peaks[0]):R} BCap_peak_median_bytes={Median(peaks[3]):R} " +
            $"paired_peak_delta_bytes={Median(peaks[3].Zip(peaks[0], (a, b) => a - b).ToArray()):R} samples={Samples} " +
            "same_process_order_and_gc_commitment_can_confound_delta=True");
        var controlDelta = Median(times[1].Zip(times[0], (a, b) => a - b).ToArray());
        Print(0, $"brush_export samples={Samples} no_locals_median_ms={Median(times[0]):R} LH8_median_ms={Median(times[1]):R} " +
            $"brushes_off_median_ms={Median(times[2]):R} BCap_median_ms={Median(times[3]):R} " +
            $"delta_vs_no_locals_ms={Median(times[3].Zip(times[0], (a, b) => a - b).ToArray()):R} " +
            $"delta_vs_same_kernel_off_ms={Median(times[3].Zip(times[2], (a, b) => a - b).ToArray()):R} approved_gate=True");
        BrushControl(Median(times[0]), raw ? 2300 : 950, "no-locals export");
        // Fresh 2026-09-24 quiet-host median; the paired delta varies ~175-262 ms on RAW.
        BrushControl(controlDelta, raw ? 200 : 145, "LH8 export delta", floorMs: 75);
        Assert.True(Median(times[3].Zip(times[0], (a, b) => a - b).ToArray()) <= Math.Max(Median(times[0]) * .05, 700),
            "BCap export delta exceeds max(5%, 700 ms)");
    }
}
