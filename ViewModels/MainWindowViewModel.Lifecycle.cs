namespace HappyPhoton.ViewModels;

public partial class MainWindowViewModel
{
    public async ValueTask DisposeAsync()
    {
        Interlocked.Increment(ref _libraryGeneration);
        var thumbnailLoadingCts = Interlocked.Exchange(
            ref _thumbnailLoadingCts, null);
        if (thumbnailLoadingCts != null)
        {
            await CancelAsync(thumbnailLoadingCts);
        }

        var previewLoadingCts = Interlocked.Exchange(
            ref _previewLoadingCts, null);
        if (previewLoadingCts != null)
        {
            // LoadPreviewAsync owns disposal after its in-flight work exits.
            await CancelAsync(previewLoadingCts);
        }

        await WaitForThumbnailSessionsAsync();

        CancelAndDispose(ref _previewDebounce);
        CancelAndDispose(ref _histogramDebounce);
        CancelAndDispose(ref _thumbnailDebounce);
        CancelAndDispose(ref _transientStatusCts);

        if (_imageService.IsValueCreated)
        {
            await _imageService.Value.DisposeAsync();
        }
        Library.DisposeThumbnails();
    }

    private static void CancelAndDispose(ref CancellationTokenSource? source)
    {
        var cancellation = Interlocked.Exchange(ref source, null);
        cancellation?.Cancel();
        cancellation?.Dispose();
    }
}
