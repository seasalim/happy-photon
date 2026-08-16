using HappyPhoton.LibRaw.Interop;

namespace HappyPhoton.Services;

internal static class LibRawNativeSupport
{
    private static readonly Lazy<LibRawRuntimeHealth> ProcessHealth = CreateLazy(
        () => LibRawContext.RuntimeHealth,
        ImageServiceHelpers.LogError);

    public static LibRawRuntimeHealth Health => ProcessHealth.Value;

    public static bool IsAvailable => Health.IsHealthy;

    public static Task<LibRawRuntimeHealth> ProbeAsync() =>
        Task.Run(() => Health);

    internal static Lazy<LibRawRuntimeHealth> CreateLazy(
        Func<LibRawRuntimeHealth> probe,
        Action<string> logError)
    {
        ArgumentNullException.ThrowIfNull(probe);
        ArgumentNullException.ThrowIfNull(logError);
        return new Lazy<LibRawRuntimeHealth>(() =>
        {
            var health = probe();
            if (!health.IsHealthy)
            {
                logError(health.DiagnosticText);
            }
            return health;
        }, LazyThreadSafetyMode.ExecutionAndPublication);
    }
}
