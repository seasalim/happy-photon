using System.Runtime.ExceptionServices;

namespace HappyPhoton.Tests;

// Opt-in dwell evidence: retain setter exceptions even if a load catches them.
internal sealed class LoadingLabelDiagnostics : IDisposable
{
    private readonly string _path;
    private readonly object _sync = new();

    internal LoadingLabelDiagnostics(string directory, string surface)
    {
        _path = Path.Combine(directory, $"{surface}-exceptions.txt");
        AppDomain.CurrentDomain.FirstChanceException += Record;
    }

    private void Record(object? sender, FirstChanceExceptionEventArgs args)
    {
        if (args.Exception is not ObjectDisposedException) return;
        lock (_sync) File.AppendAllText(_path, args.Exception + Environment.NewLine);
    }

    public void Dispose() => AppDomain.CurrentDomain.FirstChanceException -= Record;
}
