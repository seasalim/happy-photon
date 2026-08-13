using System.Runtime.InteropServices;

namespace HappyPhoton.Services;

public enum UpdateInstallChannel
{
    GitHubRelease,
    MicrosoftStore
}

public static partial class UpdateChannelSelector
{
    public const string MicrosoftStoreProductId = "9N45WWF08BP8";
    public const string MicrosoftStoreUri =
        "ms-windows-store://pdp/?productid=" + MicrosoftStoreProductId;

    public static UpdateInstallChannel Current => Select(
        OperatingSystem.IsWindows(),
        IsCurrentProcessPackaged);

    internal static UpdateInstallChannel Select(
        bool isWindows,
        Func<bool> isPackaged) =>
        isWindows && isPackaged()
            ? UpdateInstallChannel.MicrosoftStore
            : UpdateInstallChannel.GitHubRelease;

    private static bool IsCurrentProcessPackaged()
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        uint length = 0;
        return GetCurrentPackageFullName(ref length, null) ==
               ErrorInsufficientBuffer;
    }

    private const int ErrorInsufficientBuffer = 122;

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int GetCurrentPackageFullName(
        ref uint packageFullNameLength,
        char[]? packageFullName);
}
