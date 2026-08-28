using Avalonia;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class CompareViewTests : IDisposable
{
    private readonly CatalogVmFixture _fx = new("compare");

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(2, true)]
    [InlineData(4, true)]
    [InlineData(5, false)]
    public async Task EntryGate_RequiresTwoToFourSelected(
        int selectedCount,
        bool expected)
    {
        using var catalog = await _fx.CreateUniqueCatalogAsync();
        await using var vm = CreateViewModel(catalog);
        var images = CreateImages(5);
        vm.Browse.SetImages(images);
        vm.SelectedImage = images[0];
        foreach (var image in images.Take(selectedCount))
            vm.ToggleImageSelection(image);

        Assert.Equal(expected, vm.CanEnterCompare);
        Assert.Equal(expected, vm.EnterCompareCommand.CanExecute(null));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public async Task Entry_SnapshotsMembership(int count)
    {
        using var catalog = await _fx.CreateUniqueCatalogAsync();
        await using var vm = CreateViewModel(catalog);
        var images = CreateImages(count);
        vm.Browse.SetImages(images);
        foreach (var image in images) vm.ToggleImageSelection(image);
        vm.SelectedImage = images[^1];

        vm.EnterCompareCommand.Execute(null);

        Assert.True(vm.IsBrowseMode);
        Assert.True(vm.IsCompareMode);
        Assert.Equal(images, vm.ComparePanes.Select(pane => pane.Image));
        Assert.Same(images[^1], vm.SelectedImage);
        Assert.Single(images, image => image.IsActive);
    }

    [Fact]
    public async Task Assessments_TargetPinnedActivePaneAcrossFilterRefresh()
    {
        using var catalog = await _fx.CreateUniqueCatalogAsync();
        await using var vm = CreateViewModel(catalog);
        var images = await CreateCatalogImagesAsync(catalog, 4);
        foreach (var image in images)
        {
            image.Flag = ImageFlag.Picked;
            await catalog.SaveFlagStateAsync(image.CatalogId, image.Flag);
        }
        vm.Browse.SetImages(images);
        vm.Browse.FlagFilter = FlagFilter.Picked;
        foreach (var image in images) vm.ToggleImageSelection(image);
        vm.SelectedImage = images[1];
        vm.EnterCompareCommand.Execute(null);

        await vm.ToggleRejectedImageCommand.ExecuteAsync(null);
        await vm.SetRatingCommand.ExecuteAsync(4);
        await vm.SetColorLabelCommand.ExecuteAsync(ColorLabel.Green);

        Assert.Equal(4, vm.ComparePanes.Count);
        Assert.Same(images[1], vm.SelectedImage);
        Assert.False(vm.Browse.ContainsVisible(images[1]));
        Assert.Equal(ImageFlag.Rejected, images[1].Flag);
        Assert.Equal(4, images[1].Rating);
        Assert.Equal(ColorLabel.Green, images[1].ColorLabel);
        Assert.All(images.Where(image => !ReferenceEquals(image, images[1])), image =>
        {
            Assert.Equal(ImageFlag.Picked, image.Flag);
            Assert.Equal(0, image.Rating);
            Assert.Equal(ColorLabel.None, image.ColorLabel);
        });

        vm.ExitCompareCommand.Execute(null);
        Assert.False(vm.IsCompareMode);
        Assert.All(images, image => Assert.True(image.IsSelected));
        Assert.Same(images[1], vm.SelectedImage);
    }

    [Fact]
    public async Task Navigation_UsesSnapshotWhenActivePaneIsFilteredOut()
    {
        using var catalog = await _fx.CreateUniqueCatalogAsync();
        await using var vm = CreateViewModel(catalog);
        var images = await CreateCatalogImagesAsync(catalog, 3);
        foreach (var image in images)
        {
            image.Flag = ImageFlag.Picked;
            await catalog.SaveFlagStateAsync(image.CatalogId, image.Flag);
        }
        vm.Browse.SetImages(images);
        vm.Browse.FlagFilter = FlagFilter.Picked;
        foreach (var image in images) vm.ToggleImageSelection(image);
        vm.SelectedImage = images[1];
        vm.EnterCompareCommand.Execute(null);
        await vm.ToggleRejectedImageCommand.ExecuteAsync(null);

        Assert.True(vm.SelectPreviousImageCommand.CanExecute(null));
        Assert.True(vm.SelectNextImageCommand.CanExecute(null));
        vm.SelectNextImageCommand.Execute(null);
        Assert.Same(images[2], vm.SelectedImage);
        vm.SelectNextImageCommand.Execute(null);
        Assert.Same(images[2], vm.SelectedImage);
        vm.SelectPreviousImageCommand.Execute(null);
        Assert.Same(images[1], vm.SelectedImage);
        Assert.Equal(3, vm.ComparePanes.Count);
    }

    [Fact]
    public async Task DestructiveAndFileCommands_HaveNoCompareTargets()
    {
        using var catalog = await _fx.CreateUniqueCatalogAsync();
        await using var vm = CreateViewModel(catalog);
        var images = await CreateCatalogImagesAsync(catalog, 2);
        vm.Browse.SetImages(images);
        foreach (var image in images) vm.ToggleImageSelection(image);
        vm.SelectedImage = images[0];
        var confirmations = 0;
        var copies = 0;
        vm.ConfirmDeleteAsync = _ =>
        {
            confirmations++;
            return Task.FromResult(true);
        };
        vm.ConfirmDeleteRejectedAsync = (_, _, _) =>
        {
            confirmations++;
            return Task.FromResult(true);
        };
        vm.CopyToClipboardAsync = _ =>
        {
            copies++;
            return Task.CompletedTask;
        };
        vm.EnterCompareCommand.Execute(null);

        await vm.DeleteImageCommand.ExecuteAsync(null);
        await vm.DeleteRejectedImagesCommand.ExecuteAsync(null);
        await vm.CopyImagePathsCommand.ExecuteAsync(null);

        Assert.Equal(0, confirmations);
        Assert.Equal(0, copies);
        Assert.All(images, image => Assert.True(vm.Browse.Contains(image)));
    }

    [Theory]
    [InlineData(6000, 4000, 3000, 6000)]
    [InlineData(4000, 6000, 8000, 3000)]
    public void SyncMath_MapsSameContentPointAcrossMixedAspects(
        double sourceWidth,
        double sourceHeight,
        double targetWidth,
        double targetHeight)
    {
        var normalized = SynchronizedViewMath.NormalizePoint(
            new ImagePoint(sourceWidth * 0.23, sourceHeight * 0.71),
            sourceWidth,
            sourceHeight);
        var mapped = SynchronizedViewMath.MapPoint(
            normalized,
            targetWidth,
            targetHeight);

        Assert.Equal(targetWidth * 0.23, mapped.X, 8);
        Assert.Equal(targetHeight * 0.71, mapped.Y, 8);
    }

    [Fact]
    public void Service_ClampsAndSuppressesEquivalentUpdates()
    {
        var service = new SynchronizedViewService();
        var notifications = 0;
        service.ViewportChanged += (_, _) => notifications++;

        service.SetViewport(new NormalizedViewport(
            new NormalizedPoint(-2, 4),
            2.5));
        service.SetViewport(new NormalizedViewport(
            new NormalizedPoint(0, 1),
            2.5));

        Assert.Equal(new NormalizedPoint(0, 1), service.Viewport.Center);
        Assert.Equal(2.5, service.Viewport.ZoomRelativeToFit);
        Assert.Equal(1, notifications);
    }

    [Fact]
    public void Service_ConstrainsCenterToEveryPanesRepresentableRange()
    {
        var service = new SynchronizedViewService();
        service.SetViewport(
            new NormalizedViewport(new NormalizedPoint(0.8, 0.2), 1.1),
            [
                new NormalizedCenterBounds(0.1, 0.9, 0.2, 0.8),
                new NormalizedCenterBounds(0.5, 0.5, 0.1, 0.9)
            ]);

        Assert.Equal(new NormalizedPoint(0.5, 0.2), service.Viewport.Center);
    }

    [Fact]
    public async Task LoupeRefinement_UsesOriginalDimensionsAndSerializesFullRenders()
    {
        using var catalog = await _fx.CreateUniqueCatalogAsync();
        var loader = new TrackingFullBaseLoader();
        await using var vm = _fx.CreateViewModel(
            catalog,
            loader,
            _ => Task.CompletedTask);
        var images = CreateImages(2);
        foreach (var image in images)
        {
            TestImages.WriteJpeg(
                image.FilePath,
                MagickColors.DarkSlateGray,
                1800,
                1200);
            image.ApplyMetadata(new ImageMetadata
            {
                PixelWidth = 1800,
                PixelHeight = 1200
            });
        }
        vm.Browse.SetImages(images);
        foreach (var image in images) vm.ToggleImageSelection(image);
        vm.SelectedImage = images[0];

        vm.EnterCompareCommand.Execute(null);
        await vm.CompareLoadingTask.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.All(vm.ComparePanes, pane =>
        {
            Assert.Equal(new PixelSize(1800, 1200), pane.OriginalViewPixelSize);
            Assert.Equal(1600, pane.RenderedLongEdge);
            vm.PublishCompareRequiredDeviceLongEdge(
                pane,
                1800,
                isLoupePeekActive: true);
        });
        await vm.CompareLoadingTask.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(2, loader.FullLoadCount);
        Assert.Equal(1, loader.MaximumConcurrentFullLoads);
        Assert.All(vm.ComparePanes, pane => Assert.Equal(1800, pane.RenderedLongEdge));
        foreach (var pane in vm.ComparePanes)
            vm.PublishCompareRequiredDeviceLongEdge(
                pane,
                1700,
                isLoupePeekActive: false);
        Assert.All(vm.ComparePanes, pane =>
        {
            Assert.Equal(1600, pane.RenderedLongEdge);
            Assert.Equal(1600, Math.Max(
                pane.Preview!.PixelSize.Width,
                pane.Preview.PixelSize.Height));
        });
        Assert.Equal(2, loader.FullLoadCount);
        foreach (var pane in vm.ComparePanes)
            vm.PublishCompareRequiredDeviceLongEdge(
                pane,
                3600,
                isLoupePeekActive: true);
        await vm.CompareLoadingTask.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(4, loader.FullLoadCount);
        Assert.Equal(1, loader.MaximumConcurrentFullLoads);
        Assert.All(vm.ComparePanes, pane => Assert.Equal(1800, pane.RenderedLongEdge));
    }

    [Fact]
    public void LoadingMessage_MeansNothingToShowNotWorkInProgress()
    {
        var pane = new ComparePaneViewModel(
            new ImageFile(Path.Combine(Path.GetTempPath(), "pane.jpg")));
        Assert.True(pane.ShowLoadingMessage);

        using var bitmap = new Avalonia.Media.Imaging.WriteableBitmap(
            new PixelSize(2, 2),
            new Vector(96, 96),
            Avalonia.Platform.PixelFormat.Bgra8888,
            Avalonia.Platform.AlphaFormat.Premul);
        pane.Preview = bitmap;
        Assert.False(pane.ShowLoadingMessage);

        // The authoritative render is still running, but a painted pane must
        // not wear the label.
        Assert.True(pane.IsLoading);
        pane.IsLoading = false;
        Assert.False(pane.ShowLoadingMessage);

        // A pane whose load failed outright shows the thumbnail, not a
        // perpetual loading claim.
        pane.Preview = null;
        Assert.False(pane.ShowLoadingMessage);
    }

    public void Dispose() => _fx.Dispose();

    private MainWindowViewModel CreateViewModel(CatalogService catalog) =>
        _fx.CreateViewModel(
            catalog,
            new NullBaseLoader(),
            _ => Task.CompletedTask);

    private ImageFile[] CreateImages(int count) =>
        Enumerable.Range(0, count)
            .Select(index => new ImageFile(_fx.Path($"image-{index}.jpg")))
            .ToArray();

    private async Task<ImageFile[]> CreateCatalogImagesAsync(
        CatalogService catalog,
        int count)
    {
        var images = CreateImages(count);
        var states = await catalog.LoadOrCreateImageStatesAsync(
            images.Select(image => image.FilePath).ToArray());
        foreach (var image in images)
            image.CatalogId = states[image.FilePath].Single().CatalogId;
        return images;
    }

    private sealed class TrackingFullBaseLoader : IBaseImageLoader
    {
        private readonly StandardBaseLoader _inner = new();
        private int _active;
        private int _maximum;
        private int _fullLoadCount;

        public int FullLoadCount => Volatile.Read(ref _fullLoadCount);
        public int MaximumConcurrentFullLoads => Volatile.Read(ref _maximum);

        public bool CanLoad(ImageFile file) => _inner.CanLoad(file);

        public BaseImageLoadOutcome LoadPreviewBaseWithOutcome(
            ImageFile file,
            BaseDecodeSettings decode,
            CancellationToken cancellationToken) =>
            _inner.LoadPreviewBaseWithOutcome(file, decode, cancellationToken);

        public BaseImage? LoadFullBase(
            ImageFile file,
            BaseDecodeSettings decode,
            CancellationToken cancellationToken)
        {
            var active = Interlocked.Increment(ref _active);
            Interlocked.Increment(ref _fullLoadCount);
            UpdateMaximum(active);
            try
            {
                return _inner.LoadFullBase(file, decode, cancellationToken);
            }
            finally
            {
                Interlocked.Decrement(ref _active);
            }
        }

        private void UpdateMaximum(int candidate)
        {
            var current = Volatile.Read(ref _maximum);
            while (candidate > current)
            {
                var observed = Interlocked.CompareExchange(
                    ref _maximum,
                    candidate,
                    current);
                if (observed == current) return;
                current = observed;
            }
        }
    }
}
