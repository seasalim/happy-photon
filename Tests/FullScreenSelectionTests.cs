using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class FullScreenSelectionTests : IDisposable
{
    private readonly CatalogVmFixture _fx = new("fullscreen-selection");
    private readonly CatalogService _catalog;

    public FullScreenSelectionTests()
    {
        _catalog = _fx.CreateCatalog("catalog");
        _catalog.InitializeAsync().GetAwaiter().GetResult();
    }

    [Fact]
    public async Task ScopedNavigation_UsesLibraryOrderAndClampedCompactOffsets()
    {
        await using var vm = CreateViewModel();
        var images = CreateImages(6);
        vm.Library.SetImages(images);
        vm.ToggleImageSelection(images[4]);
        vm.ToggleImageSelection(images[0]);
        vm.ToggleImageSelection(images[2]);
        vm.SelectedImage = images[2];

        vm.ToggleFullScreenCommand.Execute(null);

        Assert.True(vm.IsFullScreenSelectionRestricted);
        vm.SelectFirstImage();
        Assert.Same(images[0], vm.SelectedImage);
        vm.SelectPreviousImageCommand.Execute(null);
        Assert.Same(images[0], vm.SelectedImage);
        vm.SelectNextImageCommand.Execute(null);
        Assert.Same(images[2], vm.SelectedImage);
        vm.SelectNextImageCommand.Execute(null);
        Assert.Same(images[4], vm.SelectedImage);
        vm.SelectNextImageCommand.Execute(null);
        Assert.Same(images[4], vm.SelectedImage);

        vm.SelectImageUp(1);
        Assert.Same(images[2], vm.SelectedImage);
        vm.SelectImageUp(4);
        Assert.Same(images[0], vm.SelectedImage);
        vm.SelectImageDown(1);
        Assert.Same(images[2], vm.SelectedImage);
        vm.SelectImageDown(10);
        Assert.Same(images[4], vm.SelectedImage);
        vm.SelectLastImage();
        Assert.Same(images[4], vm.SelectedImage);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task ZeroOrOneSelected_UsesFullFolderNavigation(int selectedCount)
    {
        await using var vm = CreateViewModel();
        var images = CreateImages(3);
        vm.Library.SetImages(images);
        vm.SelectedImage = images[1];
        if (selectedCount == 1)
        {
            vm.ToggleImageSelection(images[1]);
        }

        vm.ToggleFullScreenCommand.Execute(null);
        vm.SelectNextImageCommand.Execute(null);

        Assert.False(vm.IsFullScreenSelectionRestricted);
        Assert.Same(images[2], vm.SelectedImage);
    }

    [Fact]
    public async Task Entry_AnchorsFirstSelectedMember()
    {
        await using var vm = CreateViewModel();
        var images = CreateImages(5);
        vm.Library.SetImages(images);
        vm.ToggleImageSelection(images[1]);
        vm.ToggleImageSelection(images[3]);
        vm.SelectedImage = images[3];

        vm.ToggleFullScreenCommand.Execute(null);

        Assert.Same(images[1], vm.SelectedImage);
        Assert.Equal("SELECTION · 1 / 2", vm.FullScreenSelectionBadgeText);
    }

    [Fact]
    public async Task ReanchoredEntry_StartsOnePreviewLoad()
    {
        var loader = new CountingBaseLoader();
        await using var vm = CreateViewModel(
            loader,
            new TestSourceAvailabilityService(
                SourceAvailability.AvailableLocally));
        var images = CreateImages(3);
        foreach (var image in images)
        {
            File.WriteAllBytes(image.FilePath, [1]);
        }
        vm.Library.SetImages(images);
        vm.ToggleImageSelection(images[0]);
        vm.ToggleImageSelection(images[2]);
        vm.SelectedImage = images[1];

        vm.ToggleFullScreenCommand.Execute(null);

        try
        {
            // Wait generously for the first load to START; exactly-one is
            // enforced by the Assert.Equal below, not by this wait.
            Assert.True(SpinWait.SpinUntil(
                () => loader.PreviewLoadCount >= 1,
                TestWaits.Condition));
            await Task.Delay(100);
            Assert.Equal(1, loader.PreviewLoadCount);
        }
        finally
        {
            loader.ReleasePreviewLoads();
        }
    }

    [Fact]
    public async Task LiveSelectionChangesExpandReanchorAndReleaseUntilReentry()
    {
        await using var vm = CreateViewModel();
        var images = CreateImages(4);
        vm.Library.SetImages(images);
        vm.ToggleImageSelection(images[0]);
        vm.ToggleImageSelection(images[2]);
        vm.SelectedImage = images[0];
        vm.ToggleFullScreenCommand.Execute(null);

        vm.ToggleImageSelection(images[1]);
        Assert.Equal("SELECTION · 1 / 3", vm.FullScreenSelectionBadgeText);

        vm.ToggleImageSelection(images[0]);
        Assert.True(vm.IsFullScreenSelectionRestricted);
        Assert.Same(images[1], vm.SelectedImage);
        Assert.Equal("SELECTION · 1 / 2", vm.FullScreenSelectionBadgeText);

        vm.ToggleImageSelection(images[2]);
        Assert.False(vm.IsFullScreenSelectionRestricted);
        vm.ToggleImageSelection(images[3]);
        Assert.False(vm.IsFullScreenSelectionRestricted);
        vm.SelectNextImageCommand.Execute(null);
        Assert.Same(images[2], vm.SelectedImage);

        vm.ToggleFullScreenCommand.Execute(null);
        vm.ToggleFullScreenCommand.Execute(null);
        Assert.True(vm.IsFullScreenSelectionRestricted);
        Assert.Same(images[1], vm.SelectedImage);

        vm.ToggleFullScreenCommand.Execute(null);
        Assert.False(vm.IsFullScreenSelectionRestricted);
    }

    [Fact]
    public async Task FilterAndRemoval_ReconcileVisibleSelectionImmediately()
    {
        await using var vm = CreateViewModel();
        var images = CreateImages(4);
        images[2].Flag = ImageFlag.Picked;
        images[3].Flag = ImageFlag.Picked;
        vm.Library.SetImages(images);
        vm.ToggleImageSelection(images[0]);
        vm.ToggleImageSelection(images[2]);
        vm.ToggleImageSelection(images[3]);
        vm.SelectedImage = images[0];
        vm.ToggleFullScreenCommand.Execute(null);

        vm.Library.FlagFilter = FlagFilter.Picked;

        Assert.True(vm.IsFullScreenSelectionRestricted);
        Assert.Same(images[2], vm.SelectedImage);
        Assert.Equal("SELECTION · 1 / 2", vm.FullScreenSelectionBadgeText);

        vm.Library.Remove(images[2]);

        Assert.False(vm.IsFullScreenSelectionRestricted);
    }

    [Fact]
    public async Task SetImages_KeepsRetainedMembersButReleasesForReplacement()
    {
        await using var vm = CreateViewModel();
        var images = CreateImages(3);
        vm.Library.SetImages(images);
        vm.ToggleImageSelection(images[0]);
        vm.ToggleImageSelection(images[1]);
        vm.SelectedImage = images[0];
        vm.ToggleFullScreenCommand.Execute(null);

        var inserted = CreateImage("inserted.jpg");
        vm.Library.SetImages([inserted, images[0], images[1]]);

        Assert.True(vm.IsFullScreenSelectionRestricted);
        Assert.Equal("SELECTION · 1 / 2", vm.FullScreenSelectionBadgeText);

        vm.Library.SetImages(CreateImages(3, "replacement"));

        Assert.True(vm.IsFullScreenMode);
        Assert.False(vm.IsFullScreenSelectionRestricted);
    }

    [Fact]
    public async Task FolderLoad_ReleasesScopeAndExitsFullScreen()
    {
        await using var vm = CreateViewModel();
        var images = CreateImages(2);
        vm.Library.SetImages(images);
        vm.ToggleImageSelection(images[0]);
        vm.ToggleImageSelection(images[1]);
        vm.SelectedImage = images[0];
        vm.ToggleFullScreenCommand.Execute(null);
        var replacementFolder = Directory.CreateDirectory(
            _fx.Path("replacement-folder")).FullName;

        await vm.LoadFolderAsync(replacementFolder);

        Assert.False(vm.IsFullScreenMode);
        Assert.False(vm.IsFullScreenSelectionRestricted);
        Assert.Empty(vm.Library.VisibleImages);
    }

    private MainWindowViewModel CreateViewModel() =>
        CreateViewModel(new NullBaseLoader());

    private MainWindowViewModel CreateViewModel(
        IBaseImageLoader loader,
        ISourceAvailabilityService? availabilityService = null) =>
        _fx.CreateViewModel(
            _catalog,
            loader,
            loadMetadataAsync: _ => Task.CompletedTask,
            availabilityService: availabilityService);

    private List<ImageFile> CreateImages(int count, string prefix = "image") =>
        Enumerable.Range(0, count)
            .Select(index => CreateImage($"{prefix}-{index}.jpg"))
            .ToList();

    private ImageFile CreateImage(string name) =>
        new(_fx.Path(name));

    public void Dispose()
    {
        _catalog.Dispose();
        _fx.Dispose();
    }

    private sealed class CountingBaseLoader : IBaseImageLoader
    {
        private int _previewLoadCount;
        private readonly ManualResetEventSlim _releasePreviewLoads = new();

        public int PreviewLoadCount => Volatile.Read(ref _previewLoadCount);

        public bool CanLoad(ImageFile file) => true;

        BaseImageLoadOutcome IBaseImageLoader.LoadPreviewBaseWithOutcome(ImageFile file, BaseDecodeSettings decode, CancellationToken cancellationToken) => BaseImageLoadOutcome.FromImage(LoadPreviewBase(file, decode, cancellationToken), BaseImageLoadFailure.DecodeFailed);

        public void ReleasePreviewLoads() => _releasePreviewLoads.Set();

        public BaseImage? LoadPreviewBase(
            ImageFile file,
            BaseDecodeSettings decode,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _previewLoadCount);
            _releasePreviewLoads.Wait(cancellationToken);
            return null;
        }

        public BaseImage? LoadFullBase(
            ImageFile file,
            BaseDecodeSettings decode,
            CancellationToken cancellationToken) => null;
    }
}
