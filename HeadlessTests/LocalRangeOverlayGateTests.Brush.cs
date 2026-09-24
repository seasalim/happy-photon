using System.Diagnostics;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

public sealed partial class LocalRangeOverlayGateTests
{
    [AvaloniaFact]
    public async Task BrushRequestedOverlay()
    {
        Assert.SkipWhen(Environment.GetEnvironmentVariable("HAPPY_PHOTON_PERF") != "1", "Opt-in approved brush mask gate");
        if (Environment.ProcessorCount <= 2) throw new InvalidOperationException("Full CPU required");
#if DEBUG
        throw new InvalidOperationException("Use Release");
#endif
        var samples = int.Parse(Environment.GetEnvironmentVariable("LOCALS_SAMPLES") ?? "5");
        Assert.Equal(5, samples);
        var standard = Environment.GetEnvironmentVariable("HAPPY_PHOTON_LOCALS_FIXTURE") == "standard";
        var path = GoldenTestPaths.Asset(standard ? "iphone-14-pro-iso-1000.heic" : "canon-eos-6d-iso-6400.cr2");
        if (((int)File.GetAttributes(path) & (0x1000 | 0x40000 | 0x400000)) != 0)
            throw new InvalidOperationException("Fixture must be locally available");
        using var fixture = new CatalogVmFixture("brush-overlay-report");
        using var catalog = await fixture.CreateCatalogAsync();
        await using var vm = fixture.CreateViewModel(catalog, new BaseLoaderRouter(new RawBaseLoader(), new StandardBaseLoader()), _ => Task.CompletedTask);
        var settings = LocalsBrushWorkloads.Settings(true);
        var image = new ImageFile(path) { EditSettings = settings };
        image.CatalogId = await catalog.GetOrCreateImageAsync(path);
        await catalog.SaveEditSettingsAsync(image.CatalogId, settings);
        vm.Browse.SetImages([image]); vm.IsDevelopMode = true; vm.SelectedImage = image;
        await TestWaits.UntilAsync(() => vm.PreviewImage != null && vm.IsHistoryLoaded);
        var size = vm.PreviewImage!.PixelSize;
        var edge = Math.Max(size.Width, size.Height);
        var documents = LocalsBrushWorkloads.Create(true, size.Width, size.Height);
        var brushSettings = LocalsBrushProduction.Attach(settings, documents);
        var selectedIndex = 0; LocalAdjustment selectedControl;
        using (var lease = vm.ImageService.Previews.AcquireLocalRangeBase(image, settings, edge))
        {
            if (lease == null) throw new InvalidOperationException("No matching loaded base");
            selectedControl = LargestSupport(lease.Base, settings, size);
            var coverage = LocalsBrushCoverage.Scan(lease.Base, settings, documents, size.Width, size.Height);
            selectedIndex = Array.IndexOf(coverage.Support, coverage.Support.Max());
            output.WriteLine(LocalsBrushWorkloads.Description(true));
            for (var i = 0; i < documents.Length; i++)
                output.WriteLine($"BCap brush={i + 1} geometry_pixels={coverage.Geometry[i]} support_pixels={coverage.Support[i]} " +
                    $"coverage={coverage.Support[i] / (double)(size.Width * size.Height):R}");
            output.WriteLine($"BCap selected={selectedIndex + 1} support_pixels={coverage.Support[selectedIndex]} " +
                $"size={size} selection_outside_timing=True");
        }
        var color = HappyPhotonColors.LocalMaskColor;
        var tint = (uint)(color.R << 16 | color.G << 8 | color.B);
        WriteableBitmap? drawable = null;
        void Request(int arm)
        {
            if (arm == 0) { GC.KeepAlive(vm.PreviewImage); return; }
            using var lease = vm.ImageService.Previews.AcquireLocalRangeBase(image, settings, edge);
            if (lease == null) throw new InvalidOperationException("No matching loaded base");
            drawable = arm == 1 ? LocalRangeMaskRenderer.Render(lease.Base, settings, selectedControl, size, tint, CancellationToken.None) :
                LocalRangeMaskRenderer.Render(lease.Base, brushSettings, brushSettings.Locals![selectedIndex], size, tint, CancellationToken.None);
        }
        var times = Enumerable.Range(0, 3).Select(_ => new double[samples]).ToArray();
        var memory = Enumerable.Range(0, 3).Select(_ => new long[samples]).ToArray();
        for (var sample = -1; sample < samples; sample++) for (var step = 0; step < 3; step++)
        {
            var arm = (step + Math.Max(0, sample)) % 3;
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            using var process = Process.GetCurrentProcess(); process.Refresh();
            var baseline = process.PrivateMemorySize64; var peak = baseline;
            using var stop = new ManualResetEventSlim();
            // Sampling cadence, never a latency assertion or a correctness wait.
            var sampler = Task.Run(() => { while (!stop.Wait(1)) { process.Refresh(); peak = Math.Max(peak, process.PrivateMemorySize64); } });
            var started = Stopwatch.GetTimestamp();
            try
            {
                Request(arm);
                var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                stop.Set(); await sampler;
                process.Refresh(); peak = Math.Max(peak, process.PrivateMemorySize64);
                if (sample >= 0) { times[arm][sample] = elapsed; memory[arm][sample] = peak - baseline; }
                output.WriteLine($"brush_requested_overlay fixture={(standard ? "HEIC" : "RAW")} sample={sample} first_request={sample < 0} " +
                    $"arm={new[] { "resting-off", "LH8", "BCap" }[arm]} ms={elapsed:R} private_bytes={peak - baseline} " +
                    $"size={size} drawable={drawable?.PixelSize.ToString() ?? "resting-preview"} upload=WriteableBitmap");
            }
            finally { stop.Set(); await sampler; drawable?.Dispose(); drawable = null; }
        }
        double Median(double[] values) => values.Order().ElementAt(values.Length / 2);
        // Fresh 2026-09-24 quiet-host medians; a 3 ms floor keeps a ~7 ms control from invalidating on noise.
        var reference = standard ? 9.6 : 7.1;
        var band = Math.Max(reference * .25, 3);
        var control = Median(times[1]);
        var valid = control >= reference - band && control <= reference + band;
        output.WriteLine($"brush_requested_overlay samples={samples} BCap_median_ms={Median(times[2]):R} LH8_median_ms={control:R} " +
            $"control_reference_ms={reference} control_band_ms={band:R} control_valid={valid} " +
            $"BCap_private_increment_bytes={Median(memory[2].Zip(memory[0], (a, b) => (double)(a - b)).ToArray())} " +
            $"LH8_private_increment_bytes={Median(memory[1].Zip(memory[0], (a, b) => (double)(a - b)).ToArray())} " +
            $"q16_frame_bytes={size.Width * (long)size.Height * 6} approved_gate=True");
        Assert.True(valid, $"Invalid run: LH8 overlay {control:R} ms is outside ±{band:R} ms of {reference:R} ms");
        Assert.True(Median(times[2]) <= 60, "BCap requested mask exceeds 60 ms");
        var privateIncrement = Median(memory[2].Zip(memory[0], (a, b) => (double)(a - b)).ToArray());
        Assert.True(privateIncrement <= Math.Max(size.Width * (long)size.Height * 6 * 1.5, 16 * 1024 * 1024),
            "BCap requested mask private memory exceeds max(150% Q16 frame, 16 MiB)");
        GC.KeepAlive(vm.PreviewImage);
    }
}
