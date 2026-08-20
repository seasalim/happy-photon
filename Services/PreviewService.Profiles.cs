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
        ClippingOverlaySide overlaySides,
        bool forceProfileRefresh,
        CancellationToken cancellationToken)
    {
        var settingsSnapshot = settings.Clone();
        await imageFile.EnsureCatalogIdAsync(_catalogService);
        ObjectDisposedException.ThrowIf(
            Volatile.Read(ref _disposed) != 0,
            this);
        var generation = Interlocked.Increment(ref _renderGeneration);
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
            using var snapshot = await AcquirePreviewBaseAsync(
                imageFile,
                decode,
                generation,
                cancellationToken);
            if (snapshot == null)
            {
                return PreviewArtifacts.Empty(generation, imageFile.IsRaw);
            }
            if (generation != Volatile.Read(ref _renderGeneration))
            {
                return PreviewArtifacts.Empty(
                    generation,
                    snapshot.Base.Info.IsRawSource);
            }
            if (snapshot.IsStale)
            {
                QueueRefresh(
                    snapshot.RefreshTask!,
                    imageFile,
                    settingsSnapshot,
                    thumbnailRequest,
                    skipHistogram,
                    overlaySides,
                    decode,
                    generation);
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
            RenderStarted?.Invoke();
            var rendered = await Task.Run(
                () => Render(
                    snapshot.Base,
                    settingsSnapshot,
                    thumbnailRequest,
                    skipHistogram,
                    overlaySides,
                    generation,
                    cancellationToken),
                cancellationToken);
            if (generation != Volatile.Read(ref _renderGeneration) ||
                cancellationToken.IsCancellationRequested)
            {
                rendered.Dispose();
                cancellationToken.ThrowIfCancellationRequested();
                return PreviewArtifacts.Empty(
                    generation,
                    snapshot.Base.Info.IsRawSource);
            }

            var settingsHash = RenderSettingsHash.Compute(
                settingsSnapshot,
                snapshot.Base.Info.ProfileToken);
            if (rendered.Bitmap == null ||
                !TryRememberRendered(
                    imageFile,
                    rendered,
                    settingsHash,
                    generation))
            {
                rendered.Dispose();
                return PreviewArtifacts.Empty(
                    generation,
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
            ReportPreviewSuccess(imageFile, generation);
            return rendered.DetachArtifacts(generation);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            HandleImageLoadError(ex, imageFile.FilePath);
            ReportPreviewFailure(imageFile, generation);
            return PreviewArtifacts.Empty(generation, imageFile.IsRaw);
        }
    }
}
