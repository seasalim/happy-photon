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
        bool computeWaveform,
        ClippingOverlaySide overlaySides,
        BaseDecodeSettings decode,
        long generation,
        long surfaceGeneration)
    {
        var startWorker = false;
        lock (_refreshSync)
        {
            if (Volatile.Read(ref _disposed) != 0)
            {
                return;
            }

            startWorker = !_pendingRefreshes.ContainsKey(refreshTask);
            var pending = new PendingRefresh(
                imageFile,
                settings.Clone(),
                thumbnailRequest,
                computeWaveform,
                overlaySides,
                decode,
                generation,
                surfaceGeneration);
            if (_pendingRefreshes.TryGetValue(refreshTask, out var existing))
            {
                pending = pending with
                {
                    ComputeWaveform = existing.ComputeWaveform || computeWaveform,
                    OverlaySides = existing.OverlaySides | overlaySides
                };
            }
            _pendingRefreshes[refreshTask] = pending;
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
                        pending.SurfaceGeneration,
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
                hasHistogram: true,
                pending.SurfaceGeneration,
                refreshed.RawHistogram,
                refreshed.Output.Clipping,
                refreshed.Output.IsRawSource,
                refreshed.Output.ProfileState,
                refreshed.Output.DetachClippingMask(),
                refreshed.Info.AsShotKelvin,
                refreshed.Info.AsShotTint,
                refreshed.Output.IsMonochrome);
            var promotionLease = CreatePromotionLease(
                pending.ImageFile,
                refreshed.Output,
                refreshed.SettingsHash,
                refreshGeneration,
                surfaceAuthorized: true);
            refresh.SetPromotionLease(promotionLease);
            refreshed.Output.Dispose();
            PreviewRefreshed?.Invoke(this, refresh);
            ReportPreviewOutcome(
                pending.ImageFile,
                pending.SurfaceGeneration,
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
        using var snapshot = _baseCoordinator.TryAcquireCurrent(
            pending.ImageFile,
            pending.Decode);
        if (snapshot == null)
        {
            return null;
        }
        var rawHistogram = snapshot.Analysis.RawHistogram;

        RenderStarted?.Invoke();
        var rendered = await Task.Run(
            () => Render(
                snapshot.Base,
                snapshot.Analysis.SourceSaturation,
                pending.Settings,
                pending.ThumbnailRequest,
                skipHistogram: false,
                pending.ComputeWaveform,
                pending.OverlaySides,
                generation,
                CancellationToken.None,
                surfaceAuthorized: true),
            CancellationToken.None).ConfigureAwait(false);
        var settingsHash = RenderSettingsHash.Compute(
            pending.Settings,
            snapshot.Base.Info.ProfileToken);
        if (generation != Volatile.Read(ref _renderGeneration) ||
            rendered.Bitmap == null)
        {
            rendered.Dispose();
            return null;
        }

        TagPreview(
            rendered.Bitmap,
            pending.ImageFile,
            generation,
            pending.Decode.CacheKey,
            settingsHash,
            snapshot.Base);

        return new RefreshedRender(
            rendered,
            rawHistogram,
            settingsHash,
            snapshot.Base.Info);
    }

    private sealed record RefreshedRender(
        RenderOutput Output,
        HistogramData? RawHistogram,
        string SettingsHash,
        BaseImageInfo Info);
}
