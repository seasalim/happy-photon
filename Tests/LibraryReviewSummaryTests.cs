using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class LibraryReviewSummaryTests : IDisposable
{
    private readonly string _root = Directory.CreateDirectory(Path.Combine(
        Path.GetTempPath(),
        $"happy-photon-library-review-{Guid.NewGuid():N}")).FullName;

    [Fact]
    public async Task PreloadedMetadata_AggregatesCountDatesAndSize()
    {
        using var catalog = CreateCatalog("preloaded-catalog");
        await using var vm = CreateViewModel(catalog, _ => Task.CompletedTask);
        var first = CreateImage("first.jpg", 1_000, new DateTime(2026, 8, 1));
        var missingDate = CreateImage("second.jpg", 2_000, null);
        var last = CreateImage("third.jpg", 3_000, new DateTime(2026, 8, 9));
        vm.Library.SetImages([first, missingDate, last]);

        vm.ToggleImageSelection(first);
        vm.ToggleImageSelection(missingDate);
        vm.ToggleImageSelection(last);

        Assert.Equal(3, vm.LibrarySelectionCount);
        Assert.Equal(6_000, vm.LibrarySelectionCombinedFileSize);
        Assert.Equal(new DateTime(2026, 8, 1), vm.LibrarySelectionEarliestDate);
        Assert.Equal(new DateTime(2026, 8, 9), vm.LibrarySelectionLatestDate);
        await vm.WaitForLibrarySelectionSummaryAsync();
        Assert.False(vm.IsLibrarySelectionSummaryLoading);
    }

    [Fact]
    public async Task MissingMetadata_FillsSeriallyAndPublishesNewestSelection()
    {
        using var catalog = CreateCatalog("delayed-catalog");
        var firstLoadStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirstLoad = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var loadedNames = new List<string>();
        await using var vm = CreateViewModel(catalog, async image =>
        {
            lock (loadedNames) loadedNames.Add(image.FileName);
            if (image.FileName == "old-a.jpg")
            {
                firstLoadStarted.TrySetResult();
                await releaseFirstLoad.Task;
            }

            var offset = image.FileName.StartsWith("new", StringComparison.Ordinal)
                ? 10
                : 1;
            image.ApplyMetadata(new ImageMetadata
            {
                FileSize = offset * 1_000,
                DateTaken = new DateTime(2026, 8, offset)
            });
        });
        var oldA = new ImageFile(Path.Combine(_root, "old-a.jpg"));
        var oldB = new ImageFile(Path.Combine(_root, "old-b.jpg"));
        var newA = new ImageFile(Path.Combine(_root, "new-a.jpg"));
        var newB = new ImageFile(Path.Combine(_root, "new-b.jpg"));
        vm.Library.SetImages([oldA, oldB, newA, newB]);

        vm.ToggleImageSelection(oldA);
        vm.ToggleImageSelection(oldB);
        await firstLoadStarted.Task;
        Assert.Equal(2, vm.LibrarySelectionCount);
        Assert.True(vm.IsLibrarySelectionSummaryLoading);

        vm.ToggleImageSelection(oldA);
        vm.ToggleImageSelection(oldB);
        vm.ToggleImageSelection(newA);
        vm.ToggleImageSelection(newB);
        releaseFirstLoad.TrySetResult();
        await vm.WaitForLibrarySelectionSummaryAsync();

        Assert.Equal(2, vm.LibrarySelectionCount);
        Assert.Equal(20_000, vm.LibrarySelectionCombinedFileSize);
        Assert.Equal(new DateTime(2026, 8, 10), vm.LibrarySelectionEarliestDate);
        Assert.Equal(new DateTime(2026, 8, 10), vm.LibrarySelectionLatestDate);
        Assert.Equal(["old-a.jpg", "new-a.jpg", "new-b.jpg"], loadedNames);
        Assert.False(vm.IsLibrarySelectionSummaryLoading);
    }

    [Fact]
    public async Task OnlineOnlyMembers_AreCountedButExcludedFromAggregates()
    {
        using var catalog = CreateCatalog("online-catalog");
        var availability = new TestSourceAvailabilityService(
            SourceAvailability.AvailableLocally)
        {
            Resolver = path => Path.GetFileName(path) == "cloud.jpg"
                ? SourceAvailability.RequiresHydration
                : SourceAvailability.AvailableLocally
        };
        await using var vm = CreateViewModel(
            catalog,
            _ => Task.CompletedTask,
            availability);
        var local = CreateImage("local.jpg", 4_000, new DateTime(2026, 7, 3));
        var cloud = CreateImage("cloud.jpg", 9_000, new DateTime(2026, 6, 1));
        vm.Library.SetImages([local, cloud]);

        vm.ToggleImageSelection(local);
        vm.ToggleImageSelection(cloud);
        await vm.WaitForLibrarySelectionSummaryAsync();

        Assert.Equal(2, vm.LibrarySelectionCount);
        Assert.Equal(1, vm.LibrarySelectionOnlineOnlyCount);
        Assert.Equal(4_000, vm.LibrarySelectionCombinedFileSize);
        Assert.Equal(new DateTime(2026, 7, 3), vm.LibrarySelectionEarliestDate);
        Assert.True(cloud.SourceRequiresHydration);
    }

    [Fact]
    public async Task SameCountLibraryReplacement_InvalidatesPublishedSummary()
    {
        using var catalog = CreateCatalog("replacement-catalog");
        await using var vm = CreateViewModel(catalog, _ => Task.CompletedTask);
        var oldA = CreateImage("old-a.jpg", 1_000, new DateTime(2026, 1, 1));
        var oldB = CreateImage("old-b.jpg", 2_000, new DateTime(2026, 1, 2));
        vm.Library.SetImages([oldA, oldB]);
        vm.ToggleImageSelection(oldA);
        vm.ToggleImageSelection(oldB);
        await vm.WaitForLibrarySelectionSummaryAsync();
        Assert.Equal(3_000, vm.LibrarySelectionCombinedFileSize);

        var newA = CreateImage("new-a.jpg", 4_000, new DateTime(2026, 2, 1));
        var newB = CreateImage("new-b.jpg", 5_000, new DateTime(2026, 2, 2));
        newA.IsSelected = true;
        newB.IsSelected = true;

        vm.Library.SetImages([newA, newB]);
        await vm.WaitForLibrarySelectionSummaryAsync();

        Assert.Equal(2, vm.LibrarySelectionCount);
        Assert.Equal(9_000, vm.LibrarySelectionCombinedFileSize);
        Assert.Equal(new DateTime(2026, 2, 1), vm.LibrarySelectionEarliestDate);
        Assert.Equal(new DateTime(2026, 2, 2), vm.LibrarySelectionLatestDate);
    }

    [Fact]
    public async Task FolderReplacement_WaitsForCanceledSummaryAndClearsCount()
    {
        using var catalog = CreateCatalog("folder-catalog");
        var loadStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseLoad = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        await using var vm = CreateViewModel(catalog, async image =>
        {
            loadStarted.TrySetResult();
            await releaseLoad.Task;
            image.ApplyMetadata(new ImageMetadata { FileSize = 100 });
        });
        var first = new ImageFile(Path.Combine(_root, "first.jpg"));
        var second = new ImageFile(Path.Combine(_root, "second.jpg"));
        vm.Library.SetImages([first, second]);
        vm.ToggleImageSelection(first);
        vm.ToggleImageSelection(second);
        await loadStarted.Task;

        var replacement = Directory.CreateDirectory(
            Path.Combine(_root, "replacement")).FullName;
        var folderLoad = vm.LoadFolderAsync(replacement);
        await Task.Delay(50);
        Assert.False(folderLoad.IsCompleted);
        Assert.Equal(0, vm.LibrarySelectionCount);

        releaseLoad.TrySetResult();
        await folderLoad;

        Assert.Equal(0, vm.LibrarySelectionCount);
        Assert.False(vm.HasLibrarySelectionSummary);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private CatalogService CreateCatalog(string name)
    {
        var catalog = new CatalogService(Path.Combine(_root, name));
        catalog.InitializeAsync().GetAwaiter().GetResult();
        return catalog;
    }

    private MainWindowViewModel CreateViewModel(
        CatalogService catalog,
        Func<ImageFile, Task> loadMetadataAsync,
        ISourceAvailabilityService? availability = null) =>
        new(
            catalog,
            baseLoader: null,
            loadMetadataAsync,
            availability ?? new TestSourceAvailabilityService(
                SourceAvailability.AvailableLocally),
            postSelection: _ => { });

    private ImageFile CreateImage(
        string name,
        long fileSize,
        DateTime? dateTaken)
    {
        var image = new ImageFile(Path.Combine(_root, name));
        image.ApplyMetadata(new ImageMetadata
        {
            FileSize = fileSize,
            DateTaken = dateTaken
        });
        return image;
    }
}
