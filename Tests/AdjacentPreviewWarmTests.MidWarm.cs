using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

public sealed partial class AdjacentPreviewWarmTests
{
    [WindowsTheory]
    [InlineData("loupe")]
    [InlineData("develop")]
    public async Task SteppingOntoTheImageBeingWarmedWaitsForThatDecodeInTheLoupeOnly(
        string surface)
    {
        _fixture.RequireWindows();
        using var catalog = await CreateCatalogAsync($"midwarm-{surface}");
        var images = new[]
        {
            await CreateCatalogImageAsync(catalog, "current.jpg"),
            await CreateCatalogImageAsync(catalog, "target.jpg"),
            await CreateCatalogImageAsync(catalog, "after.jpg")
        };
        var clock = new TestTimeProvider();
        var loader = new HeldDecodeLoader("target.jpg");
        var vm = new MainWindowViewModel(
            catalog,
            loader,
            loadMetadataAsync: _ => Task.CompletedTask,
            availabilityService: new TestSourceAvailabilityService(
                SourceAvailability.AvailableLocally),
            timeProvider: clock)
        {
            IsDevelopMode = surface == "develop"
        };
        vm.Browse.SetImages(images);
        try
        {
            vm.SelectedImage = images[0];
            if (surface == "loupe") vm.EnterLoupeCommand.Execute(null);
            await SettleAsync(vm, surface);
            clock.Advance(TimeSpan.FromMilliseconds(75));
            Assert.True(loader.Started.Wait(TestWaits.Condition));

            vm.SelectedImage = images[1];
            if (surface == "loupe")
            {
                await TestWaits.UntilAsync(() => vm.LoupePane?.Image == images[1]);
                Assert.Null(vm.LoupePane!.Preview);
                Assert.Equal(1, loader.Attempts);
                loader.Release.Set();
                await vm.LoupeLoadingTask;
                Assert.NotNull(vm.LoupePane?.Preview);
                Assert.Equal(1, loader.Attempts);
            }
            else
            {
                // Develop needs its own base for editing, so the warm is
                // cancelled and the foreground decode is the second attempt.
                await TestWaits.UntilAsync(() => loader.Attempts == 2);
                loader.Release.Set();
                await SettleAsync(vm, surface);
            }
            // A cancelled attempt never records, so both surfaces show one
            // completed decode of the target.
            Assert.Equal(1, loader.Paths.Count(name => name == "target.jpg"));
        }
        finally
        {
            await vm.DisposeAsync();
        }
    }

    [WindowsFact]
    public async Task AStalePreviewPaintsAtOnceWhileTheJoinedWarmFinishesWithoutASecondDecode()
    {
        _fixture.RequireWindows();
        using var catalog = await CreateCatalogAsync("loupe-stale-join");
        var images = new[]
        {
            await CreateCatalogImageAsync(catalog, "current.jpg"),
            await CreateCatalogImageAsync(catalog, "target.jpg")
        };
        // A settings-stale preview on disk, as an edit elsewhere would leave.
        var seed = new PreviewCacheService(catalog);
        using (var pixels = new MagickImage(MagickColors.Orange, 32, 24))
        {
            seed.QueueSaveToCache(images[1], pixels, "stale-settings");
        }
        await TestWaits.UntilAsync(() => seed.PendingWrites == 0);
        await seed.DisposeAsync();

        var clock = new TestTimeProvider();
        var loader = new HeldDecodeLoader("target.jpg");
        var vm = new MainWindowViewModel(
            catalog,
            loader,
            loadMetadataAsync: _ => Task.CompletedTask,
            availabilityService: new TestSourceAvailabilityService(
                SourceAvailability.AvailableLocally),
            timeProvider: clock);
        vm.Browse.SetImages(images);
        try
        {
            vm.SelectedImage = images[0];
            vm.EnterLoupeCommand.Execute(null);
            await vm.LoupeLoadingTask;
            clock.Advance(TimeSpan.FromMilliseconds(75));
            Assert.True(loader.Started.Wait(TestWaits.Condition));

            vm.SelectedImage = images[1];
            // The stale preview paints while the warm decode is still held.
            await TestWaits.UntilAsync(() =>
                vm.LoupePane?.Image == images[1] && vm.LoupePane.Preview != null);
            Assert.False(loader.Release.IsSet);
            Assert.Equal(1, loader.Attempts);

            loader.Release.Set();
            await vm.LoupeLoadingTask;
            Assert.Equal(1, loader.Attempts);
            Assert.Equal(1, loader.Paths.Count(name => name == "target.jpg"));
        }
        finally
        {
            await vm.DisposeAsync();
        }
    }

    [WindowsFact]
    public async Task DevelopShowsTheLoadingMessageOnlyWhileNothingHasPainted()
    {
        _fixture.RequireWindows();
        using var catalog = await CreateCatalogAsync("develop-loading-message");
        var image = await CreateCatalogImageAsync(catalog, "first.jpg");
        var clock = new TestTimeProvider();
        var loader = new HeldDecodeLoader("first.jpg");
        var vm = new MainWindowViewModel(
            catalog,
            loader,
            loadMetadataAsync: _ => Task.CompletedTask,
            availabilityService: new TestSourceAvailabilityService(
                SourceAvailability.AvailableLocally),
            timeProvider: clock)
        {
            IsDevelopMode = true
        };
        vm.Browse.SetImages([image]);
        try
        {
            Assert.False(vm.ShowDevelopLoadingMessage);
            vm.SelectedImage = image;
            Assert.True(loader.Started.Wait(TestWaits.Condition));
            Assert.Null(vm.PreviewImage);
            Assert.True(vm.ShowDevelopLoadingMessage);

            loader.Release.Set();
            await SettleAsync(vm, "develop");
            Assert.False(vm.ShowDevelopLoadingMessage);
        }
        finally
        {
            await vm.DisposeAsync();
        }
    }

    [WindowsFact]
    public async Task LoupeThumbnailLoadsKeepResidencyWithinTheBudget()
    {
        _fixture.RequireWindows();
        using var catalog = await CreateCatalogAsync("loupe-residency");
        var images = new List<ImageFile>();
        for (var index = 0; index < 5; index++)
            images.Add(await CreateCatalogImageAsync(catalog, $"big-{index}.jpg"));
        var path = Path.Combine(_root.Path, "real.jpg");
        using (var pixels = new MagickImage(MagickColors.Orange, 64, 48))
        {
            pixels.Write(path);
        }
        var target = new ImageFile(path);
        await target.EnsureCatalogIdAsync(catalog);
        images.Add(target);
        var clock = new TestTimeProvider();
        var vm = new MainWindowViewModel(
            catalog,
            new RecordingLoader(),
            loadMetadataAsync: _ => Task.CompletedTask,
            availabilityService: new TestSourceAvailabilityService(
                SourceAvailability.AvailableLocally),
            timeProvider: clock);
        vm.Browse.SetImages(images);
        try
        {
            // Five 16 MiB thumbnails already resident: over the 64 MiB budget.
            foreach (var big in images.Take(5))
            {
                vm.Browse.ReplaceThumbnail(big, new WriteableBitmap(
                    new PixelSize(2048, 2048),
                    new Vector(96, 96),
                    PixelFormat.Bgra8888,
                    AlphaFormat.Premul));
            }
            Assert.True(vm.ResidentThumbnailBytes > MainWindowViewModel.ThumbnailPixelBudget);

            vm.SelectedImage = target;
            vm.EnterLoupeCommand.Execute(null);
            await TestWaits.UntilAsync(() => target.Thumbnail != null);
            await TestWaits.UntilAsync(() =>
                vm.ResidentThumbnailBytes <= MainWindowViewModel.ThumbnailPixelBudget);
            Assert.NotNull(target.Thumbnail);
        }
        finally
        {
            await vm.DisposeAsync();
        }
    }

    [WindowsFact]
    public async Task RapidLoupeStepsNeverOverlapThumbnailSourceLoads()
    {
        _fixture.RequireWindows();
        using var catalog = await CreateCatalogAsync("loupe-thumbnail-chain");
        var images = new List<ImageFile>();
        for (var index = 0; index < 4; index++)
        {
            var path = Path.Combine(_root.Path, $"real-{index}.jpg");
            using (var pixels = new MagickImage(MagickColors.Orange, 64, 48))
            {
                pixels.Write(path);
            }
            var image = new ImageFile(path);
            await image.EnsureCatalogIdAsync(catalog);
            images.Add(image);
        }
        var clock = new TestTimeProvider();
        var vm = new MainWindowViewModel(
            catalog,
            new RecordingLoader(),
            loadMetadataAsync: _ => Task.CompletedTask,
            availabilityService: new TestSourceAvailabilityService(
                SourceAvailability.AvailableLocally),
            timeProvider: clock);
        vm.Browse.SetImages(images);
        var gate = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = 0;
        vm.ImageService.Thumbnails.SourceLoadGateAsync = () =>
        {
            Interlocked.Increment(ref entered);
            return gate.Task;
        };
        try
        {
            vm.SelectedImage = images[0];
            vm.EnterLoupeCommand.Execute(null);
            await TestWaits.UntilAsync(() => Volatile.Read(ref entered) == 1);
            // Three fast steps while the first source read is still held.
            vm.SelectedImage = images[1];
            vm.SelectedImage = images[2];
            vm.SelectedImage = images[3];
            await Task.Yield();
            Assert.Equal(1, Volatile.Read(ref entered));

            gate.TrySetResult();
            await TestWaits.UntilAsync(() => images[3].Thumbnail != null);
            // The held read, then only the latest selection; nothing in between.
            Assert.Equal(2, Volatile.Read(ref entered));
            Assert.Null(images[1].Thumbnail);
            Assert.Null(images[2].Thumbnail);
        }
        finally
        {
            vm.ImageService.Thumbnails.SourceLoadGateAsync = null;
            await vm.DisposeAsync();
        }
    }

    private sealed class HeldDecodeLoader(string target) : RecordingLoader
    {
        public ManualResetEventSlim Started { get; } = new();
        public ManualResetEventSlim Release { get; } = new();
        public int Attempts;

        public override BaseImageLoadOutcome LoadPreviewBaseWithOutcome(
            ImageFile file,
            BaseDecodeSettings decode,
            CancellationToken cancellationToken)
        {
            if (file.FileName == target)
            {
                Interlocked.Increment(ref Attempts);
                Started.Set();
                Release.Wait(cancellationToken);
            }
            return base.LoadPreviewBaseWithOutcome(file, decode, cancellationToken);
        }
    }

    [WindowsTheory]
    [InlineData("loupe")]
    [InlineData("develop")]
    public async Task SteppingWhileTheWalkIsAheadKeepsThatWorkerInsteadOfRestartingIt(
        string surface)
    {
        _fixture.RequireWindows();
        using var catalog = await CreateCatalogAsync($"keep-ahead-{surface}");
        var images = new[]
        {
            await CreateCatalogImageAsync(catalog, "first.jpg"),
            await CreateCatalogImageAsync(catalog, "second.jpg"),
            await CreateCatalogImageAsync(catalog, "third.jpg"),
            await CreateCatalogImageAsync(catalog, "fourth.jpg")
        };
        var clock = new TestTimeProvider();
        var loader = new HeldDecodeLoader("third.jpg");
        var vm = new MainWindowViewModel(
            catalog,
            loader,
            loadMetadataAsync: _ => Task.CompletedTask,
            availabilityService: new TestSourceAvailabilityService(
                SourceAvailability.AvailableLocally),
            timeProvider: clock)
        {
            IsDevelopMode = surface == "develop"
        };
        vm.Browse.SetImages(images);
        try
        {
            vm.SelectedImage = images[0];
            if (surface == "loupe") vm.EnterLoupeCommand.Execute(null);
            await SettleAsync(vm, surface);
            clock.Advance(TimeSpan.FromMilliseconds(75));
            // The walk warms second, then holds on third.
            Assert.True(loader.Started.Wait(TestWaits.Condition));

            vm.SelectedImage = images[1];
            await SettleAsync(vm, surface);
            Assert.Equal(1, loader.Attempts);
            Assert.Equal(1, vm.ImageService.Previews.PreviewActivityCount);
            clock.Advance(TimeSpan.FromMilliseconds(75));

            loader.Release.Set();
            await TestWaits.UntilAsync(() =>
                loader.Paths.Contains("fourth.jpg") &&
                vm.ImageService.Previews.PreviewActivityCount == 0);
            Assert.Equal(1, loader.Attempts);
            Assert.Equal(1, loader.Paths.Count(name => name == "third.jpg"));
        }
        finally
        {
            await vm.DisposeAsync();
        }
    }
}
