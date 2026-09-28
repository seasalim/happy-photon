namespace HappyPhoton.Services;

public sealed class CatalogUpgradeBackupException(Exception cause)
    : IOException($"The before-upgrade backup failed: {cause.Message} The catalog has not been upgraded.", cause);

public sealed partial class CatalogBackupService
{
    internal void BeforeUpgrade()
    {
        string? stem = null;
        var staging = Path.Combine(Path.GetTempPath(), $"hp-upgrade-{Guid.NewGuid():N}");

        try
        {
            AssertOwned();
            Directory.CreateDirectory(Folder);

            foreach (var path in Directory.GetFiles(Folder, "hp-backup-*.partial.*"))
            {
                if (IsBackupName(Path.GetFileName(path).Split(".partial.")[0]))
                    Change("sweep", () => File.Delete(path));
            }

            stem = Path.Combine(Folder, $"hp-backup-{Guid.NewGuid():N}");
            CatalogRestorePreservation.Create(catalog.CatalogPath, staging, stem, Change,
                "before-upgrade", path =>
                {
                    Step?.Invoke("before:upgrade-verify");
                    using var verified = CheckForRestore(path);
                });
        }
        catch (Exception ex)
        {
            try
            {
                if (stem != null) CatalogRestorePreservation.CleanupPartials(catalog.CatalogPath, stem, Change);
            }
            catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException)
            {
                // Preserve the failure reason; the next upgrade attempt sweeps any leftovers.
            }

            throw new CatalogUpgradeBackupException(ex);
        }
        finally
        {
            try
            {
                if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Temporary-copy cleanup must not mask the backup result.
            }
        }
    }
}
