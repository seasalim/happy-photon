using Avalonia;
using HappyPhoton.Models;
using ImageMagick;

namespace HappyPhoton.Services;
public sealed partial class PreviewService
{
    private readonly object _adjacentWarmSync = new();
    private CancellationTokenSource? _adjacentWarmCancellation;
    private Task? _adjacentWarmTask;
    private AdjacentWarmEntry? _adjacentWarmEntry;
    private int _activeAdjacentWarmWorkers;
    internal bool AdjacentWarmEnabled { get; set; } = true;
    internal int AdjacentWarmEntryCount => ReadAdjacentWarmEntry() == null ? 0 : 1;
    internal bool TryStartAdjacentWarm(ImageFile imageFile) =>
        TryStartAdjacentWarm(imageFile, out _);
    internal bool TryStartAdjacentWarm(
        ImageFile imageFile,
        out Task? blockingWorker)
    {
        ArgumentNullException.ThrowIfNull(imageFile);
        blockingWorker = null;
        var settings = imageFile.EditSettings.Clone();
        var settingsHash = RenderSettingsHash.Compute(settings);
        lock (_adjacentWarmSync)
        {
            if (_disposed != 0 || !AdjacentWarmEnabled) return false;
            if (_adjacentWarmTask?.IsCompleted == false)
            {
                _adjacentWarmCancellation!.Cancel();
                blockingWorker = _adjacentWarmTask;
                return false;
            }
            if (imageFile.CatalogId == 0 || !CanReadAdjacentSource(imageFile) ||
                _previewCache.HasSettingsMatchedEntry(imageFile, settingsHash) ||
                _adjacentWarmEntry?.Matches(imageFile, settingsHash) == true)
                return false;
            _adjacentWarmCancellation?.Dispose();
            var cancellation = new CancellationTokenSource();
            _adjacentWarmCancellation = cancellation;
            Interlocked.Increment(ref _activeAdjacentWarmWorkers);
            _adjacentWarmTask = Task.Factory.StartNew(
                () => WarmAdjacentAsync(imageFile, settings, settingsHash,
                    cancellation), CancellationToken.None,
                TaskCreationOptions.LongRunning, TaskScheduler.Default).Unwrap();
            _ = _adjacentWarmTask.ContinueWith(_ =>
                Interlocked.Decrement(ref _activeAdjacentWarmWorkers),
                TaskScheduler.Default);
        }
        AdjacentWarmWorkStarted?.Invoke();
        return true;
    }
    internal void InvalidateAdjacentWarm(ImageFile? imageFile = null,
        bool dropRetained = false)
    {
        lock (_adjacentWarmSync)
        {
            _adjacentWarmCancellation?.Cancel();
            if (dropRetained && (imageFile == null ||
                _adjacentWarmEntry?.MatchesIdentity(imageFile) == true))
                _adjacentWarmEntry = null;
        }
    }
    private async Task WarmAdjacentAsync(ImageFile imageFile,
        EditSettings settings, string readerHash,
        CancellationTokenSource cancellation)
    {
        var token = cancellation.Token;
        try
        {
            var sourceWriteTime = File.GetLastWriteTimeUtc(imageFile.FilePath);
            var decode = await ResolveDecodeAsync(imageFile, settings, token)
                .ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            var outcome = _baseLoader.LoadPreviewBaseWithOutcome(imageFile,
                decode, token);
            using var pair = outcome.Pair;
            using var baseImage = pair?.DetachInteractive();
            if (baseImage == null) return;
            var writerHash = RenderSettingsHash.Compute(settings,
                baseImage.Info.ProfileToken);
            if (!string.Equals(readerHash, writerHash, StringComparison.Ordinal))
                return;
            using var rendered = _renderPipeline.Render(new RenderRequest(
                baseImage, settings, RenderIntent.Preview,
                BaseImage.InteractivePreviewMaxDimension,
                new RenderOptions(false, false)));
            token.ThrowIfCancellationRequested();
            if (!SourceMatches(imageFile, sourceWriteTime))
                return;
            rendered.Image.Quality = 90;
            var identity = new PreviewCacheIdentity(
                RenderGeometry.CalculateOriginalViewSize(
                    baseImage.Info.FullWidth,
                    baseImage.Info.FullHeight,
                    settings),
                new PixelSize(
                    baseImage.Info.FullWidth,
                    baseImage.Info.FullHeight));
            var entry = new AdjacentWarmEntry(imageFile.CatalogId,
                Path.GetFullPath(imageFile.FilePath), sourceWriteTime, writerHash,
                identity, rendered.Image.ToByteArray(MagickFormat.Jpeg));
            lock (_adjacentWarmSync)
            {
                if (token.IsCancellationRequested ||
                    !ReferenceEquals(_adjacentWarmCancellation, cancellation) ||
                    _disposed != 0)
                    return;
                _adjacentWarmEntry = entry;
            }
            _previewCache.QueueSaveToCache(
                imageFile, rendered.Image, writerHash, identity);
            _ = DropWhenPersistedAsync(imageFile, entry);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception exception)
        {
            ImageServiceHelpers.LogDebug(nameof(PreviewService),
                $"Adjacent warm failed: {exception.Message}", imageFile.FilePath);
        }
    }
    private CachedPreview? TryLoadAdjacentWarm(ImageFile imageFile,
        string settingsHash)
    {
        var entry = ReadAdjacentWarmEntry();
        if (entry == null || !entry.Matches(imageFile, settingsHash)) return null;
        if (!CanReadAdjacentSource(imageFile) ||
            !SourceMatches(imageFile, entry.SourceWriteTime))
        {
            DropAdjacentWarmEntry(entry);
            return null;
        }
        try
        {
            return new CachedPreview(new MagickImage(entry.EncodedJpeg),
                entry.SettingsHash, entry.Identity.OriginalViewSize,
                entry.Identity.OriginalImageSize);
        }
        catch
        {
            DropAdjacentWarmEntry(entry);
            return null;
        }
    }
    private async Task DropWhenPersistedAsync(ImageFile imageFile,
        AdjacentWarmEntry entry)
    {
        while (ReferenceEquals(ReadAdjacentWarmEntry(), entry) &&
               Volatile.Read(ref _disposed) == 0)
        {
            if (!SourceMatches(imageFile, entry.SourceWriteTime) ||
                (_previewCache.PendingWrites == 0 &&
                 _previewCache.HasSettingsMatchedEntry(imageFile,
                     entry.SettingsHash, entry.SourceWriteTime)))
            {
                DropAdjacentWarmEntry(entry);
                return;
            }
            await Task.Delay(250).ConfigureAwait(false);
        }
    }
    private bool CanReadAdjacentSource(ImageFile imageFile) =>
        SourceAccessPolicy.CanRead(_sourceAvailability.GetAvailability(
                imageFile.FilePath),
            SourceReadIntent.Background);
    private static bool SourceMatches(ImageFile image, DateTime writeTime)
    {
        try { return File.GetLastWriteTimeUtc(image.FilePath) == writeTime; }
        catch { return false; }
    }
    private AdjacentWarmEntry? ReadAdjacentWarmEntry()
    {
        lock (_adjacentWarmSync) return _adjacentWarmEntry;
    }
    private void DropAdjacentWarmEntry(AdjacentWarmEntry entry)
    {
        lock (_adjacentWarmSync)
        {
            if (ReferenceEquals(_adjacentWarmEntry, entry)) _adjacentWarmEntry = null;
        }
    }
    private async Task DisposeAdjacentWarmAsync()
    {
        InvalidateAdjacentWarm(dropRetained: true);
        var task = _adjacentWarmTask;
        if (task != null) await task.ConfigureAwait(
            ConfigureAwaitOptions.SuppressThrowing);
        _adjacentWarmCancellation?.Dispose();
        _adjacentWarmCancellation = null;
        _adjacentWarmTask = null;
    }
    private sealed record AdjacentWarmEntry(long CatalogId, string SourcePath,
        DateTime SourceWriteTime, string SettingsHash,
        PreviewCacheIdentity Identity, byte[] EncodedJpeg)
    {
        public bool MatchesIdentity(ImageFile imageFile) =>
            CatalogId == imageFile.CatalogId &&
            PathsEqual(SourcePath, imageFile.FilePath);
        public bool Matches(ImageFile imageFile, string settingsHash) =>
            MatchesIdentity(imageFile) && SettingsHash == settingsHash;
    }
}
