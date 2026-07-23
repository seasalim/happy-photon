namespace HappyPhoton.Services;

/// <summary>
/// Version and build-time info for the running binary, shown in the status bar.
/// </summary>
public static class AppBuildInfo
{
    public static Version Version { get; } =
        typeof(AppBuildInfo).Assembly.GetName().Version ?? new Version(0, 0, 0);

    public static DateTime BuildTime { get; } = GetBuildTime();

    public static string StatusText { get; } =
        $"v{Version.ToString(3)} · built {BuildTime:yyyy-MM-dd HH:mm}";

    private static DateTime GetBuildTime()
    {
        var path = Environment.ProcessPath;
        return path == null ? default : File.GetLastWriteTime(path);
    }
}
