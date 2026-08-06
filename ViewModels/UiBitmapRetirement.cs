using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace HappyPhoton.ViewModels;

internal sealed class UiBitmapRetirement : IDisposable
{
    private readonly object _sync = new();
    private readonly HashSet<Bitmap> _pending = new(
        ReferenceEqualityComparer.Instance);
    private bool _disposed;

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
        }

        if (!isCurrent()) bitmap.Dispose();
    }
}
