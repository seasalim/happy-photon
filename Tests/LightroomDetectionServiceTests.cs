using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class LightroomDetectionServiceTests : IDisposable
{
    private readonly TemporaryDirectory _root = new();

    [Fact]
    public async Task DetectAsync_FindsCatalogAtMaximumDepth()
    {
        var pictures = Directory.CreateDirectory(Path.Combine(_root.Path, "pictures"));
        var nested = Directory.CreateDirectory(Path.Combine(
            pictures.FullName, "year", "Lightroom"));
        var catalog = Path.Combine(nested.FullName, "photos.lrcat");
        await File.WriteAllBytesAsync(catalog, [1]);
        var service = CreateService(maxDepth: 2);

        var result = await service.DetectAsync(pictures.FullName, null);

        Assert.True(result.IsDetected);
        Assert.Equal([catalog], result.CatalogPaths);
    }

    [Fact]
    public async Task DetectAsync_DoesNotDescendPastMaximumDepth()
    {
        var pictures = Directory.CreateDirectory(Path.Combine(_root.Path, "pictures"));
        var nested = Directory.CreateDirectory(Path.Combine(
            pictures.FullName, "one", "two", "three"));
        await File.WriteAllBytesAsync(Path.Combine(nested.FullName, "photos.lrcat"), [1]);
        var service = CreateService(maxDepth: 2);

        var result = await service.DetectAsync(pictures.FullName, null);

        Assert.False(result.IsDetected);
    }

    [Fact]
    public async Task DetectAsync_ProbesDefaultPicturesLightroomFirst()
    {
        var pictures = Directory.CreateDirectory(Path.Combine(_root.Path, "default-pictures"));
        var lightroom = Directory.CreateDirectory(Path.Combine(
            pictures.FullName, "Lightroom"));
        var catalog = Path.Combine(lightroom.FullName, "default.lrcat");
        await File.WriteAllBytesAsync(catalog, [1]);
        var service = CreateService(defaultPicturesRoot: pictures.FullName);

        var result = await service.DetectAsync(null, null);

        Assert.Equal([catalog], result.CatalogPaths);
    }

    [Fact]
    public async Task DetectAsync_RespectsPerDirectoryEntryLimit()
    {
        var pictures = Directory.CreateDirectory(Path.Combine(_root.Path, "pictures"));
        var first = Directory.CreateDirectory(Path.Combine(pictures.FullName, "first"));
        var second = Directory.CreateDirectory(Path.Combine(pictures.FullName, "second"));
        var firstEntry = Directory.EnumerateFileSystemEntries(pictures.FullName).First();
        var beyondLimit = string.Equals(
            firstEntry, first.FullName, StringComparison.OrdinalIgnoreCase)
            ? second
            : first;
        await File.WriteAllBytesAsync(
            Path.Combine(beyondLimit.FullName, "hidden.lrcat"), [1]);
        var service = CreateService(entryLimit: 1);

        var result = await service.DetectAsync(pictures.FullName, null);

        Assert.False(result.IsDetected);
    }

    [Fact]
    public async Task DetectAsync_StopsWhenAggregateEntryLimitIsExhausted()
    {
        var defaultPictures = Directory.CreateDirectory(Path.Combine(
            _root.Path, "default-pictures"));
        var firstRoot = Directory.CreateDirectory(Path.Combine(
            defaultPictures.FullName, "Lightroom"));
        await File.WriteAllBytesAsync(
            Path.Combine(firstRoot.FullName, "ordinary.jpg"), [1]);
        var pictures = Directory.CreateDirectory(Path.Combine(_root.Path, "pictures"));
        await File.WriteAllBytesAsync(Path.Combine(pictures.FullName, "photos.lrcat"), [1]);
        var service = CreateService(
            defaultPicturesRoot: defaultPictures.FullName,
            totalEntryLimit: 1);

        var result = await service.DetectAsync(pictures.FullName, null);

        Assert.False(result.IsDetected);
    }

    [Fact]
    public async Task DetectAsync_RejectsRootsThatAreNotLocalFixedStorage()
    {
        var pictures = Directory.CreateDirectory(Path.Combine(_root.Path, "pictures"));
        await File.WriteAllBytesAsync(Path.Combine(pictures.FullName, "photos.lrcat"), [1]);
        var probed = false;
        var service = new LightroomDetectionService(
            isSupportedPlatform: true,
            defaultPicturesRoot: null,
            isLocalFixedPath: _ =>
            {
                probed = true;
                return false;
            });

        var result = await service.DetectAsync(pictures.FullName, null);

        Assert.True(probed);
        Assert.False(result.IsDetected);
    }

    [Fact]
    public async Task DetectAsync_SkipsReparsePointDescendants()
    {
        var pictures = Directory.CreateDirectory(Path.Combine(_root.Path, "pictures"));
        var outside = Directory.CreateDirectory(Path.Combine(_root.Path, "outside"));
        await File.WriteAllBytesAsync(Path.Combine(outside.FullName, "photos.lrcat"), [1]);
        var link = Path.Combine(pictures.FullName, "linked-lightroom");
        try
        {
            Directory.CreateSymbolicLink(link, outside.FullName);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or
                PlatformNotSupportedException)
        {
            Assert.Skip("This environment cannot create a directory symbolic link.");
        }
        var service = CreateService();

        var result = await service.DetectAsync(pictures.FullName, null);

        Assert.False(result.IsDetected);
    }

    [Fact]
    public async Task DetectAsync_RecognizesKnownInstallRootWithoutCatalog()
    {
        var adobe = Directory.CreateDirectory(Path.Combine(_root.Path, "Adobe"));
        Directory.CreateDirectory(Path.Combine(adobe.FullName, "Adobe Lightroom Classic"));
        var service = new LightroomDetectionService(
            isSupportedPlatform: true,
            defaultPicturesRoot: null,
            isLocalFixedPath: _ => true,
            installRoots: [adobe.FullName]);

        var result = await service.DetectAsync(null, null);

        Assert.True(result.IsDetected);
        Assert.Empty(result.CatalogPaths);
    }

    [Fact]
    public async Task DetectAsync_IsDisabledOutsideSupportedPlatforms()
    {
        var pictures = Directory.CreateDirectory(Path.Combine(_root.Path, "pictures"));
        await File.WriteAllBytesAsync(Path.Combine(pictures.FullName, "photos.lrcat"), [1]);
        var service = new LightroomDetectionService(
            isSupportedPlatform: false,
            defaultPicturesRoot: pictures.FullName,
            isLocalFixedPath: _ => true);

        var result = await service.DetectAsync(pictures.FullName, null);

        Assert.False(result.IsDetected);
    }

    [Fact]
    public async Task DetectAsync_ReportsAtMostFiveCatalogCandidates()
    {
        var pictures = Directory.CreateDirectory(Path.Combine(_root.Path, "pictures"));
        for (var index = 0; index < 7; index++)
        {
            await File.WriteAllBytesAsync(
                Path.Combine(pictures.FullName, $"catalog-{index}.lrcat"),
                [1]);
        }
        var service = CreateService();

        var result = await service.DetectAsync(pictures.FullName, null);

        Assert.True(result.IsDetected);
        Assert.Equal(5, result.CatalogPaths.Count);
        Assert.All(
            result.CatalogPaths,
            path => Assert.Equal(".lrcat", Path.GetExtension(path)));
    }

    private LightroomDetectionService CreateService(
        string? defaultPicturesRoot = null,
        int maxDepth = 2,
        int entryLimit = 256,
        int totalEntryLimit = 4096) =>
        new(
            isSupportedPlatform: true,
            defaultPicturesRoot,
            isLocalFixedPath: _ => true,
            maxDepth: maxDepth,
            entryLimit: entryLimit,
            totalEntryLimit: totalEntryLimit);

    public void Dispose() => _root.Dispose();
}
