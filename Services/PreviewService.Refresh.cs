using HappyPhoton.Models;
using static HappyPhoton.Services.ImageServiceHelpers;

namespace HappyPhoton.Services;

public sealed partial class PreviewService
{
    private void QueueRefresh(
        Task<BaseImageLoadFailure> refreshTask,
        ImageFile imageFile,
        EditSettings settings,
        ThumbnailSizeRequest thumbnailRequest,
        bool skipHistogram,
        ClippingOverlaySide overlaySides,
        long generation)
    {
        var startWorker = false;
        lock (_refreshSync)
        {
            if (Volatile.Read(ref _disposed) != 0)
            {
                return;
            }

            startWorker = !_pendingRefreshes.ContainsKey(refreshTask);
            _pendingRefreshes[refreshTask] = new PendingRefresh(
                imageFile,
                settings.Clone(),
                thumbnailRequest,
                skipHistogram,
                overlaySides,
                generation);
        }

        if (startWorker)
        {
            var requestId = Interlocked.Increment(
                ref _baseRefreshGeneration);
            BaseRefreshStateChanged?.Invoke(
                this,
                new PreviewBaseRefreshState(
                    imageFile,
                    requestId,
                    isRefreshing: true));
            _ = RefreshWhenBaseReadyAsync(
                refreshTask,
                imageFile,
                requestId);
        }
    }

    private async Task RefreshWhenBaseReadyAsync(
        Task<BaseImageLoadFailure> refreshTask,
        ImageFile imageFile,
        long requestId)
    {
        try
        {
            var failure = await refreshTask.ConfigureAwait(false);
            await Task.Yield();

            PendingRefresh? pending;
            lock (_refreshSync)
            {
                _pendingRefreshes.Remove(refreshTask, out pending);
            }
            if (pending == null ||
                Volatile.Read(ref _disposed) != 0)
            {
                return;
            }

            if (failure != BaseImageLoadFailure.None)
            {
                if (pending.Generation == Volatile.Read(ref _renderGeneration))
                {
                    ReportPreviewOutcome(
                        pending.ImageFile,
                        pending.Generation,
                        failure);
                }
                return;
            }

            var refreshGeneration = pending.Generation + 1;
            if (Interlocked.CompareExchange(
                    ref _renderGeneration,
                    refreshGeneration,
                    pending.Generation) != pending.Generation)
            {
                return;
            }

            Interlocked.Increment(ref _activeRefreshRenders);
            RefreshedRender? refreshed;
            try
            {
                refreshed = await RenderRefreshedAsync(
                        pending,
                        refreshGeneration)
                    .ConfigureAwait(false);
            }
            finally
            {
                Interlocked.Decrement(ref _activeRefreshRenders);
            }
            if (refreshed?.Output.Bitmap == null)
            {
                return;
            }
            if (RefreshReadyGateAsync is { } gate)
            {
                await gate().ConfigureAwait(false);
            }

            using var refresh = new PreviewRefresh(
                pending.ImageFile,
                refreshed.Output.DetachBitmap()!,
                refreshed.Output.Histogram,
                !pending.SkipHistogram,
                refreshGeneration,
                refreshed.RawHistogram,
                refreshed.Output.Clipping,
                refreshed.Output.IsRawSource,
                refreshed.Output.DetachClippingMask());
            refreshed.Output.Dispose();
            PreviewRefreshed?.Invoke(this, refresh);
            ReportPreviewOutcome(
                pending.ImageFile,
                refreshGeneration,
                BaseImageLoadFailure.None);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            LogDebug(
                nameof(PreviewService),
                $"Base refresh failed: {ex.Message}");
        }
        finally
        {
            lock (_refreshSync)
            {
                _pendingRefreshes.Remove(refreshTask);
            }
            BaseRefreshStateChanged?.Invoke(
                this,
                new PreviewBaseRefreshState(
                    imageFile,
                    requestId,
                    isRefreshing: false));
        }
    }

    private async Task<RefreshedRender?> RenderRefreshedAsync(
        PendingRefresh pending,
        long generation)
    {
        if (RefreshRenderGateAsync is { } gate)
        {
            await gate().ConfigureAwait(false);
        }
        var decode = BaseDecodeSettings.From(pending.Settings);
        using var snapshot = _baseCoordinator.TryAcquireCurrent(
            pending.ImageFile,
            decode);
        if (snapshot == null)
        {
            return null;
        }
        var rawHistogram = snapshot.Base.Info.RawHistogram;

        RenderStarted?.Invoke();
        var rendered = await Task.Run(
            () => Render(
                snapshot.Base,
                pending.Settings,
                pending.ThumbnailRequest,
                pending.SkipHistogram,
                pending.OverlaySides,
                generation,
                CancellationToken.None),
            CancellationToken.None).ConfigureAwait(false);
        if (generation != Volatile.Read(ref _renderGeneration) ||
            rendered.Bitmap == null ||
            !TryRememberRendered(
                pending.ImageFile,
                rendered,
                RenderSettingsHash.Compute(pending.Settings),
                generation))
        {
            rendered.Dispose();
            return null;
        }

        return new RefreshedRender(rendered, rawHistogram);
    }

    private sealed record RefreshedRender(
        RenderOutput Output,
        HistogramData? RawHistogram);
}
