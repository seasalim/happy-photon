namespace HappyPhoton.ViewModels;

public partial class MainWindowViewModel
{
    public async ValueTask DisposeAsync()
    {
        CloseRenderOutcomeChannel();
        DisposeBackgroundActivity();
        await DisposeUpdatesAsync();
        Interlocked.Increment(ref _libraryGeneration);
        await CancelXmpReconcileAsync();
        if (_xmpWriter != null)
        {
            await _xmpWriter.DisposeAsync();
            _xmpWriter = null;
        }
        CancelSourceHydration();
        await CancelLibrarySelectionSummaryAsync();
        _burstAnalysisRestartRequested = false;
        CancelBurstAnalysis();
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
        await WaitForSelectionMetadataLoadsAsync();
        await Task.WhenAny(
            WaitForBurstAnalysisAsync(),
            Task.Delay(TimeSpan.FromSeconds(2)));

        CancelAndDispose(ref _previewDebounce);
        CancelAndDispose(ref _histogramDebounce);
        CancelAndDispose(ref _thumbnailDebounce);
        CancelRestingPreview(clearParent: true);
        CancelAndDispose(ref _transientStatusCts);
        CancelAndDispose(ref _assessmentFeedbackCts);
        CancelAndDispose(ref _clippingOverlayCts);
        await DrainBackgroundActivityAsync();

        if (_imageService.IsValueCreated)
        {
            var previews = _imageService.Value.Previews;
            previews.PreviewRefreshed -= OnPreviewRefreshed;
            previews.PreviewLoadCompleted -= OnPreviewLoadCompleted;
            previews.BaseRefreshStateChanged -= OnBaseRefreshStateChanged;
            previews.RenderedThumbnailWorkStarted -=
                OnRenderedThumbnailWorkStarted;
            await _imageService.Value.DisposeAsync();
        }

        ClearPreviewImage();
        ClearPreviewClippingArtifacts();
        Library.DisposeThumbnails();
        _bitmapRetirement.Dispose();
    }

    private static void CancelAndDispose(ref CancellationTokenSource? source)
    {
        var cancellation = Interlocked.Exchange(ref source, null);
        cancellation?.Cancel();
        cancellation?.Dispose();
    }
}
