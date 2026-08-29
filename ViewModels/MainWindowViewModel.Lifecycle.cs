namespace HappyPhoton.ViewModels;

public partial class MainWindowViewModel
{
    public async ValueTask DisposeAsync()
    {
        CloseRenderOutcomeChannel();
        await CancelAndDrainLoupeAsync();
        await CancelAndDrainCompareAsync();
        CloseBeforeAfterSplit();
        await CancelAndDrainExportJobAsync();
        DisposeBackgroundActivity();
        await DisposeUpdatesAsync();
        Interlocked.Increment(ref _browseGeneration);
        await CancelXmpReconcileAsync();
        if (_xmpWriter != null)
        {
            await _xmpWriter.DisposeAsync();
            _xmpWriter = null;
        }
        CancelSourceHydration();
        await CancelBrowseSelectionSummaryAsync();
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
        try
        {
            if (Interlocked.Exchange(ref _proofTask, null) is { } proofTask)
                await proofTask;
        }
        catch (OperationCanceledException) { }

        await WaitForThumbnailSessionsAsync();
        await WaitForSelectionMetadataLoadsAsync();
        await Task.WhenAny(
            WaitForBurstAnalysisAsync(),
            Task.Delay(TimeSpan.FromSeconds(2)));

        CancelAndDispose(ref _previewDebounce);
        CancelAdjacentPreviewWarm(true, dropRetained: true);
        if (Interlocked.Exchange(ref _previewDebounceTask, null) is
            { } pendingPreviewUpdate)
        {
            // Cancellation cannot stop an action already past its token
            // checks; its autosave must finish before the catalog goes away.
            PreviewDebounceDrainStarted?.Invoke();
            await pendingPreviewUpdate;
            PreviewDebounceDrainCompleted?.Invoke();
        }
        CancelAndDispose(ref _histogramDebounce);
        CancelAndDispose(ref _thumbnailDebounce);
        CancelRestingPreview(clearParent: true);
        CancelAndDispose(ref _transientStatusCts);
        CancelAndDispose(ref _assessmentFeedbackCts);
        CancelAndDispose(ref _alignmentGridCts);
        CancelAndDispose(ref _clippingOverlayCts);
        await DrainBackgroundActivityAsync();

        if (_imageService.IsValueCreated)
        {
            DependentExportServicesDisposing?.Invoke();
            var previews = _imageService.Value.Previews;
            previews.PreviewRefreshed -= OnPreviewRefreshed;
            previews.PreviewLoadCompleted -= OnPreviewLoadCompleted;
            previews.BaseRefreshStateChanged -= OnBaseRefreshStateChanged;
            previews.RenderedThumbnailWorkStarted -=
                OnRenderedThumbnailWorkStarted;
            previews.AdjacentWarmWorkStarted -=
                OnAdjacentWarmWorkStarted;
            await _imageService.Value.DisposeAsync();
        }

        ClearPreviewImage();
        ClearPreviewClippingArtifacts();
        Browse.DisposeThumbnails();
        _bitmapRetirement.Dispose();
    }

    private static void CancelAndDispose(ref CancellationTokenSource? source)
    {
        var cancellation = Interlocked.Exchange(ref source, null);
        cancellation?.Cancel();
        cancellation?.Dispose();
    }
}
