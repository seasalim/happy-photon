using System.Reflection;
using System.Runtime.InteropServices;

namespace HappyPhoton.Services;

/// <summary>
/// Build identity and support information for the running application.
/// </summary>
public static class AppBuildInfo
{
    private static readonly Assembly Assembly = typeof(AppBuildInfo).Assembly;

    public static Version Version { get; } =
        Assembly.GetName().Version ?? new Version(0, 0, 0);

    public static AppBuildIdentity Identity { get; } = AppBuildIdentityFactory.Create(
        new AppBuildInfoInputs(
            Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion,
            Version.ToString(3),
            GetMetadata("SourceRevision"),
            GetMetadata("BuildTimestampUtc"),
            GetMetadata("RepositoryUrl"),
            Assembly.GetCustomAttribute<AssemblyCopyrightAttribute>()?.Copyright,
            GetLocalExecutableTimestamp(),
            RuntimeInformation.OSDescription,
            RuntimeInformation.ProcessArchitecture.ToString()));

    public static string StatusText { get; } =
        $"v{Identity.FriendlyVersion} · {Identity.DateDisplayText}";

    private static string? GetMetadata(string key) =>
        Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => attribute.Key == key)
            ?.Value;

    private static DateTimeOffset? GetLocalExecutableTimestamp()
    {
        try
        {
            var path = Environment.ProcessPath;
            return path == null
                ? null
                : new DateTimeOffset(File.GetLastWriteTimeUtc(path), TimeSpan.Zero);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or
            ArgumentException or NotSupportedException or
            System.Security.SecurityException)
        {
            return null;
        }
    }
}
