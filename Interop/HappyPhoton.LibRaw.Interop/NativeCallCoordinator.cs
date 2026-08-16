namespace HappyPhoton.LibRaw.Interop;

internal interface INativeCallObserver
{
    void BeforeNativeCall();
}

internal static class NativeCallCoordinator
{
    internal static void Before(CancellationToken cancellationToken,
        INativeCallObserver? observer)
    {
        cancellationToken.ThrowIfCancellationRequested();
        observer?.BeforeNativeCall();
        cancellationToken.ThrowIfCancellationRequested();
    }
}
