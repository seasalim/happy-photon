namespace HappyPhoton.Services;

internal sealed class SingleInstanceGuard : IDisposable
{
    private const string ApplicationMutexName = "HappyPhoton.Application.SingleInstance";
    private Mutex? mutex;

    private SingleInstanceGuard(Mutex mutex)
    {
        this.mutex = mutex;
    }

    public static SingleInstanceGuard? TryAcquire(string mutexName = ApplicationMutexName)
    {
        var mutex = new Mutex(initiallyOwned: true, mutexName, out var createdNew);
        if (!createdNew)
        {
            mutex.Dispose();
            return null;
        }

        return new SingleInstanceGuard(mutex);
    }

    public void Dispose()
    {
        var ownedMutex = Interlocked.Exchange(ref mutex, null);
        if (ownedMutex is null)
        {
            return;
        }

        ownedMutex.ReleaseMutex();
        ownedMutex.Dispose();
    }
}
