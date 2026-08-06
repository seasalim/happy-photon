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
    private readonly string _temporaryDirectory;
    private readonly Channel<CacheWrite> _queue;
    private readonly Task _processingGate;
    private readonly TimeSpan _drainTimeout;
    private readonly CancellationTokenSource _cancellation = new();
    private readonly Task _processingTask;
    private int _disposed;

    public SettingsHashedCacheWriter(
        CatalogService catalogService,
        Func<long, string> getCachePath,
        int jpegQuality,
        int queueCapacity = 8,
        Task? processingGate = null,
        TimeSpan? drainTimeout = null)
    {
        _catalogService = catalogService;
        _getCachePath = getCachePath;
        _jpegQuality = jpegQuality;
        _temporaryDirectory = Path.Combine(catalogService.CatalogPath, "assets", "tmp");
        _processingGate = processingGate ?? Task.CompletedTask;
        _drainTimeout = drainTimeout ?? DefaultDrainTimeout;
        _queue = Channel.CreateBounded<CacheWrite>(
            new BoundedChannelOptions(queueCapacity)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = false
            },
            dropped => dropped.Image.Dispose());
        _processingTask = Task.Run(ProcessAsync);
    }

    public void Queue(ImageFile imageFile, MagickImage image, string settingsHash)
    {
        if (!CanQueue(imageFile, settingsHash)) return;

        MagickImage? clone = null;
        try
        {
            clone = new MagickImage(image);
            if (TryQueueOwned(imageFile, clone, settingsHash)) clone = null;
        }
        catch
        {
        }
        finally
        {
            clone?.Dispose();
        }
    }

    public void Queue(ImageFile imageFile, Bitmap bitmap, string settingsHash)
    {
        if (!CanQueue(imageFile, settingsHash)) return;

        MagickImage? image = null;
        try
        {
            image = ConvertToMagickImage(bitmap);
            if (TryQueueOwned(imageFile, image, settingsHash)) image = null;
        }
        catch
        {
        }
        finally
        {
            image?.Dispose();
        }
    }

    private bool CanQueue(ImageFile imageFile, string settingsHash) =>
        Volatile.Read(ref _disposed) == 0 &&
        imageFile.CatalogId != 0 &&
        !string.IsNullOrWhiteSpace(settingsHash);

    private bool TryQueueOwned(
        ImageFile imageFile,
        MagickImage image,
        string settingsHash)
    {
        var write = new CacheWrite(
            _getCachePath(imageFile.CatalogId),
            imageFile.FilePath,
            File.GetLastWriteTimeUtc(imageFile.FilePath),
            settingsHash,
            image);
        return _queue.Writer.TryWrite(write);
    }

    private async Task ProcessAsync()
    {
        try
        {
            await _processingGate.ConfigureAwait(false);
            await foreach (var write in _queue.Reader
                .ReadAllAsync(_cancellation.Token).ConfigureAwait(false))
            {
                Save(write);
            }
        }
        catch (OperationCanceledException) when (_cancellation.IsCancellationRequested)
        {
        }
        finally
        {
            while (_queue.Reader.TryRead(out var pending)) pending.Image.Dispose();
        }
    }

    private void Save(CacheWrite write)
    {
        var stem = Path.Combine(_temporaryDirectory, Guid.NewGuid().ToString("N"));
        var temporaryPath = $"{stem}.jpg";
        var temporaryMetadataPath = $"{stem}.meta";
        var metadataPath = Path.ChangeExtension(write.CachePath, ".meta");
        try
        {
            Directory.CreateDirectory(_temporaryDirectory);
            Directory.CreateDirectory(Path.GetDirectoryName(write.CachePath)!);
            write.Image.Quality = (uint)_jpegQuality;
            write.Image.Write(temporaryPath, MagickFormat.Jpeg);
            File.WriteAllText(
                temporaryMetadataPath,
                write.SettingsHash,
                new UTF8Encoding(false));
            if (File.GetLastWriteTimeUtc(write.SourcePath) != write.SourceWriteTime)
            {
                File.Delete(temporaryPath);
                File.Delete(temporaryMetadataPath);
                return;
            }

            File.Delete(metadataPath);
            File.Move(temporaryPath, write.CachePath, overwrite: true);
            File.Move(temporaryMetadataPath, metadataPath);
        }
        catch
        {
            TryDelete(temporaryPath);
            TryDelete(temporaryMetadataPath);
        }
        finally
        {
            write.Image.Dispose();
        }
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
        }
    }

    internal Task ProcessingTask => _processingTask;

    private sealed record CacheWrite(
        string CachePath,
        string SourcePath,
        DateTime SourceWriteTime,
        string SettingsHash,
        MagickImage Image);
}
