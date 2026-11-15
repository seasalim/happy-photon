using System.Security.Cryptography;
using System.Text;
using ImageMagick;
using ImageMagick.Configuration;

namespace HappyPhoton.Services;

internal static class MagickSecurityPolicy
{
    internal static void Initialize()
    {
        var configuration = ConfigurationFiles.Default;
        const string closingTag = "</policymap>";
        if (!configuration.Policy.Data.Contains(closingTag, StringComparison.Ordinal))
            throw new InvalidOperationException("ImageMagick's default policy has no closing policymap tag.");

        configuration.Policy.Data = configuration.Policy.Data.Replace(
            closingTag,
            """<policy domain="system" name="max-profile-count" value="32" /></policymap>""");
        var directory = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "HappyPhoton",
            Environment.UserName, $"magick-{MagickNET.Version}-profiles-32"));
        var lockKey = OperatingSystem.IsWindows() ? directory.ToUpperInvariant() : directory;
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(lockKey)));
        using var mutex = new Mutex(false, $"HappyPhoton.MagickConfiguration.{hash}");

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
            Directory.CreateDirectory(directory);

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
}
