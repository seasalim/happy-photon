namespace HappyPhoton.Services;

internal enum SourceAvailability
{
    AvailableLocally,
    RequiresHydration,
    Unknown,
    Unavailable
}

internal enum SourceReadIntent
{
    Background,
    UserApprovedHydration
}

internal interface ISourceAvailabilityService
{
    SourceAvailability GetAvailability(string path);
}

internal static class SourceAvailabilityExtensions
{
    // The single definition of "online-only"; call sites differ in scope
    // (library banner, selection summary, export estimate), not in predicate.
    internal static bool IsOnlineOnly(this SourceAvailability availability) =>
        availability == SourceAvailability.RequiresHydration;
}

internal static class SourceAccessPolicy
{
    internal static bool CanRead(
        SourceAvailability availability,
        SourceReadIntent intent) => availability switch
        {
            SourceAvailability.AvailableLocally => true,
            SourceAvailability.Unknown => true,
            SourceAvailability.RequiresHydration =>
                intent == SourceReadIntent.UserApprovedHydration,
            _ => false
        };
}

internal sealed class SourceAvailabilityService : ISourceAvailabilityService
{
    private const FileAttributes RecallOnOpen =
        (FileAttributes)0x00040000;
    private const FileAttributes RecallOnDataAccess =
        (FileAttributes)0x00400000;
    private const FileAttributes HydrationAttributes =
        FileAttributes.Offline | RecallOnOpen | RecallOnDataAccess;

    public SourceAvailability GetAvailability(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            return SourceAvailability.Unknown;
        }

        try
        {
            return ClassifyWindowsAttributes(File.GetAttributes(path));
        }
        catch (IOException)
        {
            return SourceAvailability.Unavailable;
        }
        catch (UnauthorizedAccessException)
        {
            return SourceAvailability.Unavailable;
        }
    }

    internal static SourceAvailability GetEnumerationHint(FileInfo file)
    {
        if (!OperatingSystem.IsWindows())
        {
            return SourceAvailability.Unknown;
        }

        try
        {
            return ClassifyWindowsAttributes(file.Attributes);
        }
        catch (IOException)
        {
            return SourceAvailability.Unavailable;
        }
        catch (UnauthorizedAccessException)
        {
            return SourceAvailability.Unavailable;
        }
    }

    internal static SourceAvailability ClassifyWindowsAttributes(
        FileAttributes attributes) =>
        (attributes & HydrationAttributes) != 0
            ? SourceAvailability.RequiresHydration
            : SourceAvailability.AvailableLocally;
}
