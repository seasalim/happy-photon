using Avalonia.Headless.XUnit;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class SpotVisualizationGateTests(ITestOutputHelper output)
{
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
        var metadataPath = Path.ChangeExtension(cachePath, ".meta");
        Assert.True(PreviewCacheMetadata.TryRead(metadataPath, out var metaBefore));

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
        Assert.True(PreviewCacheMetadata.TryRead(metadataPath, out var metaAfter));
        // Every write gets a fresh ID even when its rendered content is identical.
        Assert.NotEqual(metaBefore.Generation, metaAfter.Generation);
        Assert.Equal(metaBefore with { Generation = metaAfter.Generation }, metaAfter);
        output.WriteLine($"HEAL-WP6 G2 S64 RAW export_bytes={on.Length} differing_bytes=0 preview_cache_differing_bytes=0");
    }

    private static ImageFile LocalRaw()
    {
        var path = GoldenTestPaths.Asset("canon-eos-6d-iso-6400.cr2");
        GoldenTestPaths.RequireReadableFixture(path);

        return new(path);
    }
}
