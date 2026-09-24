using System.Diagnostics;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

public sealed partial class LocalRangeOverlayGateTests(ITestOutputHelper output)
{
    [AvaloniaFact]
    public Task QualifiedRangeProductionRequestedOverlay() => RequestedOverlay(false);

    [AvaloniaFact]
    public Task QualifiedRangeHueRequestedOverlay() => RequestedOverlay(true);

    [AvaloniaFact]
    public Task QualifiedRangeHueEightRequestedOverlay() => RequestedOverlay(true, true);

    private async Task RequestedOverlay(bool hue, bool eight = false)
    {
        Assert.SkipWhen(Environment.GetEnvironmentVariable("HAPPY_PHOTON_PERF") != "1", "Opt-in overlay gate");
        Assert.True(Environment.ProcessorCount > 2, "Full CPU required");
        var standard = Environment.GetEnvironmentVariable("HAPPY_PHOTON_LOCALS_FIXTURE") == "standard";
        var path = GoldenTestPaths.Asset(standard ? "iphone-14-pro-iso-1000.heic" : "canon-eos-6d-iso-6400.cr2");
        Assert.Equal(0, (int)File.GetAttributes(path) & (0x1000 | 0x40000 | 0x400000));
        using var fixture = new CatalogVmFixture("range-overlay-gate");
        using var catalog = await fixture.CreateCatalogAsync();
        await using var vm = fixture.CreateViewModel(catalog, new BaseLoaderRouter(new RawBaseLoader(), new StandardBaseLoader()), _ => Task.CompletedTask);
        var settings = new EditSettings { Locals = [new() { Type = "radial", Rx = .3, Ry = .2,
            Angle = 30, Feather = .5, Hue = hue ? new() { Enabled = true, Center = 350 } : null, Luminance = new() { Enabled = true, Lower = .47 } }] };
        if (eight) settings = EightRangeSettings();
        var image = new ImageFile(path) { EditSettings = settings };
        image.CatalogId = await catalog.GetOrCreateImageAsync(path);
        await catalog.SaveEditSettingsAsync(image.CatalogId, settings);
        vm.Browse.SetImages([image]); vm.IsDevelopMode = true; vm.SelectedImage = image;
        await TestWaits.UntilAsync(() => vm.PreviewImage != null && vm.IsHistoryLoaded);
        var size = vm.PreviewImage!.PixelSize;
        var edge = Math.Max(size.Width, size.Height);
        if (!eight)
        {
            settings.Locals![0].Rx = Math.Sqrt(.45 * size.Width / edge * size.Height / edge / (Math.PI * 2 / 3));
            settings.Locals[0].Ry = settings.Locals[0].Rx * 2 / 3;
        }
        var selected = settings.Locals![0];
        if (eight)
        {
            using var lease = vm.ImageService.Previews.AcquireLocalRangeBase(image, settings, edge);
            selected = LargestSupport(lease!.Base, settings, size);
            vm.SelectedLocal = vm.Locals.Single(local => local.Id == selected.Id);
        }
        WriteableBitmap? drawable = null;
        var color = HappyPhotonColors.LocalMaskColor;
        var tint = (uint)(color.R << 16 | color.G << 8 | color.B);
        void Request()
        {
            using var lease = vm.ImageService.Previews.AcquireLocalRangeBase(image, settings, Math.Max(size.Width, size.Height));
            Assert.NotNull(lease);
            drawable = LocalRangeMaskRenderer.Render(lease.Base, settings, selected, size, tint, CancellationToken.None);
            Assert.Equal(size, drawable.PixelSize);
        }
        var firstRequest = Stopwatch.StartNew();
        Request();
        output.WriteLine($"first_request_ms={firstRequest.Elapsed.TotalMilliseconds:F4}");
        drawable!.Dispose(); drawable = null;
        var times = new double[5]; var increments = new long[5];
        using var process = Process.GetCurrentProcess();
        for (var i = 0; i < times.Length; i++)
        {
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            process.Refresh(); var baseline = process.PrivateMemorySize64; var peak = baseline;
            using var stop = new ManualResetEventSlim();
            // Sampling cadence only; no duration-based correctness assertion.
            var sampler = Task.Run(() => { while (!stop.Wait(1)) { process.Refresh(); peak = Math.Max(peak, process.PrivateMemorySize64); } });
            var clock = Stopwatch.StartNew();
            try { Request(); times[i] = clock.Elapsed.TotalMilliseconds; }
            finally { stop.Set(); await sampler; }
            process.Refresh(); peak = Math.Max(peak, process.PrivateMemorySize64);
            increments[i] = peak - baseline;
            drawable!.Dispose(); drawable = null;
        }
        var ms = times.Order().ElementAt(2); var memory = increments.Order().ElementAt(2);
        var limit = Math.Max(size.Width * (long)size.Height * 6 * 1.5, 16 * 1048576);
        output.WriteLine($"production_overlay fixture={(standard ? "HEIC" : "RAW")} size={size} cpu={Environment.ProcessorCount} " +
            $"median_ms={ms:F4} private_increment={memory} limit={limit} workload={(eight ? "LH8" : "R1")} selected={selected.Ordinal} bounds=WP6 upload=WriteableBitmap control=resting_preview_ShowMask_off " +
            $"times=[{string.Join(',', times)}] private=[{string.Join(',', increments)}]");
        Assert.True(ms <= 60, "Requested overlay time"); Assert.True(memory <= limit, "Requested overlay private increment");
        GC.KeepAlive(vm.PreviewImage);
    }
}
