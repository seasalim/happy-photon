using System.Threading.Channels;
using ImageMagick;
using HappyPhoton.Models;

namespace HappyPhoton.Services;

public sealed class PreviewCacheService : IAsyncDisposable
{
    private const int PreviewQuality = 90;
    private const int QueueCapacity = 8;
    private static readonly TimeSpan ShutdownDrainTimeout = TimeSpan.FromSeconds(2);

    private readonly ICatalogService _catalogService;
    private readonly string _temporaryDirectory;
    private readonly Channel<CacheWrite> _saveQueue;
    private readonly Task _processingGate;
    private readonly TimeSpan _shutdownDrainTimeout;
    private readonly CancellationTokenSource _writerCancellation = new();
    private readonly Task _processingTask;
    private int _disposed;

    public PreviewCacheService(ICatalogService catalogService) : this(
        catalogService,
        QueueCapacity,
        Task.CompletedTask,
        ShutdownDrainTimeout)
    {
    }

    internal PreviewCacheService(
        ICatalogService catalogService,
        int queueCapacity,
        Task processingGate,
        TimeSpan shutdownDrainTimeout)
    {
        _catalogService = catalogService;
        _temporaryDirectory = Path.Combine(catalogService.CatalogPath, "assets", "tmp");
        _processingGate = processingGate;
        _shutdownDrainTimeout = shutdownDrainTimeout;
        _saveQueue = Channel.CreateBounded<CacheWrite>(
            new BoundedChannelOptions(queueCapacity)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = false
            },
            dropped => dropped.Image.Dispose());
        _processingTask = Task.Run(ProcessSaveQueueAsync);
    }

    public string GetCachePath(ImageFile imageFile)
    {
        if (imageFile.CatalogId == 0)
            throw new InvalidOperationException("Image must be in catalog before caching.");
        return _catalogService.GetPreviewPath(imageFile.CatalogId);
    }

    public bool IsCacheValid(ImageFile imageFile)
    {
        if (imageFile.CatalogId == 0) return false;
        var cachePath = _catalogService.GetPreviewPath(imageFile.CatalogId);
        if (!File.Exists(cachePath)) return false;
        try
        {
            return File.GetLastWriteTimeUtc(cachePath) > File.GetLastWriteTimeUtc(imageFile.FilePath);
        }
        catch
        {
            return false;
        }
    }

    public MagickImage? LoadFromCache(ImageFile imageFile)
    {
        if (imageFile.CatalogId == 0) return null;
        try
        {
            var cachePath = _catalogService.GetPreviewPath(imageFile.CatalogId);
            return File.Exists(cachePath) ? new MagickImage(cachePath) : null;
        }
        catch
        {
            return null;
        }
    }

    public void QueueSaveToCache(ImageFile imageFile, MagickImage image)
    {
        if (Volatile.Read(ref _disposed) != 0 || imageFile.CatalogId == 0) return;

        MagickImage? clone = null;
        try
        {
            clone = new MagickImage(image);
            var write = new CacheWrite(
                _catalogService.GetPreviewPath(imageFile.CatalogId),
                imageFile.FilePath,
                File.GetLastWriteTimeUtc(imageFile.FilePath),
                clone);
            if (_saveQueue.Writer.TryWrite(write))
            {
                clone = null;
            }
        }
        catch
        {
        }
        finally
        {
            clone?.Dispose();
        }
    }

    private async Task ProcessSaveQueueAsync()
    {
        try
        {
            await _processingGate.ConfigureAwait(false);
            await foreach (var write in _saveQueue.Reader
                .ReadAllAsync(_writerCancellation.Token).ConfigureAwait(false))
            {
                Save(write);
            }
        }
        catch (OperationCanceledException) when (_writerCancellation.IsCancellationRequested)
        {
        }
        finally
        {
            while (_saveQueue.Reader.TryRead(out var pending))
            {
                pending.Image.Dispose();
            }
        }
    }

    private void Save(CacheWrite write)
    {
        var temporaryPath = Path.Combine(_temporaryDirectory, $"{Guid.NewGuid():N}.jpg");
        try
        {
            Directory.CreateDirectory(_temporaryDirectory);
            var cacheDirectory = Path.GetDirectoryName(write.CachePath);
            if (!string.IsNullOrEmpty(cacheDirectory)) Directory.CreateDirectory(cacheDirectory);
            write.Image.Quality = PreviewQuality;
            write.Image.Write(temporaryPath, MagickFormat.Jpeg);
            if (File.GetLastWriteTimeUtc(write.SourcePath) != write.SourceWriteTime)
            {
                File.Delete(temporaryPath);
                return;
            }
            File.Move(temporaryPath, write.CachePath, overwrite: true);
        }
        catch
        {
            try
            {
                if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
            }
            catch
            {
            }
        }
        finally
        {
            write.Image.Dispose();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            _saveQueue.Writer.TryComplete();
        }

        var completed = await Task.WhenAny(
            _processingTask,
            Task.Delay(_shutdownDrainTimeout)).ConfigureAwait(false);
        if (completed == _processingTask)
        {
            await _processingTask.ConfigureAwait(false);
        }
        else
        {
            _writerCancellation.Cancel();
        }
    }

    internal Task ProcessingTask => _processingTask;

    private sealed record CacheWrite(
        string CachePath,
        string SourcePath,
        DateTime SourceWriteTime,
        MagickImage Image);
}
