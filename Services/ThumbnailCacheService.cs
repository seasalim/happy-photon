using System.Threading.Channels;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using ImageMagick;
using HappyPhoton.Models;

namespace HappyPhoton.Services;

public sealed class ThumbnailCacheService : IAsyncDisposable
{
    private const int QueueCapacity = 256;
    private static readonly TimeSpan ShutdownDrainTimeout = TimeSpan.FromSeconds(2);

    private readonly ICatalogService _catalogService;
    private readonly string _temporaryDirectory;
    private readonly Channel<CacheWrite> _saveQueue;
    private readonly Task _processingGate;
    private readonly TimeSpan _shutdownDrainTimeout;
    private readonly CancellationTokenSource _writerCancellation = new();
    private readonly Task _processingTask;
    private int _disposed;

    public ThumbnailCacheService(ICatalogService catalogService) : this(
        catalogService,
        QueueCapacity,
        Task.CompletedTask,
        ShutdownDrainTimeout)
    {
    }

    internal ThumbnailCacheService(
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
            dropped => dropped.Bitmap.Dispose());
        _processingTask = Task.Run(ProcessSaveQueueAsync);
    }

    public string GetCachePath(ImageFile imageFile)
    {
        if (imageFile.CatalogId == 0)
            throw new InvalidOperationException("Image must be in catalog before caching.");
        return _catalogService.GetThumbnailPath(imageFile.CatalogId);
    }

    public bool IsCacheValid(ImageFile imageFile)
    {
        if (imageFile.CatalogId == 0) return false;
        var cachePath = _catalogService.GetThumbnailPath(imageFile.CatalogId);
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

    public Bitmap? LoadFromCache(ImageFile imageFile)
    {
        if (imageFile.CatalogId == 0) return null;
        try
        {
            var cachePath = _catalogService.GetThumbnailPath(imageFile.CatalogId);
            if (!File.Exists(cachePath)) return null;
            var bitmap = new Bitmap(cachePath);
            if (!IsJpeg(cachePath)) QueueSaveToCache(imageFile, bitmap);
            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    public void QueueSaveToCache(ImageFile imageFile, Bitmap bitmap)
    {
        if (Volatile.Read(ref _disposed) != 0 || imageFile.CatalogId == 0) return;

        var cacheBitmap = Clone(bitmap);
        if (cacheBitmap == null) return;

        try
        {
            var write = new CacheWrite(
                _catalogService.GetThumbnailPath(imageFile.CatalogId),
                imageFile.FilePath,
                File.GetLastWriteTimeUtc(imageFile.FilePath),
                cacheBitmap);
            if (!_saveQueue.Writer.TryWrite(write))
            {
                cacheBitmap.Dispose();
            }
        }
        catch
        {
            cacheBitmap.Dispose();
        }
    }

    private static WriteableBitmap? Clone(Bitmap bitmap)
    {
        WriteableBitmap? clone = null;
        try
        {
            clone = new WriteableBitmap(
                bitmap.PixelSize,
                bitmap.Dpi,
                PixelFormat.Bgra8888,
                AlphaFormat.Premul);
            using var buffer = clone.Lock();
            bitmap.CopyPixels(buffer);
            return clone;
        }
        catch
        {
            clone?.Dispose();
            return null;
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
                pending.Bitmap.Dispose();
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
            using (var image = BitmapConversionService.ConvertToMagickImage(write.Bitmap))
            {
                image.BackgroundColor = MagickColors.Black;
                image.Alpha(AlphaOption.Remove);
                image.Quality = 85;
                image.Write(temporaryPath, MagickFormat.Jpeg);
            }
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
            write.Bitmap.Dispose();
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

    private static bool IsJpeg(string path)
    {
        Span<byte> signature = stackalloc byte[3];
        using var stream = File.OpenRead(path);
        return stream.Read(signature) == signature.Length &&
               signature[0] == 0xff &&
               signature[1] == 0xd8 &&
               signature[2] == 0xff;
    }

    private sealed record CacheWrite(
        string CachePath,
        string SourcePath,
        DateTime SourceWriteTime,
        Bitmap Bitmap);
}
