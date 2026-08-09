using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace HappyPhoton.ViewModels;

internal sealed class UiBitmapRetirement : IDisposable
{
    private readonly object _sync = new();
    private readonly HashSet<Bitmap> _pending = new(
        ReferenceEqualityComparer.Instance);
    private bool _disposed;
    private long _pendingBytes;
    private long _peakPendingBytes;

    public long PendingBytes => Interlocked.Read(ref _pendingBytes);
    public long PeakPendingBytes => Interlocked.Read(ref _peakPendingBytes);

    public void Retire(Bitmap bitmap, Func<bool> isCurrent)
    {
        lock (_sync)
        {
            if (_disposed)
            {
                bitmap.Dispose();
                return;
            }

            if (!_pending.Add(bitmap)) return;
            var pending = Interlocked.Add(
                ref _pendingBytes,
                EstimateBytes(bitmap));
            UpdatePeak(pending);
        }

        Dispatcher.UIThread.Post(
            () => CompleteRetirement(bitmap, isCurrent),
            DispatcherPriority.Loaded);
    }

    public void Dispose()
    {
        Bitmap[] pending;
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
            pending = _pending.ToArray();
            _pending.Clear();
            Interlocked.Exchange(ref _pendingBytes, 0);
        }

        foreach (var bitmap in pending)
        {
            bitmap.Dispose();
        }
    }

    private void CompleteRetirement(Bitmap bitmap, Func<bool> isCurrent)
    {
        lock (_sync)
        {
            if (!_pending.Remove(bitmap)) return;
            Interlocked.Add(ref _pendingBytes, -EstimateBytes(bitmap));
        }

        if (!isCurrent()) bitmap.Dispose();
    }

    private static long EstimateBytes(Bitmap bitmap) =>
        (long)bitmap.PixelSize.Width * bitmap.PixelSize.Height * 4;

    private void UpdatePeak(long value)
    {
        var current = Interlocked.Read(ref _peakPendingBytes);
        while (value > current)
        {
            var observed = Interlocked.CompareExchange(
                ref _peakPendingBytes,
                value,
                current);
            if (observed == current) return;
            current = observed;
        }
    }
}
