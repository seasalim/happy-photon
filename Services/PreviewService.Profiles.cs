using System.Diagnostics;
using HappyPhoton.Models;
using static HappyPhoton.Services.ImageServiceHelpers;

namespace HappyPhoton.Services;

public sealed partial class PreviewService
{
    private async Task<PreviewArtifacts> RenderAsync(
        ImageFile imageFile,
        EditSettings settings,
        ThumbnailSizeRequest thumbnailRequest,
        bool skipHistogram,
        bool computeWaveform,
        ClippingOverlaySide overlaySides,
        bool forceProfileRefresh,
        CancellationToken cancellationToken,
        long? surfaceGeneration)
    {
        var settingsSnapshot = settings.Clone();
        var outcomeGeneration = surfaceGeneration ?? 0;
        if (surfaceGeneration.HasValue &&
            !TryAdoptSurfaceGeneration(surfaceGeneration.Value))
        {
            return PreviewArtifacts.Empty(
                surfaceGeneration.Value,
                imageFile.IsRaw);
        }
        await imageFile.EnsureCatalogIdAsync(_catalogService);
        if (surfaceGeneration.HasValue &&
            surfaceGeneration.Value != Volatile.Read(
                ref _latestSurfaceGeneration))
        {
            return PreviewArtifacts.Empty(
                surfaceGeneration.Value,
                imageFile.IsRaw);
        }
        ObjectDisposedException.ThrowIf(
            Volatile.Read(ref _disposed) != 0,
            this);
        var generation = Interlocked.Increment(ref _renderGeneration);
        if (!surfaceGeneration.HasValue)
        {
            outcomeGeneration = generation;
        }
        var stopwatch = Stopwatch.StartNew();

        try
        {
            var decode = BaseDecodeSettings.From(settingsSnapshot);
            if (settingsSnapshot.RawProfile != null)
            {
                var resolution = await _dcpProfiles.ResolveAsync(
                    imageFile,
                    settingsSnapshot.RawProfile,
                    forceRefresh: forceProfileRefresh,
                    cancellationToken).ConfigureAwait(false);
                decode = decode.WithProfileResolution(resolution);
            }
            if (!IsCurrentSurfaceGeneration(surfaceGeneration))
            {
                return PreviewArtifacts.Empty(
                    outcomeGeneration,
                    imageFile.IsRaw);
            }
            using var snapshot = await AcquirePreviewBaseAsync(
                imageFile,
                decode,
                generation,
                outcomeGeneration,
                surfaceGeneration,
                cancellationToken);
            if (snapshot == null)
            {
                return PreviewArtifacts.Empty(outcomeGeneration, imageFile.IsRaw);
            }
            if (!IsCurrentRenderRequest(surfaceGeneration, generation))
            {
                return PreviewArtifacts.Empty(
                    outcomeGeneration,
                    snapshot.Base.Info.IsRawSource);
            }
            if (snapshot.IsStale)
            {
                QueueRefresh(
                    snapshot.RefreshTask!,
                    imageFile,
                    settingsSnapshot,
                    thumbnailRequest,
                    computeWaveform,
                    overlaySides,
                    decode,
                    generation,
                    outcomeGeneration);
            }

            LogPerformance(
                nameof(RenderAsync),
                "Base",
                stopwatch.ElapsedMilliseconds,
                imageFile.FilePath,
                $"size={snapshot.Base.Pixels.Width}x{snapshot.Base.Pixels.Height}");
            stopwatch.Restart();

            if (RenderGateAsync is { } gate)
            {
                await gate().ConfigureAwait(false);
            }
            if (!IsCurrentSurfaceGeneration(surfaceGeneration) ||
                !IsCurrentRenderRequest(surfaceGeneration, generation))
            {
                return PreviewArtifacts.Empty(
                    outcomeGeneration,
                    snapshot.Base.Info.IsRawSource);
            }
            RenderStarted?.Invoke();
            var rendered = await Task.Run(
                () => Render(
                    snapshot.Base,
                    settingsSnapshot,
                    thumbnailRequest,
                    skipHistogram,
                    computeWaveform,
                    overlaySides,
                    generation,
                    cancellationToken,
                    surfaceAuthorized: surfaceGeneration.HasValue),
                cancellationToken);
            if (!IsCurrentRenderRequest(surfaceGeneration, generation) ||
                cancellationToken.IsCancellationRequested)
            {
                rendered.Dispose();
                cancellationToken.ThrowIfCancellationRequested();
                return PreviewArtifacts.Empty(
                    outcomeGeneration,
                    snapshot.Base.Info.IsRawSource);
            }

            var settingsHash = RenderSettingsHash.Compute(
                settingsSnapshot,
                snapshot.Base.Info.ProfileToken);
            if (rendered.Bitmap == null)
            {
                rendered.Dispose();
                return PreviewArtifacts.Empty(
                    outcomeGeneration,
                    snapshot.Base.Info.IsRawSource);
            }
            LogPerformance(
                nameof(RenderAsync),
                $"RenderV{RenderPipeline.Version}",
                stopwatch.ElapsedMilliseconds,
                imageFile.FilePath);
            TagPreview(
                rendered.Bitmap,
                imageFile,
                generation,
                decode.CacheKey,
                settingsHash, snapshot.Base);
            var promotionLease = CreatePromotionLease(
                imageFile,
                rendered,
                settingsHash,
                generation,
                surfaceAuthorized: surfaceGeneration.HasValue);
            ReportPreviewSuccess(imageFile, outcomeGeneration);
            return rendered.DetachArtifacts(
                outcomeGeneration,
                snapshot.Base.Info,
                snapshot.IsStale,
                promotionLease);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            HandleImageLoadError(ex, imageFile.FilePath);
            ReportPreviewFailure(imageFile, outcomeGeneration);
            return PreviewArtifacts.Empty(outcomeGeneration, imageFile.IsRaw);
        }
        finally
        {
            RenderRequestCompleted?.Invoke(outcomeGeneration);
        }
    }

    private bool TryAdoptSurfaceGeneration(long generation)
    {
        while (true)
        {
            var current = Volatile.Read(ref _latestSurfaceGeneration);
            if (generation < current)
            {
                return false;
            }
            if (generation == current ||
                Interlocked.CompareExchange(
                    ref _latestSurfaceGeneration,
                    generation,
                    current) == current)
            {
                return true;
            }
        }
    }

    private bool IsCurrentSurfaceGeneration(long? generation) =>
        !generation.HasValue ||
        generation.Value == Volatile.Read(ref _latestSurfaceGeneration);

    private bool IsCurrentRenderRequest(
        long? surfaceGeneration,
        long renderGeneration) =>
        surfaceGeneration.HasValue
            ? IsCurrentSurfaceGeneration(surfaceGeneration)
            : renderGeneration == Volatile.Read(ref _renderGeneration);
}
