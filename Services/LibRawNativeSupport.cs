using Sdcb.LibRaw;

namespace HappyPhoton.Services;

internal static class LibRawNativeSupport
{
    private static readonly Lazy<bool> Availability = new(
        Probe,
        LazyThreadSafetyMode.ExecutionAndPublication);

    public static bool IsAvailable => Availability.Value;

    private static bool Probe()
    {
        try
        {
            _ = RawContext.VersionNumber;
            return true;
        }
        catch (Exception exception) when (
            exception is DllNotFoundException or
                BadImageFormatException or
                EntryPointNotFoundException or
                TypeInitializationException)
        {
            ImageServiceHelpers.LogDebug(
                nameof(LibRawNativeSupport),
                $"Native LibRaw is unavailable: {exception.Message}");
            return false;
        }
    }
}
