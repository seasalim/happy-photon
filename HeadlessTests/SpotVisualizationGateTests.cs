using System.Diagnostics;
using Avalonia;
using Avalonia.Headless.XUnit;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class SpotVisualizationGateTests(ITestOutputHelper output)
{
    [AvaloniaFact]
    public async Task G1CopyHighPassAndBitmapCreation()
    {
        Assert.SkipWhen(Environment.GetEnvironmentVariable("HAPPY_PHOTON_PERF") != "1", "Opt-in HEAL-WP6 G1");
        Assert.True(Environment.ProcessorCount > 2);
        using var fixture = new CatalogVmFixture("visualize-timing");
        using var catalog = await fixture.CreateCatalogAsync();
        var loader = new GatedBaseImageLoader(new RawBaseLoader(), new SourceAvailabilityService());
        await using var vm = fixture.CreateViewModel(catalog, loader, _ => Task.CompletedTask);
        var image = LocalRaw();
        var settings = LocalsBrushWorkloads.Settings(true);
        image.EditSettings = settings;
        image.CatalogId = await catalog.GetOrCreateImageAsync(image.FilePath);
        await catalog.SaveEditSettingsAsync(image.CatalogId, settings);
        vm.Browse.SetImages([image]);
        vm.IsDevelopMode = true;
        vm.SelectedImage = image;
        await TestWaits.UntilAsync(() => vm.PreviewImage != null && vm.IsHistoryLoaded);
        var size = vm.PreviewImage!.PixelSize;
        using var lease = vm.ImageService.Previews.AcquireLocalRangeBase(image, settings, Math.Max(size.Width, size.Height));
        Assert.NotNull(lease);
        var basis = lease.Base;
        var pipeline = new RenderPipeline();
        var documents = LocalsBrushWorkloads.Create(true, size.Width, size.Height);
        var brushSettings = LocalsBrushProduction.Attach(settings, documents);
        var coverage = LocalsBrushCoverage.Scan(basis, settings, documents, size.Width, size.Height);
        var selected = Array.IndexOf(coverage.Support, coverage.Support.Max());
        var color = HappyPhotonColors.LocalMaskColor;
        var tint = (uint)(color.R << 16 | color.G << 8 | color.B);
        var control = await MeasureBrushControlAsync(() =>
        {
            using var mask = LocalRangeMaskRenderer.Render(basis, brushSettings, brushSettings.Locals![selected],
                size, tint, CancellationToken.None);
        });
        using var fitRender = pipeline.Render(new(basis, new(), RenderIntent.Preview, null, new(false, false)));
        using var fit = BitmapConversionService.ConvertToBitmap(fitRender.Image)!;
        var fitMs = Measure(() => Visualize(fit, new Rect(0, 0, 1, 1)));
        using var full = loader.LoadFullBase(image, BaseDecodeSettings.Default, CancellationToken.None);
        Assert.NotNull(full);
        using var fullRender = pipeline.Render(new(full, new(), RenderIntent.Export, null, new(false, false)));
        using var refined = BitmapConversionService.ConvertToBitmap(fullRender.Image)!;
        var region = new Rect(0, 0, 2560d / refined.PixelSize.Width, 1440d / refined.PixelSize.Height);
        var viewportMs = Measure(() => Visualize(refined, region));
        output.WriteLine($"HEAL-WP6 G1 control_BCap_ms={control:F2} allowed=35..65 fit_ms={fitMs:F2} limit=60 viewport_ms={viewportMs:F2} limit=100 copy_and_bitmap_creation=included");
        Assert.InRange(control, 35, 65);
        Assert.InRange(fitMs, 0, 60);
        Assert.InRange(viewportMs, 0, 100);

        static void Visualize(Avalonia.Media.Imaging.Bitmap source, Rect region)
        {
            var copy = ZoomPanControl.CopySpotViewport(source, region, out var rect);
            using var result = SpotVisualizationRenderer.Render(copy, rect.Width, rect.Height, 50);
        }
    }

    [AvaloniaFact]
    public async Task G2S64ExportAndCachesAreByteIdentical()
    {
        using var fixture = new CatalogVmFixture("visualize-export");
        using var catalog = await fixture.CreateCatalogAsync();
        await using var vm = fixture.CreateViewModel(catalog, new RawBaseLoader(), _ => Task.CompletedTask);
        var image = LocalRaw();
        image.EditSettings.Repairs = HealWorkloads.Repairs(HealWorkloads.S64());
        image.CatalogId = await catalog.GetOrCreateImageAsync(image.FilePath);
        await catalog.SaveEditSettingsAsync(image.CatalogId, image.EditSettings);
        vm.IsDevelopMode = true;
        vm.SelectedImage = image;
        await TestWaits.UntilAsync(() => vm.PreviewImage != null && vm.IsHistoryLoaded);
        await vm.ToggleSpotsModeCommand.ExecuteAsync(null);
        await using var cache = new PreviewCacheService(catalog);
        cache.QueueSaveToCache(image, vm.PreviewImage!, RenderSettingsHash.Compute(image.EditSettings), File.GetLastWriteTimeUtc(image.FilePath));
        var cachePath = catalog.GetPreviewPath(image.CatalogId);
        await TestWaits.UntilAsync(() => File.Exists(cachePath) && File.Exists(Path.ChangeExtension(cachePath, ".meta")));
        var cacheBefore = File.ReadAllBytes(cachePath);
        var metaBefore = File.ReadAllBytes(Path.ChangeExtension(cachePath, ".meta"));
        var service = new ImageExportService(new RenderPipeline(), new RawBaseLoader(), new ExportMetadataService());
        var settings = new ExportSettings { OutputFolder = fixture.Path("off"), Format = ExportFormat.Png };
        await service.ExportBatchAsync([image], settings);
        vm.VisualizeSpots = true;
        vm.SpotVisualizeThreshold = 80;
        var viewer = new ZoomPanControl { Source = vm.PreviewImage, IsSpotsMode = true, VisualizeSpots = true };
        using var scope = new TestUiScope(new Avalonia.Controls.Window { Width = 800, Height = 600, Content = viewer });
        await TestWaits.UntilAsync(() => viewer.VisualizeBitmap != null);
        cache.QueueSaveToCache(image, vm.PreviewImage!, RenderSettingsHash.Compute(image.EditSettings), File.GetLastWriteTimeUtc(image.FilePath));
        await TestWaits.UntilAsync(() => cache.PendingWrites == 0 && cache.WriterInHandCount == 0);
        settings.OutputFolder = fixture.Path("on");
        await service.ExportBatchAsync([image], settings);
        var off = File.ReadAllBytes(Directory.GetFiles(fixture.Path("off"), "*.png", SearchOption.AllDirectories).Single());
        var on = File.ReadAllBytes(Directory.GetFiles(fixture.Path("on"), "*.png", SearchOption.AllDirectories).Single());
        Assert.Equal(off, on);
        Assert.Equal(cacheBefore, File.ReadAllBytes(cachePath));
        Assert.Equal(metaBefore, File.ReadAllBytes(Path.ChangeExtension(cachePath, ".meta")));
        output.WriteLine($"HEAL-WP6 G2 S64 RAW export_bytes={on.Length} differing_bytes=0 preview_cache_differing_bytes=0");
    }

    private static ImageFile LocalRaw()
    {
        var path = GoldenTestPaths.Asset("canon-eos-6d-iso-6400.cr2");
        Assert.Equal(SourceAvailability.AvailableLocally, new SourceAvailabilityService().GetAvailability(path));

        return new(path);
    }

    private static async Task<double> MeasureBrushControlAsync(Action request)
    {
        var times = new List<double>();

        // Keep the reference harness's per-sample GC and private-byte sampler outside timing.
        for (var sample = -1; sample < 5; sample++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            using var process = Process.GetCurrentProcess();
            process.Refresh();
            using var stop = new ManualResetEventSlim();
            var sampler = Task.Run(() =>
            {
                // Sampling cadence matches BrushRequestedOverlay, never an assertion wait.
                while (!stop.Wait(1)) process.Refresh();
            });

            try
            {
                var started = Stopwatch.GetTimestamp();
                request();
                var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                if (sample >= 0) times.Add(elapsed);
            }
            finally
            {
                stop.Set();
                await sampler;
            }
        }

        return times.Order().ElementAt(2);
    }

    private static double Measure(Action action)
    {
        action();
        var times = new double[5];

        for (var i = 0; i < times.Length; i++)
        {
            var start = Stopwatch.GetTimestamp();
            action();
            times[i] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        }

        return times.Order().ElementAt(2);
    }
}
