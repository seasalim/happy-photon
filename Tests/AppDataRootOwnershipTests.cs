using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class AppDataRootOwnershipTests : IDisposable
{
    private readonly TemporaryDirectory _root = new();

    [Fact]
    public void AssertAppOwned_RejectsMissingAndChangedMarkers()
    {
        var target = Directory.CreateDirectory(Path.Combine(_root.Path, "target")).FullName;
        Assert.Throws<AppDataOwnershipException>(
            () => AppDataRootOwnership.AssertAppOwned(target));

        AppDataRootOwnership.Claim(target);
        File.WriteAllText(Path.Combine(target, AppDataRootOwnership.MarkerFileName), "changed");

        Assert.Throws<AppDataOwnershipException>(
            () => AppDataRootOwnership.AssertAppOwned(target));
    }

    [Fact]
    public void Validation_RejectsEqualAncestorAndObviousRoots()
    {
        var catalog = Path.Combine(_root.Path, "catalog");
        var cache = Path.Combine(catalog, "cache");

        Assert.Throws<ArgumentException>(() =>
            AppDataRootOwnership.ValidateProposedRoots(catalog, catalog));
        Assert.Throws<ArgumentException>(() =>
            AppDataRootOwnership.ValidateProposedRoots(catalog, cache));
        Assert.Throws<ArgumentException>(() =>
            AppDataRootOwnership.ValidateObviousTarget(Path.GetPathRoot(_root.Path)!));
    }

    [Fact]
    public void CreateDedicatedChild_InsideExistingRoot_RefusesBeforeClaiming()
    {
        var root = Path.Combine(
            Path.GetTempPath(), $"happy-photon-child-overlap-{Guid.NewGuid():N}");
        var catalogRoot = Path.Combine(root, "catalog");
        try
        {
            AppDataRootOwnership.Claim(catalogRoot);

            Assert.Throws<ArgumentException>(() =>
                AppDataRootOwnership.CreateDedicatedChild(
                    catalogRoot, "Happy Photon Cache", [catalogRoot]));
            Assert.False(Directory.Exists(
                Path.Combine(catalogRoot, "Happy Photon Cache")));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task RefusedMove_LeavesFilesystemByteIdentical()
    {
        var source = Path.Combine(_root.Path, "source");
        var destination = Path.Combine(_root.Path, "destination");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(destination);
        await File.WriteAllBytesAsync(Path.Combine(source, "catalog.db"), [1, 2, 3]);
        await File.WriteAllBytesAsync(Path.Combine(destination, "keep.bin"), [4, 5, 6]);
        var before = Snapshot(_root.Path);
        var locationService = new AppDataLocationService(new AppDataPlatformPaths(
            Path.Combine(_root.Path, "Pictures"), Path.Combine(_root.Path, "pointer"),
            Path.Combine(_root.Path, "data"), Path.Combine(_root.Path, "cache")));
        var migrator = new CatalogLocationMigrator(locationService);
        var locations = new AppDataLocations(
            source, Path.Combine(_root.Path, "cache-root"),
            AppDataLocationOrigin.Persisted, AppDataLocationOrigin.Persisted);

        await Assert.ThrowsAsync<AppDataOwnershipException>(() =>
            migrator.StageMoveAsync(
                locations, CatalogLocationMoveKind.Catalog, destination));

        Assert.Equal(before, Snapshot(_root.Path));
    }

    private static byte[] Snapshot(string root)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        foreach (var path in Directory.EnumerateFileSystemEntries(
                     root, "*", SearchOption.AllDirectories).Order())
        {
            writer.Write(Path.GetRelativePath(root, path));
            if (File.Exists(path)) writer.Write(File.ReadAllBytes(path));
        }
        return stream.ToArray();
    }

    public void Dispose() => _root.Dispose();
}
