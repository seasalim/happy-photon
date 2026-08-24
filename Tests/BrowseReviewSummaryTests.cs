using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class BrowseReviewSummaryTests : IDisposable
{
    private readonly CatalogVmFixture _fx = new("browse-review");

    [Fact]
    public async Task PreloadedMetadata_AggregatesCountDatesAndSize()
    {
        using var catalog = CreateCatalog("preloaded-catalog");
        await using var vm = CreateViewModel(catalog, _ => Task.CompletedTask);
        var first = CreateImage("first.jpg", 1_000, new DateTime(2026, 8, 1));
        var missingDate = CreateImage("second.jpg", 2_000, null);
        var last = CreateImage("third.jpg", 3_000, new DateTime(2026, 8, 9));
        vm.Browse.SetImages([first, missingDate, last]);

        vm.ToggleImageSelection(first);
        vm.ToggleImageSelection(missingDate);
        vm.ToggleImageSelection(last);

        Assert.Equal(3, vm.BrowseSelectionCount);
        Assert.Equal(6_000, vm.BrowseSelectionCombinedFileSize);
        Assert.Equal(new DateTime(2026, 8, 1), vm.BrowseSelectionEarliestDate);
        Assert.Equal(new DateTime(2026, 8, 9), vm.BrowseSelectionLatestDate);
        await vm.WaitForBrowseSelectionSummaryAsync();
        Assert.False(vm.IsBrowseSelectionSummaryLoading);
    }

    [Fact]
    public async Task FileModifiedFallback_IsExcludedFromSelectionDateRange()
    {
        using var catalog = CreateCatalog("modified-fallback-catalog");
        await using var vm = CreateViewModel(catalog, _ => Task.CompletedTask);
        var captured = CreateImage(
            "captured.jpg",
            1_000,
            new DateTime(2026, 8, 1));
        var modifiedOnly = new ImageFile(_fx.Path("modified.jpg"));
        modifiedOnly.ApplyMetadata(new ImageMetadata
        {
            FileSize = 2_000,
            FileModifiedDate = new DateTime(2026, 8, 14)
        });
        vm.Browse.SetImages([captured, modifiedOnly]);

        vm.ToggleImageSelection(captured);
        vm.ToggleImageSelection(modifiedOnly);
        await vm.WaitForBrowseSelectionSummaryAsync();

        Assert.Equal(new DateTime(2026, 8, 1), vm.BrowseSelectionEarliestDate);
        Assert.Equal(new DateTime(2026, 8, 1), vm.BrowseSelectionLatestDate);
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
        var oldA = new ImageFile(_fx.Path("old-a.jpg"));
        var oldB = new ImageFile(_fx.Path("old-b.jpg"));
        var newA = new ImageFile(_fx.Path("new-a.jpg"));
        var newB = new ImageFile(_fx.Path("new-b.jpg"));
        vm.Browse.SetImages([oldA, oldB, newA, newB]);

        vm.ToggleImageSelection(oldA);
        vm.ToggleImageSelection(oldB);
        await firstLoadStarted.Task;
        Assert.Equal(2, vm.BrowseSelectionCount);
        Assert.True(vm.IsBrowseSelectionSummaryLoading);

        vm.ToggleImageSelection(oldA);
        vm.ToggleImageSelection(oldB);
        vm.ToggleImageSelection(newA);
        vm.ToggleImageSelection(newB);
        releaseFirstLoad.TrySetResult();
        await vm.WaitForBrowseSelectionSummaryAsync();

        Assert.Equal(2, vm.BrowseSelectionCount);
        Assert.Equal(20_000, vm.BrowseSelectionCombinedFileSize);
        Assert.Equal(new DateTime(2026, 8, 10), vm.BrowseSelectionEarliestDate);
        Assert.Equal(new DateTime(2026, 8, 10), vm.BrowseSelectionLatestDate);
        Assert.Equal(["old-a.jpg", "new-a.jpg", "new-b.jpg"], loadedNames);
        Assert.False(vm.IsBrowseSelectionSummaryLoading);
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
        vm.Browse.SetImages([local, cloud]);

        vm.ToggleImageSelection(local);
        vm.ToggleImageSelection(cloud);
        await vm.WaitForBrowseSelectionSummaryAsync();

        Assert.Equal(2, vm.BrowseSelectionCount);
        Assert.Equal(1, vm.BrowseSelectionOnlineOnlyCount);
        Assert.Equal(4_000, vm.BrowseSelectionCombinedFileSize);
        Assert.Equal(new DateTime(2026, 7, 3), vm.BrowseSelectionEarliestDate);
        Assert.True(cloud.SourceRequiresHydration);
    }

    [Fact]
    public async Task SameCountBrowseReplacement_InvalidatesPublishedSummary()
    {
        using var catalog = CreateCatalog("replacement-catalog");
        await using var vm = CreateViewModel(catalog, _ => Task.CompletedTask);
        var oldA = CreateImage("old-a.jpg", 1_000, new DateTime(2026, 1, 1));
        var oldB = CreateImage("old-b.jpg", 2_000, new DateTime(2026, 1, 2));
        vm.Browse.SetImages([oldA, oldB]);
        vm.ToggleImageSelection(oldA);
        vm.ToggleImageSelection(oldB);
        await vm.WaitForBrowseSelectionSummaryAsync();
        Assert.Equal(3_000, vm.BrowseSelectionCombinedFileSize);

        var newA = CreateImage("new-a.jpg", 4_000, new DateTime(2026, 2, 1));
        var newB = CreateImage("new-b.jpg", 5_000, new DateTime(2026, 2, 2));
        newA.IsSelected = true;
        newB.IsSelected = true;

        vm.Browse.SetImages([newA, newB]);
        await vm.WaitForBrowseSelectionSummaryAsync();

        Assert.Equal(2, vm.BrowseSelectionCount);
        Assert.Equal(9_000, vm.BrowseSelectionCombinedFileSize);
        Assert.Equal(new DateTime(2026, 2, 1), vm.BrowseSelectionEarliestDate);
        Assert.Equal(new DateTime(2026, 2, 2), vm.BrowseSelectionLatestDate);
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
        var first = new ImageFile(_fx.Path("first.jpg"));
        var second = new ImageFile(_fx.Path("second.jpg"));
        vm.Browse.SetImages([first, second]);
        vm.ToggleImageSelection(first);
        vm.ToggleImageSelection(second);
        await loadStarted.Task;

        var replacement = Directory.CreateDirectory(
            _fx.Path("replacement")).FullName;
        var folderLoad = vm.LoadFolderAsync(replacement);
        // The load cannot finish until releaseLoad is set below, so this only
        // settles the state it asserts; it never races a deadline.
        await Task.Delay(50);
        Assert.False(folderLoad.IsCompleted);
        Assert.Equal(0, vm.BrowseSelectionCount);

        releaseLoad.TrySetResult();
        await folderLoad;

        Assert.Equal(0, vm.BrowseSelectionCount);
        Assert.False(vm.HasBrowseSelectionSummary);
    }

    public void Dispose() => _fx.Dispose();

    private CatalogService CreateCatalog(string name)
    {
        var catalog = _fx.CreateCatalog(name);
        catalog.InitializeAsync().GetAwaiter().GetResult();
        return catalog;
    }

    private MainWindowViewModel CreateViewModel(
        CatalogService catalog,
        Func<ImageFile, Task> loadMetadataAsync,
        ISourceAvailabilityService? availability = null) =>
        _fx.CreateViewModel(
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
        var image = new ImageFile(_fx.Path(name));
        image.ApplyMetadata(new ImageMetadata
        {
            FileSize = fileSize,
            DateTaken = dateTaken
        });
        return image;
    }
}
