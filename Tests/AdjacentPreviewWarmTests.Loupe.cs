using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

public sealed partial class AdjacentPreviewWarmTests
{
    [WindowsFact]
    public async Task SettledLoupeSelectionWarmsInTravelDirectionAndPaintsFromTheWarm()
    {
        _fixture.RequireWindows();
        using var catalog = await CreateCatalogAsync("loupe-direction");
        var images = new[]
        {
            await CreateCatalogImageAsync(catalog, "first.jpg"),
            await CreateCatalogImageAsync(catalog, "second.jpg"),
            await CreateCatalogImageAsync(catalog, "third.jpg"),
            await CreateCatalogImageAsync(catalog, "fourth.jpg")
        };
        var clock = new TestTimeProvider();
        var loader = new RecordingLoader();
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
            vm.SelectedImage = images[2];
            vm.EnterLoupeCommand.Execute(null);
            Assert.True(vm.IsLoupeMode);
            await vm.LoupeLoadingTask;
            Assert.NotNull(vm.LoupePane?.Preview);
            clock.Advance(TimeSpan.FromMilliseconds(74));
            await Task.Yield();
            Assert.Single(loader.Paths);
            clock.Advance(TimeSpan.FromMilliseconds(1));
            await TestWaits.UntilAsync(() => loader.Paths.Count >= 2);
            Assert.Equal("fourth.jpg", loader.Paths.ElementAt(1));
            await TestWaits.UntilAsync(() =>
                vm.ImageService.Previews.AdjacentWarmEntryCount == 1);

            vm.SelectedImage = images[3];
            await vm.LoupeLoadingTask;
            Assert.NotNull(vm.LoupePane?.Preview);
            clock.Advance(TimeSpan.FromMilliseconds(75));
            await Task.Yield();
            Assert.Equal(2, loader.Paths.Count);

            vm.SelectedImage = images[1];
            await vm.LoupeLoadingTask;
            Assert.NotNull(vm.LoupePane?.Preview);
            clock.Advance(TimeSpan.FromMilliseconds(75));
            await TestWaits.UntilAsync(() => loader.Paths.Count >= 4);
            Assert.Equal("first.jpg", loader.Paths.ElementAt(3));

            vm.ExitLoupeCommand.Execute(null);
            await TestWaits.UntilAsync(() =>
                vm.ImageService.Previews.PreviewActivityCount == 0);
        }
        finally
        {
            await vm.DisposeAsync();
        }
    }

    [WindowsTheory]
    [InlineData("loupe")]
    [InlineData("develop")]
    public async Task LingeringWarmsFiveAheadSoABurstOfStepsFindsEveryPreviewCached(
        string surface)
    {
        _fixture.RequireWindows();
        using var catalog = await CreateCatalogAsync($"lookahead-{surface}");
        var names = new[]
        {
            "first.jpg", "second.jpg", "third.jpg", "fourth.jpg",
            "fifth.jpg", "sixth.jpg", "seventh.jpg"
        };
        var images = new List<ImageFile>();
        foreach (var name in names)
            images.Add(await CreateCatalogImageAsync(catalog, name));
        var clock = new TestTimeProvider();
        var loader = new RecordingLoader();
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
            await TestWaits.UntilAsync(() => loader.Paths.Count >= 6);
            await TestWaits.UntilAsync(() =>
                vm.ImageService.Previews.PreviewActivityCount == 0 &&
                vm.ImageService.Previews.PendingCacheWrites == 0);
            Assert.Equal(names.Take(6), loader.Paths);

            for (var index = 1; index <= 5; index++)
            {
                using (var cached = await vm.ImageService.Previews
                           .LoadCachedPreviewAsync(
                               images[index], images[index].EditSettings))
                {
                    Assert.True(cached is { SettingsMatch: true },
                        $"{names[index]} had no cached preview when selected.");
                }
                vm.SelectedImage = images[index];
                await SettleAsync(vm, surface);
                clock.Advance(TimeSpan.FromMilliseconds(75));
                await TestWaits.UntilAsync(() =>
                    vm.ImageService.Previews.PreviewActivityCount == 0);
            }

            await TestWaits.UntilAsync(() => loader.Paths.Contains("seventh.jpg"));
            if (surface == "loupe")
            {
                // The loupe paints from the cache alone, so nothing decodes twice.
                Assert.Equal(names, loader.Paths);
            }
            else
            {
                // Develop always decodes the selected image for editing; the
                // warm only has to keep every step's preview one read away.
                Assert.Equal(names, loader.Paths.Distinct());
            }
        }
        finally
        {
            await vm.DisposeAsync();
        }
    }

    [WindowsFact]
    public async Task RestrictedLoupeSelectionWarmsAlongTheSelectionNotTheGrid()
    {
        _fixture.RequireWindows();
        using var catalog = await CreateCatalogAsync("loupe-restricted");
        var names = new[]
        {
            "first.jpg", "second.jpg", "third.jpg", "fourth.jpg",
            "fifth.jpg", "sixth.jpg", "seventh.jpg"
        };
        var images = new List<ImageFile>();
        foreach (var name in names)
            images.Add(await CreateCatalogImageAsync(catalog, name));
        var clock = new TestTimeProvider();
        var loader = new RecordingLoader();
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
            images[0].IsSelected = true;
            images[3].IsSelected = true;
            images[6].IsSelected = true;
            vm.SelectedImage = images[0];
            vm.EnterLoupeCommand.Execute(null);
            Assert.True(vm.IsFullScreenSelectionRestricted);
            await vm.LoupeLoadingTask;
            clock.Advance(TimeSpan.FromMilliseconds(75));
            await TestWaits.UntilAsync(() => loader.Paths.Count >= 3);
            await TestWaits.UntilAsync(() =>
                vm.ImageService.Previews.PreviewActivityCount == 0);
            Assert.Equal(
                new[] { "first.jpg", "fourth.jpg", "seventh.jpg" },
                loader.Paths);
        }
        finally
        {
            await vm.DisposeAsync();
        }
    }

    [WindowsFact]
    public async Task EnteringARestrictedSelectionFromItsLastMemberStillWarmsForward()
    {
        _fixture.RequireWindows();
        using var catalog = await CreateCatalogAsync("loupe-restricted-last");
        var names = new[]
        {
            "first.jpg", "second.jpg", "third.jpg", "fourth.jpg",
            "fifth.jpg", "sixth.jpg", "seventh.jpg"
        };
        var images = new List<ImageFile>();
        foreach (var name in names)
            images.Add(await CreateCatalogImageAsync(catalog, name));
        var clock = new TestTimeProvider();
        var loader = new RecordingLoader();
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
            images[0].IsSelected = true;
            images[3].IsSelected = true;
            images[6].IsSelected = true;
            vm.SelectedImage = images[6];
            vm.EnterLoupeCommand.Execute(null);
            // Entry re-anchors to the first member; that jump is not travel.
            Assert.Same(images[0], vm.SelectedImage);
            await vm.LoupeLoadingTask;
            clock.Advance(TimeSpan.FromMilliseconds(75));
            await TestWaits.UntilAsync(() => loader.Paths.Count >= 3);
            await TestWaits.UntilAsync(() =>
                vm.ImageService.Previews.PreviewActivityCount == 0);
            Assert.Equal(
                new[] { "first.jpg", "fourth.jpg", "seventh.jpg" },
                loader.Paths);
        }
        finally
        {
            await vm.DisposeAsync();
        }
    }

    [WindowsFact]
    public async Task LoupeRefinementCancelsTheWarmAndResumesAfterIt()
    {
        _fixture.RequireWindows();
        using var catalog = await CreateCatalogAsync("loupe-refinement");
        var images = new[]
        {
            await CreateCatalogImageAsync(catalog, "current.jpg"),
            await CreateCatalogImageAsync(catalog, "target.jpg")
        };
        var clock = new TestTimeProvider();
        var loader = new AdjacentBlockingLoader("target.jpg");
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
            Assert.Equal(1, loader.Attempts);

            // A 1:1 peek needs a full decode the fake loader cannot provide,
            // so the refinement fails fast; the warm must still yield to it
            // and come back once it settles. The 48 px fixture is already at
            // its achievable size, so forget that to make the peek refine.
            vm.LoupePane!.AchievableLongEdge = 0;
            vm.PublishLoupeRequiredDeviceLongEdge(4000, isLoupePeekActive: true);
            await TestWaits.UntilAsync(() =>
                vm.ImageService.Previews.PreviewActivityCount == 0);
            await vm.LoupeLoadingTask;
            clock.Advance(TimeSpan.FromMilliseconds(75));
            await TestWaits.UntilAsync(() => loader.Attempts == 2);
        }
        finally
        {
            await vm.DisposeAsync();
        }
    }

    [WindowsFact]
    public async Task RefinementQueuedDuringTheInitialLoupeLoadDefersTheWarm()
    {
        _fixture.RequireWindows();
        using var catalog = await CreateCatalogAsync("loupe-early-refinement");
        var images = new[]
        {
            await CreateCatalogImageAsync(catalog, "current.jpg"),
            await CreateCatalogImageAsync(catalog, "target.jpg")
        };
        var clock = new TestTimeProvider();
        var loader = new GatedRefinementLoader("target.jpg");
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
            // Nothing has painted yet, so the peek queues behind the load.
            vm.PublishLoupeRequiredDeviceLongEdge(4000, isLoupePeekActive: true);
            Assert.True(vm.LoupePane!.IsRefinementQueued);
            await TestWaits.UntilAsync(() => vm.LoupePane?.Preview != null);
            clock.Advance(TimeSpan.FromMilliseconds(75));
            Assert.True(loader.RefinementStarted.Wait(TestWaits.Condition));

            loader.Release.Set();
            await vm.LoupeLoadingTask;
            clock.Advance(TimeSpan.FromMilliseconds(75));
            await TestWaits.UntilAsync(() => loader.Paths.Contains("target.jpg"));
            Assert.False(loader.WarmedBeforeRelease);
        }
        finally
        {
            await vm.DisposeAsync();
        }
    }

    private sealed class GatedRefinementLoader(string target) : RecordingLoader
    {
        public ManualResetEventSlim RefinementStarted { get; } = new();
        public ManualResetEventSlim Release { get; } = new();
        public bool WarmedBeforeRelease { get; private set; }

        // A preview base that stands in for a much larger original, so the
        // 1:1 peek has something to refine toward.
        public override BaseImageLoadOutcome LoadPreviewBaseWithOutcome(
            ImageFile file,
            BaseDecodeSettings decode,
            CancellationToken cancellationToken)
        {
            if (file.FileName == target && !Release.IsSet)
                WarmedBeforeRelease = true;
            Paths.Enqueue(file.FileName);
            return BaseImageLoadOutcome.Loaded(new BaseImage(
                new MagickImage(MagickColors.Gray, 48, 32)
                {
                    Depth = 16,
                    ColorSpace = ColorSpace.RGB
                },
                new BaseImageInfo(
                    BaseSourceKind.Standard, false, decode, null, null, 6504, 0,
                    false, null, 1, 4800, 3200)
                {
                    ProfileToken = decode.ProfileResolution?.Token ?? string.Empty
                }));
        }

        public override BaseImage? LoadFullBase(
            ImageFile file,
            BaseDecodeSettings decode,
            CancellationToken cancellationToken)
        {
            RefinementStarted.Set();
            Release.Wait(cancellationToken);
            return null;
        }
    }

    [WindowsFact]
    public async Task ALoupeImageWithoutAThumbnailGetsOneWhileItsPreviewStillLoads()
    {
        _fixture.RequireWindows();
        using var catalog = await CreateCatalogAsync("loupe-thumbnail");
        // A real JPEG, so the thumbnail service can decode it.
        var path = Path.Combine(_root.Path, "real.jpg");
        using (var pixels = new MagickImage(MagickColors.Orange, 64, 48))
        {
            pixels.Write(path);
        }
        var image = new ImageFile(path);
        await image.EnsureCatalogIdAsync(catalog);
        var clock = new TestTimeProvider();
        var loader = new HeldDecodeLoader("real.jpg");
        var vm = new MainWindowViewModel(
            catalog,
            loader,
            loadMetadataAsync: _ => Task.CompletedTask,
            availabilityService: new TestSourceAvailabilityService(
                SourceAvailability.AvailableLocally),
            timeProvider: clock);
        vm.Browse.SetImages([image]);
        try
        {
            Assert.Null(image.Thumbnail);
            vm.SelectedImage = image;
            vm.EnterLoupeCommand.Execute(null);
            Assert.True(vm.IsThumbnailPumpPaused);
            Assert.True(loader.Started.Wait(TestWaits.Condition));
            // Preview held; the thumbnail must still arrive underneath it.
            await TestWaits.UntilAsync(() => image.Thumbnail != null);
            Assert.Null(vm.LoupePane!.Preview);

            loader.Release.Set();
            await vm.LoupeLoadingTask;
            Assert.NotNull(vm.LoupePane?.Preview);
        }
        finally
        {
            await vm.DisposeAsync();
        }
    }

    private static async Task SettleAsync(MainWindowViewModel vm, string surface)
    {
        if (surface == "loupe")
        {
            await vm.LoupeLoadingTask;
            Assert.NotNull(vm.LoupePane?.Preview);
            return;
        }
        await TestWaits.UntilAsync(() =>
            vm.PreviewImage != null && vm.InitialPreviewActivityCount == 0);
    }
}
