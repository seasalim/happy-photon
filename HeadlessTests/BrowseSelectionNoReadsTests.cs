using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

// Replaces BrowseReviewSummaryTests: the removed card's aggregates and loading
// lifecycle are no longer a contract. Selection must not scan photo metadata.
public sealed class BrowseSelectionNoReadsTests(ITestOutputHelper output)
{
    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task FiftyToggles_DoNotReadMetadata(bool resident)
    {
        using var fixture = new CatalogVmFixture("selection-no-reads");
        using var catalog = await fixture.CreateCatalogAsync();
        var metadataLoads = 0;
        await using var vm = fixture.CreateViewModel(catalog, new NullBaseLoader(), _ =>
        {
            Interlocked.Increment(ref metadataLoads);

            return Task.CompletedTask;
        }, new TestSourceAvailabilityService(SourceAvailability.AvailableLocally),
            timeProvider: new TestTimeProvider());
        var photos = Enumerable.Range(0, 10000)
            .Select(index => new ImageFile(fixture.Path($"photo-{index:D5}.jpg"))
            {
                MetadataLoaded = resident,
                FileSize = 1234,
                DateTaken = new DateTime(2026, 8, 1)
            }).ToArray();
        vm.ImageService.Previews.AdjacentWarmEnabled = false;
        vm.Browse.SetImages(photos);
        vm.SelectedImage = photos[0];
        vm.SelectAllCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        var status = new StatusBarView { DataContext = vm };
        using var scope = new TestUiScope(new Window { Content = status });
        var loadsBefore = metadataLoads;
        var sizeReads = 0;
        var dateReads = 0;
        var fileInfoReads = 0;

        foreach (var photo in photos)
        {
            photo.MetadataReadObserved = property =>
            {
                if (property == nameof(ImageFile.FileSize)) Interlocked.Increment(ref sizeReads);
                if (property == nameof(ImageFile.DateTaken)) Interlocked.Increment(ref dateReads);
                if (property == nameof(FileInfo)) Interlocked.Increment(ref fileInfoReads);
            };
        }

        for (var sample = 0; sample < 50; sample++)
        {
            vm.ToggleImageSelection(photos[1 + sample / 2]);
            var count = sample % 2 == 0 ? 9999 : 10000;
            Assert.Equal(count, vm.SelectedCount);
            Dispatcher.UIThread.RunJobs();
            Assert.Contains(status.GetVisualDescendants().OfType<TextBlock>(),
                text => text.Text == $"{vm.Browse.PhotoCountText} · {count} selected");
        }

        Dispatcher.UIThread.RunJobs();
        output.WriteLine($"metadata={metadataLoads - loadsBefore} fileInfo={fileInfoReads} " +
            $"residentSize={sizeReads} residentDate={dateReads}");
        Assert.Equal(0, metadataLoads - loadsBefore);
        Assert.Equal(0, fileInfoReads);
        Assert.Equal(0, sizeReads);
        Assert.Equal(0, dateReads);

        foreach (var photo in photos)
        {
            photo.MetadataReadObserved = null;
        }

        vm.ToggleFullScreenCommand.Execute(null);
        Assert.Equal("Selection · 1 / 10000", vm.FullScreenSelectionBadgeText);
    }

    [AvaloniaFact]
    public async Task ReadCounters_ObserveResidentPropertiesAndFileInfoPath()
    {
        using var fixture = new CatalogVmFixture("selection-counter-sensitivity");
        using var catalog = await fixture.CreateCatalogAsync();
        await using var vm = fixture.CreateViewModel(catalog);
        var image = new ImageFile(fixture.Path("missing.jpg"));
        var reads = new List<string>();
        image.MetadataReadObserved = reads.Add;
        _ = image.FileSize;
        _ = image.DateTaken;
        await vm.ImageService.Metadata.LoadAsync(image);

        Assert.Equal([nameof(ImageFile.FileSize), nameof(ImageFile.DateTaken), nameof(FileInfo)], reads);
    }
}
