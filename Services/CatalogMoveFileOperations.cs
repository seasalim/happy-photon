namespace HappyPhoton.Services;

internal static class CatalogMoveFileOperations
{
    public static bool ShouldRenameCache(string source, string destination)
    {
        var sourceAssets = Path.Combine(source, "assets");
        if (!Directory.Exists(sourceAssets)) return false;
        if (!CanRenameBetween(source, destination)) return false;
        var destinationAssets = Path.Combine(destination, "assets");
        if (Directory.Exists(destinationAssets) &&
            Directory.EnumerateFileSystemEntries(destinationAssets).Any())
        {
            throw new IOException("The destination cache is not empty.");
        }
        return true;
    }

    public static void MoveCacheOrResume(string source, string destination)
    {
        var sourceAssets = Path.Combine(source, "assets");
        var destinationAssets = Path.Combine(destination, "assets");
        if (!Directory.Exists(sourceAssets))
        {
            if (!Directory.Exists(destinationAssets)) throw new IOException(
                "The cache move cannot be resumed.");
            AppDataRootOwnership.AssertAppOwned(destination);
            return;
        }
        if (Directory.Exists(destinationAssets))
        {
            if (Directory.EnumerateFileSystemEntries(destinationAssets).Any())
                throw new IOException("The destination cache is not empty.");
            AppDataRootOwnership.AssertAppOwned(destination);
            Directory.Delete(destinationAssets);
        }
        AppDataRootOwnership.AssertAppOwned(source);
        Directory.Move(sourceAssets, destinationAssets);
    }

    public static void MoveAsideOrResume(string source, string aside)
    {
        if (Directory.Exists(source))
        {
            if (Directory.Exists(aside))
                throw new IOException("The set-aside destination already exists.");
            AppDataRootOwnership.AssertAppOwned(source);
            Directory.Move(source, aside);
            return;
        }
        if (!Directory.Exists(aside)) throw new IOException(
            "The set-aside operation cannot be resumed.");
        AppDataRootOwnership.AssertAppOwned(aside);
    }

    public static void RestoreAside(string? aside, string root)
    {
        if (aside == null || !Directory.Exists(aside) || Directory.Exists(root)) return;
        AppDataRootOwnership.AssertAppOwned(aside);
        Directory.Move(aside, root);
    }

    public static void CopyCatalog(string source, string destination)
    {
        AppDataRootOwnership.AssertAppOwned(source);
        AppDataRootOwnership.AssertAppOwned(destination);
        CopyFile(source, destination, "catalog.db");
        CopyFile(source, destination, ".catalog-identity");
        var sourcePresets = Path.Combine(source, "presets");
        var destinationPresets = Path.Combine(destination, "presets");
        Directory.CreateDirectory(destinationPresets);
        if (!Directory.Exists(sourcePresets)) return;
        foreach (var preset in Directory.EnumerateFiles(sourcePresets, "*.json"))
        {
            File.Copy(preset, Path.Combine(destinationPresets, Path.GetFileName(preset)), true);
        }
    }

    private static void CopyFile(string source, string destination, string name)
    {
        var path = Path.Combine(source, name);
        if (!File.Exists(path)) throw new FileNotFoundException(name, path);
        File.Copy(path, Path.Combine(destination, name), true);
    }

    public static void DeleteFile(string root, string relative)
    {
        var path = Path.Combine(root, relative);
        if (!File.Exists(path)) return;
        AppDataRootOwnership.AssertAppOwned(root);
        File.Delete(path);
    }

    public static void DeleteKnownDirectory(string root, string relative)
    {
        var path = Path.Combine(root, relative);
        if (!Directory.Exists(path)) return;
        AppDataRootOwnership.AssertAppOwned(root);
        Directory.Delete(path, recursive: true);
    }

    // Path.GetPathRoot is "/" for every Unix mount, so volume identity must be
    // probed with a real rename rather than compared by root string.
    private static bool CanRenameBetween(string source, string destination)
    {
        if (!string.Equals(
                Path.GetPathRoot(Path.GetFullPath(source)),
                Path.GetPathRoot(Path.GetFullPath(destination)),
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        var probe = Path.Combine(source, $".move-probe-{Guid.NewGuid():N}");
        var target = Path.Combine(destination, $".move-probe-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(probe);
            Directory.Move(probe, target);
            Directory.Delete(target);
            return true;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
        finally
        {
            try
            {
                if (Directory.Exists(probe)) Directory.Delete(probe);
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    public static bool SamePath(string left, string right) =>
        string.Equals(
            Path.GetFullPath(left),
            Path.GetFullPath(right),
            OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal);

    public static void AssertDestinationEmpty(string destination)
    {
        var unexpected = Directory.EnumerateFileSystemEntries(destination)
            .Any(path => !string.Equals(
                Path.GetFileName(path),
                AppDataRootOwnership.MarkerFileName,
                StringComparison.Ordinal));
        if (unexpected)
        {
            throw new IOException(
                "The dedicated destination folder must be empty before a move is staged.");
        }
    }
}
