using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class FolderTreeServiceTests : IDisposable
{
    private readonly TemporaryDirectory _testRoot = new();

    [Fact]
    public void Tree_ExcludesOnlyConfiguredCatalogPath()
    {
        var browseRoot = Directory.CreateDirectory(Path.Combine(_testRoot.Path, "browse")).FullName;
        var catalogPath = Directory.CreateDirectory(
            Path.Combine(browseRoot, "Happy Photon Catalog")).FullName;
        Directory.CreateDirectory(Path.Combine(catalogPath, "assets"));
        Directory.CreateDirectory(Path.Combine(browseRoot, "Shoot"));

        var otherRoot = Directory.CreateDirectory(Path.Combine(_testRoot.Path, "other")).FullName;
        Directory.CreateDirectory(Path.Combine(otherRoot, "Happy Photon Catalog"));

        var service = new FolderTreeService(catalogPath);

        var browseNames = service.GetChildFolders(browseRoot)
            .Select(node => node.Name)
            .ToArray();
        var otherNames = service.GetChildFolders(otherRoot)
            .Select(node => node.Name)
            .ToArray();

        Assert.DoesNotContain("Happy Photon Catalog", browseNames);
        Assert.Contains("Shoot", browseNames);
        Assert.Contains("Happy Photon Catalog", otherNames);
    }

    [Fact]
    public void RootNode_WithTrailingSeparator_UsesFolderName()
    {
        var root = Directory.CreateDirectory(Path.Combine(_testRoot.Path, "Pictures")).FullName;
        var service = new FolderTreeService();

        var node = service.CreateRootNode(root + Path.DirectorySeparatorChar);

        Assert.Equal("Pictures", node.Name);
        Assert.Equal(root, node.Path);
    }

    [Fact]
    public void Validation_RejectsCatalogAndDescendantsButAllowsParentAndSibling()
    {
        var browseRoot = Directory.CreateDirectory(Path.Combine(_testRoot.Path, "browse")).FullName;
        var catalogPath = Directory.CreateDirectory(
            Path.Combine(browseRoot, "Happy Photon Catalog")).FullName;
        var catalogChild = Directory.CreateDirectory(
            Path.Combine(catalogPath, "assets")).FullName;
        var sibling = Directory.CreateDirectory(
            Path.Combine(browseRoot, "Happy Photon Catalog Photos")).FullName;
        var service = new FolderTreeService(catalogPath);

        Assert.Equal(BrowseLocationValidation.Valid,
            service.ValidateBrowseLocation(browseRoot));
        Assert.Equal(BrowseLocationValidation.Catalog,
            service.ValidateBrowseLocation(catalogPath));
        Assert.Equal(BrowseLocationValidation.Catalog,
            service.ValidateBrowseLocation(catalogChild));
        Assert.Equal(BrowseLocationValidation.Valid,
            service.ValidateBrowseLocation(sibling));
    }

    [Fact]
    public void IsWithinRoot_DoesNotAcceptPathPrefixSibling()
    {
        var root = Directory.CreateDirectory(Path.Combine(_testRoot.Path, "Photos")).FullName;
        var child = Directory.CreateDirectory(Path.Combine(root, "Shoot")).FullName;
        var prefixSibling = Directory.CreateDirectory(
            Path.Combine(_testRoot.Path, "Photos Backup")).FullName;
        var service = new FolderTreeService();

        Assert.True(service.IsWithinRoot(root, child));
        Assert.False(service.IsWithinRoot(root, prefixSibling));
    }

    [Fact]
    public void MissingPicturesCandidate_ReturnsNoDefaultLocation()
    {
        var missingPath = Path.Combine(_testRoot.Path, "missing-pictures");
        var service = new FolderTreeService(null, () => missingPath);

        Assert.Null(service.GetAvailablePicturesPath());
    }

    public void Dispose() => _testRoot.Dispose();
}
