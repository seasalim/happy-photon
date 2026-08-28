using HappyPhoton.Models;
using HappyPhoton.Services;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

[Collection(AvaloniaTestCollection.Name)]
public sealed class PreviewDisposalRaceTests
{
    [WindowsFact]
    public async Task DisposeAsync_WaitsForAcceptedCachedPreviewRead()
    {
        using var root = new TemporaryDirectory();
        var sourcePath = Path.Combine(root.Path, "source.jpg");
        TestImages.WriteJpeg(sourcePath, width: 64, height: 48);
        File.SetLastWriteTimeUtc(sourcePath, DateTime.UtcNow.AddMinutes(-2));

        using var catalog = new CatalogService(Path.Combine(root.Path, "catalog"));
        await catalog.InitializeAsync();
        var image = new ImageFile(sourcePath);
        await image.EnsureCatalogIdAsync(catalog);

        await using (var writer = new PreviewCacheService(catalog))
        {
            using var cached = new MagickImage(MagickColors.Red, 64, 48);
            writer.QueueSaveToCache(
                image,
                cached,
                RenderSettingsHash.Compute(image.EditSettings));
        }

        var previewCache = new PreviewCacheService(catalog);
        var renderedThumbnailCache = new RenderedThumbnailCacheService(catalog);
        await previewCache.DisposeAsync();
        await renderedThumbnailCache.DisposeAsync();
        await using var service = new PreviewService(
            catalog,
            new NullBaseLoader(),
            new RenderPipeline(),
            previewCache,
            renderedThumbnailCache,
            createRenderedThumbnail: false);
        var readAccepted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseRead = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var disposalWaitStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        service.CachedPreviewGateAsync = () =>
        {
            readAccepted.TrySetResult();
            return releaseRead.Task;
        };
        service.DisposalTaskWaitStarted = () =>
            disposalWaitStarted.TrySetResult();

        var readTask = service.LoadCachedPreviewAsync(
            image,
            image.EditSettings);
        await readAccepted.Task.WaitAsync(TestWaits.Condition);

        var disposeTask = service.DisposeAsync().AsTask();
        await disposalWaitStarted.Task.WaitAsync(TestWaits.Condition);

        using var declined = await service.LoadCachedPreviewAsync(
            image,
            image.EditSettings).WaitAsync(TestWaits.Condition);

        Assert.Null(declined);
        Assert.False(readTask.IsCompleted);
        Assert.False(disposeTask.IsCompleted);

        releaseRead.TrySetResult();

        await disposeTask.WaitAsync(TestWaits.Condition);
        using var result = await readTask.WaitAsync(TestWaits.Condition);

        Assert.NotNull(result);
    }
}
