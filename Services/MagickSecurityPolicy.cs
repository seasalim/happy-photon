using System.Security.Cryptography;
using System.Text;
using ImageMagick;
using ImageMagick.Configuration;

namespace HappyPhoton.Services;

internal static class MagickSecurityPolicy
{
    // The explicit root is for isolated test hosts; the app uses the OS per-user location.
    internal static void Initialize(string? localApplicationDataRoot = null)
    {
        var configuration = ConfigurationFiles.Default;
        const string closingTag = "</policymap>";
        if (!configuration.Policy.Data.Contains(closingTag, StringComparison.Ordinal))
            throw new InvalidOperationException("ImageMagick's default policy has no closing policymap tag.");

        configuration.Policy.Data = configuration.Policy.Data.Replace(
            closingTag,
            """<policy domain="system" name="max-profile-count" value="32" /></policymap>""");
        var directory = GetConfigurationDirectory(localApplicationDataRoot);
        var lockKey = OperatingSystem.IsWindows() ? directory.ToUpperInvariant() : directory;
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(lockKey)));
        // The same user's processes share these files even across login/shell sessions.
        using var mutex = new Mutex(false, $"HappyPhoton.MagickConfiguration.{hash}",
            new NamedWaitHandleOptions { CurrentUserOnly = true, CurrentSessionOnly = false });

        try
        {
            if (!mutex.WaitOne(TimeSpan.FromSeconds(30)))
                throw new TimeoutException("Timed out preparing ImageMagick configuration.");
        }
        catch (AbandonedMutexException)
        {
            // We own the lock; repair any incomplete write left by the previous process.
        }

        try
        {
            if (OperatingSystem.IsWindows())
            {
                // Inherit the per-user LocalApplicationData ACL.
                Directory.CreateDirectory(directory);
            }
            else
            {
                const UnixFileMode ownerOnly = UnixFileMode.UserRead |
                    UnixFileMode.UserWrite | UnixFileMode.UserExecute;
                Directory.CreateDirectory(directory, ownerOnly);
                File.SetUnixFileMode(directory, ownerOnly);
            }

            foreach (var file in configuration.All)
            {
                var path = Path.Combine(directory, file.FileName);
                if (File.Exists(path) && File.ReadAllText(path) == file.Data) continue;

                // One staging name under the process lock also recovers interrupted writes.
                var temporary = path + ".tmp";

                try
                {
                    File.WriteAllText(temporary, file.Data);
                    File.Move(temporary, path, overwrite: true);
                }
                finally
                {
                    File.Delete(temporary);
                }
            }

            // The library skips existing files; all are complete before it sees this path.
            MagickNET.Initialize(configuration, directory);
        }
        finally
        {
            mutex.ReleaseMutex();
        }
    }

    internal static string GetConfigurationDirectory(string? localApplicationDataRoot = null)
    {
        var root = localApplicationDataRoot ?? Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.DoNotVerify);
        if (string.IsNullOrWhiteSpace(root) || !Path.IsPathFullyQualified(root))
            throw new InvalidOperationException("A per-user local application data directory is required.");

        return Path.GetFullPath(Path.Combine(root, "Happy Photon",
            $"magick-{MagickNET.Version}-profiles-32"));
    }
}
