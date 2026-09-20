using System.Text;
using System.Threading.Channels;
using Avalonia.Media.Imaging;
using ImageMagick;
using HappyPhoton.Models;
using static HappyPhoton.Services.BitmapConversionService;

namespace HappyPhoton.Services;

internal sealed class SettingsHashedCacheWriter : IAsyncDisposable
{
    private static readonly TimeSpan DefaultDrainTimeout = TimeSpan.FromSeconds(2);

    private readonly CatalogService _catalogService;
    private readonly Func<long, string> _getCachePath;
    private readonly int _jpegQuality;
    private readonly bool _versionedDimensionMetadata;
    private readonly string _temporaryDirectory;
    private readonly Channel<CacheWrite> _queue;
    private readonly Task _processingGate;
    private readonly Func<Task> _writerInHandGate;
    private readonly TimeSpan _drainTimeout;
    private readonly CancellationTokenSource _cancellation = new();
    private readonly Task _processingTask;
    private readonly HashSet<CacheWrite> _outcomes = [];
    private int _activeWrites;
    private int _outstandingWrites;
    private int _disposed;
    private int _droppedWrites;
    internal int DroppedWrites => Volatile.Read(ref _droppedWrites);

    // Counted from enqueue until the write lands, fails, or is dropped, so a
    // reader never sees zero while a write is merely between queue and hand.
    public int PendingWrites => Volatile.Read(ref _outstandingWrites);
    internal CullPerfRecorder? CullPerf { get; set; }
    internal int Tier { get; set; }
    internal int WriterInHandCount => Volatile.Read(ref _activeWrites);

    public SettingsHashedCacheWriter(
        CatalogService catalogService,
        Func<long, string> getCachePath,
        int jpegQuality,
        int queueCapacity = 8,
        Task? processingGate = null,
        TimeSpan? drainTimeout = null,
        bool versionedDimensionMetadata = false,
        Task? writerInHandGate = null,
        Func<Task>? beforeWrite = null)
    {
        _catalogService = catalogService;
        _getCachePath = getCachePath;
        _jpegQuality = jpegQuality;
        _versionedDimensionMetadata = versionedDimensionMetadata;
        _temporaryDirectory = catalogService.TemporaryAssetsPath;
        _processingGate = processingGate ?? Task.CompletedTask;
        _writerInHandGate = beforeWrite ?? (() => writerInHandGate ?? Task.CompletedTask);
        _drainTimeout = drainTimeout ?? DefaultDrainTimeout;
        _queue = Channel.CreateBounded<CacheWrite>(
            new BoundedChannelOptions(queueCapacity)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = false
            },
            dropped =>
            {
                Complete(dropped, persisted: false);
                (dropped.Image as IDisposable)?.Dispose();
                Interlocked.Decrement(ref _outstandingWrites);
            });
        _processingTask = Task.Run(ProcessAsync);
    }

    public Task<bool> Queue(
        ImageFile imageFile,
        MagickImage image,
        string settingsHash,
        PreviewCacheIdentity? identity = null)
    {
        if (!CanQueue(imageFile, settingsHash))
        {
            Drop(imageFile.CatalogId);
            return Task.FromResult(false);
        }
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        MagickImage? clone = null;
        try
        {
            clone = new MagickImage(image);
            if (TryQueueOwned(imageFile, clone, settingsHash, identity,
                    File.GetLastWriteTimeUtc(imageFile.FilePath), completion)) clone = null;
        }
        catch
        {
            Drop(imageFile.CatalogId);
            completion.TrySetResult(false);
        }
        finally
        {
            clone?.Dispose();
        }
        return completion.Task;
    }

    public void Queue(
        ImageFile imageFile,
        Bitmap bitmap,
        string settingsHash,
        PreviewCacheIdentity? identity = null,
        DateTime? sourceWriteTime = null,
        bool ownsBitmap = false)
    {
        var operation = CullPerf?.Record("CacheEnqueueStart", imageFile.CatalogId, operation: -1) ?? 0;
        object? snapshot = ownsBitmap ? bitmap : null;
        try
        {
            if (!CanQueue(imageFile, settingsHash) || sourceWriteTime == null)
            {
                Drop(imageFile.CatalogId);
                return;
            }
            snapshot ??= SnapshotBitmap(bitmap);
            if (TryQueueOwned(imageFile, snapshot, settingsHash, identity, sourceWriteTime.Value))
                snapshot = null;
        }
        catch { Drop(imageFile.CatalogId); }
        finally
        {
            (snapshot as IDisposable)?.Dispose();
            CullPerf?.Record("CacheEnqueueEnd", imageFile.CatalogId, operation: operation);
        }
    }

    private void Drop(long imageId)
    {
        Interlocked.Increment(ref _droppedWrites);
        CullPerf?.Record("CacheWriteDropped", imageId, value: Tier);
    }

    private void Complete(CacheWrite write, bool persisted)
    {
        lock (_outcomes)
        {
            if (!_outcomes.Remove(write)) return;
            if (persisted) CullPerf?.Record("CacheWriteComplete", write.ImageId, value: Tier);
            else Drop(write.ImageId);
            write.Completion.TrySetResult(persisted);
        }
    }

    private bool CanQueue(ImageFile imageFile, string settingsHash) =>
        Volatile.Read(ref _disposed) == 0 &&
        imageFile.CatalogId != 0 &&
        !string.IsNullOrWhiteSpace(settingsHash);

    private bool TryQueueOwned(
        ImageFile imageFile,
        object image,
        string settingsHash,
        PreviewCacheIdentity? identity,
        DateTime sourceWriteTime,
        TaskCompletionSource<bool>? completion = null)
    {
        var write = new CacheWrite(
            _getCachePath(imageFile.CatalogId), imageFile.CatalogId,
            imageFile.FilePath,
            sourceWriteTime,
            settingsHash,
            identity,
            image,
            completion ?? new(TaskCreationOptions.RunContinuationsAsynchronously));
        lock (_outcomes) _outcomes.Add(write);
        Interlocked.Increment(ref _outstandingWrites);
        if (_queue.Writer.TryWrite(write)) return true;
        Complete(write, persisted: false);
        Interlocked.Decrement(ref _outstandingWrites);
        return false;
    }

    private async Task ProcessAsync()
    {
        try
        {
            await _processingGate.ConfigureAwait(false);
            await foreach (var write in _queue.Reader
                .ReadAllAsync(_cancellation.Token).ConfigureAwait(false))
            {
                Interlocked.Increment(ref _activeWrites);
                try
                {
                    await _writerInHandGate().ConfigureAwait(false);
                    Save(write);
                }
                finally
                {
                    Interlocked.Decrement(ref _activeWrites);
                    Interlocked.Decrement(ref _outstandingWrites);
                }
            }
        }
        catch (OperationCanceledException) when (_cancellation.IsCancellationRequested)
        {
        }
        finally
        {
            while (_queue.Reader.TryRead(out var pending))
            {
                Complete(pending, persisted: false);
                (pending.Image as IDisposable)?.Dispose();
                Interlocked.Decrement(ref _outstandingWrites);
            }
        }
    }

    private void Save(CacheWrite write)
    {
        var stem = Path.Combine(_temporaryDirectory, Guid.NewGuid().ToString("N"));
        var temporaryPath = $"{stem}.jpg";
        var temporaryMetadataPath = $"{stem}.meta";
        var metadataPath = Path.ChangeExtension(write.CachePath, ".meta");
        MagickImage? converted = null;
        var persisted = false;
        try
        {
            if (write.Image is not MagickImage) CullPerf?.Record("CacheConvert", write.ImageId);
            var image = write.Image switch
            {
                Bitmap bitmap => converted = ConvertToMagickImage(bitmap),
                BitmapSnapshot snapshot => converted = snapshot.ToMagickImage(),
                _ => (MagickImage)write.Image
            };
            Directory.CreateDirectory(_temporaryDirectory);
            Directory.CreateDirectory(Path.GetDirectoryName(write.CachePath)!);
            image.Quality = (uint)_jpegQuality;
            image.Write(temporaryPath, MagickFormat.Jpeg);
            File.WriteAllText(
                temporaryMetadataPath,
                _versionedDimensionMetadata
                    ? RenderedThumbnailMetadata.Serialize(
                        write.SettingsHash,
                        (int)image.Width,
                        (int)image.Height)
                    // Always the versioned document. A write with no identity
                    // records zero dimensions, which reads back as "hash only" —
                    // one format on disk, no bare-hash variant to discriminate.
                    : PreviewCacheMetadata.Serialize(
                        write.SettingsHash,
                        write.Identity ?? default),
                new UTF8Encoding(false));
            if (write.Image is not MagickImage) CullPerf?.Record("CacheMetadataRead", write.ImageId);
            if (File.GetLastWriteTimeUtc(write.SourcePath) != write.SourceWriteTime ||
                (_versionedDimensionMetadata &&
                    HasEqualOrLargerMatchingEntry(write, metadataPath, temporaryPath)))
            {
                File.Delete(temporaryPath);
                File.Delete(temporaryMetadataPath);
                return;
            }

            File.Delete(metadataPath);
            File.Move(temporaryPath, write.CachePath, overwrite: true);
            File.Move(temporaryMetadataPath, metadataPath);
            persisted = true;
        }
        catch
        {
            TryDelete(temporaryPath);
            TryDelete(temporaryMetadataPath);
        }
        finally
        {
            converted?.Dispose();
            (write.Image as IDisposable)?.Dispose();
            Complete(write, persisted);
        }
    }

    private static bool HasEqualOrLargerMatchingEntry(
        CacheWrite write,
        string metadataPath,
        string candidatePath)
    {
        if (!File.Exists(write.CachePath) ||
            File.GetLastWriteTimeUtc(write.CachePath) <= write.SourceWriteTime ||
            !RenderedThumbnailMetadata.TryRead(
                metadataPath,
                write.CachePath,
                out var current) ||
            !string.Equals(
                current.SettingsHash,
                write.SettingsHash,
                StringComparison.Ordinal) ||
            !JpegDimensions.TryRead(candidatePath, out var candidate))
        {
            return false;
        }

        return current.LongEdge >= Math.Max(candidate.Width, candidate.Height);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch
        {
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0) _queue.Writer.TryComplete();
        var completed = await Task.WhenAny(
            _processingTask,
            Task.Delay(_drainTimeout)).ConfigureAwait(false);
        if (completed == _processingTask)
        {
            await _processingTask.ConfigureAwait(false);
        }
        else
        {
            _cancellation.Cancel();
            // Outcomes never own pixels: an in-hand Save still disposes its image.
            lock (_outcomes)
                foreach (var write in _outcomes.ToArray()) Complete(write, persisted: false);
        }
    }

    internal Task ProcessingTask => _processingTask;

    private sealed record CacheWrite(
        string CachePath, long ImageId,
        string SourcePath,
        DateTime SourceWriteTime,
        string SettingsHash,
        PreviewCacheIdentity? Identity,
        object Image,
        TaskCompletionSource<bool> Completion);
}
