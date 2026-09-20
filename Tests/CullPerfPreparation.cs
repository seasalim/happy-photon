using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;

namespace HappyPhoton.Tests;

internal static class CullPerfPreparation
{
    internal static double DueMilliseconds(CullPerfWorkload workload, int index) =>
        index / workload.BurstSize * workload.IntervalMs + index % workload.BurstSize * workload.BurstStepMs;

    internal static async Task PrepareAsync(CatalogService catalog, MainWindowViewModel vm,
        ImageFile[] images, CullPerfWorkload workload)
    {
        if (workload.CacheCondition == "cold") return;
        using var preview = await vm.ImageService.Previews.LoadComparePreviewAsync(images[0], images[0].EditSettings);
        if (preview == null) throw new InvalidOperationException("Fixture did not render.");
        using var bitmap = preview.DetachBitmap();
        await TestWaits.UntilAsync(() => vm.ImageService.Previews.PendingCacheWrites == 0);
        if (workload.CacheCondition == "thumbnail")
        {
            // All copies share content; each thumbnail still has its own lifetime.
            using var pixels = new ImageMagick.MagickImage(CullPerfFiles.Fixtures().Root + "/image-0000.jpg");
            pixels.Resize(160, 0);
            foreach (var image in images) image.Thumbnail = BitmapConversionService.ConvertToBitmap(pixels);
            // Preparation must not leave a rendered preview behind.
            var cachePath = catalog.GetPreviewPath(images[0].CatalogId);
            File.Delete(cachePath);
            File.Delete(Path.ChangeExtension(cachePath, ".meta"));
            return;
        }
        // Every member of this fixture group is a hard link to identical bytes.
        // Copy the real rendered entry once; cache metadata contains settings and dimensions.
        var seed = catalog.GetPreviewPath(images[0].CatalogId);
        var hash = RenderSettingsHash.Compute(images[0].EditSettings);
        await using var cache = new PreviewCacheService(catalog);
        foreach (var image in images.Skip(1))
        {
            var target = catalog.GetPreviewPath(image.CatalogId);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(seed, target);
            File.Copy(Path.ChangeExtension(seed, ".meta"), Path.ChangeExtension(target, ".meta"));
            if (!cache.HasSettingsMatchedEntry(image, hash)) throw new IOException("Cache preparation did not persist.");
        }
        if (workload.CacheCondition == "stale")
            foreach (var image in images) image.EditSettings.Exposure += workload.StaleExposureDelta;
    }
}
