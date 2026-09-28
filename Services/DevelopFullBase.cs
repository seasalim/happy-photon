using System.Diagnostics;
using Avalonia.Media.Imaging;
using HappyPhoton.Models;

namespace HappyPhoton.Services;

internal sealed class DevelopFullBase : IAsyncDisposable
{
    private readonly Func<CancellationToken, Task<BaseImage?>> _load;

    private readonly SemaphoreSlim _serial = new(1);

    private readonly CancellationTokenSource _lifetime = new();

    private BaseImage? _base;

    internal DevelopFullBase(Func<CancellationToken, Task<BaseImage?>> load) => _load = load;

    internal int HeldCount => Volatile.Read(ref _base) == null ? 0 : 1;

    internal async Task<Bitmap?> RenderAsync(EditSettings settings, RenderExecutionOptions execution,
        Action<Bitmap, BaseImage> tag, Action<RenderRequest, ImageMagick.MagickImage>? observe = null,
        Action<string>? trace = null)
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            _lifetime.Token, execution.CancellationToken);
        var token = cancellation.Token;
        await _serial.WaitAsync(token).ConfigureAwait(false);

        try
        {
            _base ??= await _load(_lifetime.Token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if (_base == null) return null;

            return await Task.Run(() => Render(settings, execution with { CancellationToken = token }, tag, observe, trace),
                token).ConfigureAwait(false);
        }
        finally
        {
            _serial.Release();
        }
    }

    private Bitmap? Render(EditSettings settings, RenderExecutionOptions execution,
        Action<Bitmap, BaseImage> tag, Action<RenderRequest, ImageMagick.MagickImage>? observe,
        Action<string>? trace)
    {
        var request = new RenderRequest(_base!, settings, RenderIntent.Export, null, new(false, false));
        trace?.Invoke("export-start");
        using var rendered = new RenderPipeline().RenderResting(
            request, execution);
        trace?.Invoke("export-end");
        observe?.Invoke(request, rendered.Image);

        execution.ThrowIfCancellationRequested();
        execution.ReportStage("bitmap-conversion");
        trace?.Invoke("conversion-start");
        var bitmap = BitmapConversionService.ConvertToBitmap(rendered.Image);
        trace?.Invoke("conversion-end");

        try
        {
            execution.ThrowIfCancellationRequested();
            if (bitmap != null) tag(bitmap, _base!);

            return bitmap;
        }
        catch
        {
            bitmap?.Dispose();
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        _lifetime.Cancel();
        await _serial.WaitAsync().ConfigureAwait(false);

        try
        {
            Interlocked.Exchange(ref _base, null)?.Dispose();
        }
        finally
        {
            _serial.Release();
        }
    }

    internal static double LastCollectionMilliseconds { get; private set; }

    internal static Task<double> ReclaimAsync() => Task.Run(() =>
    {
        var timer = Stopwatch.StartNew();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
        ImageServiceHelpers.LogDisplayTrace($"full-base gc-ms={timer.Elapsed.TotalMilliseconds:F1}");
        return LastCollectionMilliseconds = timer.Elapsed.TotalMilliseconds;
    });
}
