using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class HistoryHoverViewModelTests : IDisposable
{
    private readonly CatalogVmFixture _fixture = new("history-hover-vm");

    [AvaloniaFact]
    public async Task HoverRendersGeometryWithoutChangingLiveSurfacesOrWrites()
    {
        using var catalog = await _fixture.CreateUniqueCatalogAsync();
        var snapshot = new EditSettings
        {
            Rotation = 90,
            HorizonRotation = 12,
            Crop = new CropRegion
            {
                Left = .1,
                Top = .15,
                Right = .75,
                Bottom = .8
            }
        };
        var (vm, _, clock, _) = await PrepareAsync(
            catalog, [snapshot, new EditSettings()], 1, waveform: true);
        await using var ownedVm = vm;
        var writes = 0;
        catalog.EditHistoryWriteGateAsync = () =>
        {
            writes++;
            return Task.CompletedTask;
        };
        var preview = vm.PreviewImage;
        var histogram = vm.Histogram;
        var waveform = vm.EffectiveWaveform;
        var generation = vm.LatestPreviewOutcomeGeneration;
        var target = vm.HistoryEntries.Single(entry => !entry.IsCurrent);

        var hoverTask = vm.PreviewHistoryHoverAsync(target);
        clock.Advance(TimeSpan.FromMilliseconds(80));
        await hoverTask;
        var hover = Assert.IsAssignableFrom<Avalonia.Media.Imaging.Bitmap>(
            vm.NavigatorHoverImage);
        var identity = vm.ImageService.Previews.TryGetPreviewRenderIdentity(hover)!;
        Assert.Equal(RenderSettingsHash.Compute(snapshot), identity.SettingsHash);
        Assert.Equal(280, Math.Max(hover.PixelSize.Width, hover.PixelSize.Height));
        Assert.True(hover.PixelSize.Height > hover.PixelSize.Width);
        Assert.Same(preview, vm.PreviewImage);
        Assert.Same(histogram, vm.Histogram);
        Assert.Same(waveform, vm.EffectiveWaveform);
        Assert.Equal(generation, vm.LatestPreviewOutcomeGeneration);
        Assert.Equal(0, writes);

        var level = snapshot.Clone();
        level.HorizonRotation = 0;
        var levelResult = await vm.ImageService.Previews
            .RenderCurrentBaseSideSurfaceAsync(vm.SelectedImage!, level, 280);
        using var levelBitmap = Assert.IsAssignableFrom<Avalonia.Media.Imaging.Bitmap>(
            levelResult.Bitmap);
        Assert.True(PixelsDiffer(hover, levelBitmap));

        vm.EndHistoryHover();
        Dispatcher.UIThread.RunJobs();
        Assert.Null(vm.NavigatorHoverImage);
        Assert.Throws<ObjectDisposedException>(() => _ = hover.PixelSize);
    }

    [AvaloniaFact]
    public async Task DecodeMismatchReturnsNoLayerAndStartsNoDecode()
    {
        using var catalog = await _fixture.CreateUniqueCatalogAsync();
        var mismatch = new EditSettings
        {
            HlReconstruction = HlReconstructionMode.Blend
        };
        var (vm, _, clock, loader) = await PrepareAsync(
            catalog, [mismatch, new EditSettings()], 1);
        await using var ownedVm = vm;
        var decodeCount = loader.DecodeCount;
        var gateEntries = 0;
        vm.ImageService.Previews.SideSurfaceRenderGateAsync = () =>
        {
            gateEntries++;
            return Task.CompletedTask;
        };

        var task = vm.PreviewHistoryHoverAsync(
            vm.HistoryEntries.Single(entry => !entry.IsCurrent));
        clock.Advance(TimeSpan.FromMilliseconds(80));
        await task;

        Assert.Null(vm.NavigatorHoverImage);
        Assert.Equal(0, gateEntries);
        Assert.Equal(decodeCount, loader.DecodeCount);
    }

    [AvaloniaFact]
    public async Task InertStatesNeverEnterTheSideRender()
    {
        using var catalog = await _fixture.CreateUniqueCatalogAsync();
        var (vm, _, clock, _) = await PrepareAsync(
            catalog, [new EditSettings { Exposure = .2 }, new EditSettings()], 1);
        await using var ownedVm = vm;
        var entries = 0;
        vm.ImageService.Previews.SideSurfaceRenderGateAsync = () =>
        {
            entries++;
            return Task.CompletedTask;
        };
        var target = vm.HistoryEntries.Single(entry => !entry.IsCurrent);

        await AttemptAsync(vm, clock,
            vm.HistoryEntries.Single(entry => entry.IsCurrent));
        vm.IsCropMode = true;
        await AttemptAsync(vm, clock, target);
        vm.IsCropMode = false;
        SetField(vm, "_cropModeTransitionRequested", true);
        await AttemptAsync(vm, clock, target);
        SetField(vm, "_cropModeTransitionRequested", false);
        SetField(vm, "_isHoveringPreset", true);
        await AttemptAsync(vm, clock, target);
        SetField(vm, "_isHoveringPreset", false);
        vm.IsBeforeAfterSplit = true;
        await AttemptAsync(vm, clock, target);
        vm.IsBeforeAfterSplit = false;
        vm.IsDevelopMode = false;
        await AttemptAsync(vm, clock, target);

        Assert.Equal(0, entries);
        Assert.Null(vm.NavigatorHoverImage);
    }

    [AvaloniaFact]
    public async Task ReplacementAndHistoryApplyClearAndRetireHoverBitmaps()
    {
        using var catalog = await _fixture.CreateUniqueCatalogAsync();
        EditSettings[] settings =
        [
            new() { Exposure = .1 },
            new() { Exposure = .2 },
            new() { Exposure = .3 }
        ];
        var (vm, image, clock, _) = await PrepareAsync(catalog, settings, 2);
        await using var ownedVm = vm;
        var targets = vm.HistoryEntries.Where(entry => !entry.IsCurrent).ToArray();

        await HoverAsync(vm, clock, targets[0]);
        var first = vm.NavigatorHoverImage!;
        await HoverAsync(vm, clock, targets[1]);
        var second = vm.NavigatorHoverImage!;
        Dispatcher.UIThread.RunJobs();
        Assert.Throws<ObjectDisposedException>(() => _ = first.PixelSize);

        await vm.JumpToHistoryStepCommand.ExecuteAsync(targets[1]);
        Assert.Null(vm.NavigatorHoverImage);
        Assert.Equal(targets[1].Settings.Exposure, image.EditSettings.Exposure);
        Dispatcher.UIThread.RunJobs();
        Assert.Throws<ObjectDisposedException>(() => _ = second.PixelSize);
        await vm.UndoCommand.ExecuteAsync(null);
        Assert.Null(vm.NavigatorHoverImage);
    }

    [AvaloniaFact]
    public async Task SelectionChangeSuppressesHeldHoverRender()
    {
        using var catalog = await _fixture.CreateUniqueCatalogAsync();
        var (vm, image, clock, _) = await PrepareAsync(
            catalog, [new EditSettings { Exposure = .2 }, new EditSettings()], 1);
        await using var ownedVm = vm;
        var entered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        vm.ImageService.Previews.SideSurfaceRenderGateAsync = async () =>
        {
            entered.TrySetResult();
            await release.Task;
        };
        var target = vm.HistoryEntries.Single(entry => !entry.IsCurrent);
        var hoverTask = vm.PreviewHistoryHoverAsync(target);
        clock.Advance(TimeSpan.FromMilliseconds(80));
        await entered.Task.WaitAsync(TestWaits.Condition);

        var replacement = new ImageFile(_fixture.Path("replacement.jpg"));
        replacement.CatalogId = await catalog.GetOrCreateImageAsync(
            replacement.FilePath);
        vm.Browse.SetImages([image, replacement]);
        vm.SelectedImage = replacement;
        Assert.Null(vm.NavigatorHoverImage);
        release.TrySetResult();
        await hoverTask;
        Dispatcher.UIThread.RunJobs();

        Assert.Null(vm.NavigatorHoverImage);
    }

    [AvaloniaFact]
    public async Task HoverCancelsHeldRestingPaintAndRearmsAfterLeave()
    {
        using var catalog = await _fixture.CreateUniqueCatalogAsync();
        var (vm, _, clock, _) = await PrepareAsync(
            catalog, [new EditSettings { Exposure = .2 }, new EditSettings()], 1);
        await using var ownedVm = vm;
        var started = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        vm.ImageService.Previews.RestingStageStarted = stage =>
        {
            if (stage != "pipeline") return;
            started.TrySetResult();
            release.Task.GetAwaiter().GetResult();
        };
        var target = vm.HistoryEntries.Single(entry => !entry.IsCurrent);
        var preview = vm.PreviewImage;
        var paints = vm.RestingPaintCount;

        try
        {
            vm.PublishRequiredDeviceLongEdge(1000);
            await TestWaits.UntilAsync(() => vm.HasArmedRestingRender);
            clock.Advance(TimeSpan.FromMilliseconds(75));
            await started.Task.WaitAsync(TestWaits.Condition);

            var hoverTask = vm.PreviewHistoryHoverAsync(target);
            clock.Advance(TimeSpan.FromMilliseconds(80));
            await hoverTask;
            Assert.NotNull(vm.NavigatorHoverImage);

            release.TrySetResult();
            await TestWaits.UntilAsync(() =>
                vm.ImageService.Previews.PreviewActivityCount == 0);
            Assert.Same(preview, vm.PreviewImage);
            Assert.Equal(paints, vm.RestingPaintCount);

            vm.EndHistoryHover();
            await TestWaits.UntilAsync(() => vm.HasArmedRestingRender);
            clock.Advance(TimeSpan.FromMilliseconds(75));
            await TestWaits.UntilAsync(() => vm.RestingPaintCount == paints + 1);
            Assert.NotSame(preview, vm.PreviewImage);
        }
        finally
        {
            release.TrySetResult();
            vm.ImageService.Previews.RestingStageStarted = null;
        }
    }

    [AvaloniaFact]
    public async Task HoverIsInertThroughoutBeforeAfterSplitEntry()
    {
        using var catalog = await _fixture.CreateUniqueCatalogAsync();
        var (vm, _, clock, _) = await PrepareAsync(
            catalog, [new EditSettings(), new EditSettings { Exposure = .2 }], 1);
        await using var ownedVm = vm;
        await vm.ToggleBeforeAfterCommand.ExecuteAsync(null);
        Assert.True(vm.IsShowingOriginal);
        var started = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        vm.ImageService.Previews.RenderGateAsync = async () =>
        {
            started.TrySetResult();
            await release.Task;
        };
        Task? splitTask = null;

        try
        {
            splitTask = vm.ToggleBeforeAfterSplitCommand.ExecuteAsync(null);
            await started.Task.WaitAsync(TestWaits.Condition);
            var hoverTask = vm.PreviewHistoryHoverAsync(
                vm.HistoryEntries.Single(entry => !entry.IsCurrent));
            clock.Advance(TimeSpan.FromMilliseconds(80));
            await hoverTask;

            release.TrySetResult();
            await splitTask;
            Assert.True(vm.IsBeforeAfterSplit);
            Assert.Null(vm.NavigatorHoverImage);
        }
        finally
        {
            release.TrySetResult();
            if (splitTask != null) await splitTask;
            vm.ImageService.Previews.RenderGateAsync = null;
        }
    }

    private async Task<(MainWindowViewModel Vm, ImageFile Image,
        TestTimeProvider Clock, TwoToneLoader Loader)> PrepareAsync(
        CatalogService catalog,
        IReadOnlyList<EditSettings> settings,
        int position,
        bool waveform = false)
    {
        var image = new ImageFile(_fixture.Path($"hover-{Guid.NewGuid():N}.jpg"))
        {
            EditSettings = settings[position].Clone()
        };
        image.CatalogId = await catalog.GetOrCreateImageAsync(image.FilePath);
        var rows = settings.Select((value, index) => new CatalogEditHistoryEntry(
            index, index == 0 ? "Original" : $"Step {index}", value)).ToArray();
        await catalog.SaveEditSettingsWithHistoryAsync(
            image.CatalogId, image.EditSettings,
            new CatalogEditHistoryMutation(-1, rows, position));
        var clock = new TestTimeProvider();
        var loader = new TwoToneLoader();
        var vm = _fixture.CreateViewModel(
            catalog, loader, loadMetadataAsync: _ => Task.CompletedTask,
            availabilityService: new TestSourceAvailabilityService(
                SourceAvailability.AvailableLocally),
            timeProvider: clock);
        vm.ShowWorkspaceReady(MainWindowViewModel.CurrentFirstRunExperienceVersion);
        vm.IsDevelopMode = true;
        if (waveform) vm.SelectedScope = ScopeView.Waveform;
        vm.Browse.SetImages([image]);
        vm.SelectedImage = image;
        await TestWaits.UntilAsync(() => vm.IsHistoryLoaded);
        await TestWaits.UntilAsync(() => vm.PreviewImage != null &&
            vm.ImageService.Previews.PreviewActivityCount == 0);
        if (waveform)
            await TestWaits.UntilAsync(() => vm.EffectiveWaveform != null);
        return (vm, image, clock, loader);
    }

    private static async Task HoverAsync(
        MainWindowViewModel vm, TestTimeProvider clock, EditHistoryEntry entry)
    {
        var task = vm.PreviewHistoryHoverAsync(entry);
        clock.Advance(TimeSpan.FromMilliseconds(80));
        await task;
        Assert.NotNull(vm.NavigatorHoverImage);
    }

    private static async Task AttemptAsync(
        MainWindowViewModel vm, TestTimeProvider clock, EditHistoryEntry entry)
    {
        var task = vm.PreviewHistoryHoverAsync(entry);
        clock.Advance(TimeSpan.FromMilliseconds(100));
        await task;
        Assert.Null(vm.NavigatorHoverImage);
    }

    private static void SetField(MainWindowViewModel vm, string name, bool value) =>
        typeof(MainWindowViewModel).GetField(
            name,
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.NonPublic)!.SetValue(vm, value);

    private static bool PixelsDiffer(
        Avalonia.Media.Imaging.Bitmap left,
        Avalonia.Media.Imaging.Bitmap right)
    {
        var leftPixels = BitmapConversionService.CopyBgraPixels(left);
        var rightPixels = BitmapConversionService.CopyBgraPixels(right);
        return !leftPixels.AsSpan().SequenceEqual(rightPixels);
    }

    public void Dispose() => _fixture.Dispose();

    private sealed class TwoToneLoader : IBaseImageLoader
    {
        private int _decodeCount;
        public int DecodeCount => Volatile.Read(ref _decodeCount);
        public bool CanLoad(ImageFile file) => true;
        public BaseImage LoadPreviewBase(ImageFile file, BaseDecodeSettings decode,
            CancellationToken cancellationToken) => Create(decode);
        public BaseImageLoadOutcome LoadPreviewBaseWithOutcome(
            ImageFile file, BaseDecodeSettings decode,
            CancellationToken cancellationToken) => BaseImageLoadOutcome.Loaded(
                new PreviewBasePair(
                    Create(decode),
                    Create(decode, 1280, 960, countDecode: false)));
        public BaseImage? LoadFullBase(ImageFile file, BaseDecodeSettings decode,
            CancellationToken cancellationToken) => Create(decode);

        private BaseImage Create(
            BaseDecodeSettings decode,
            int width = 640,
            int height = 480,
            bool countDecode = true)
        {
            if (countDecode) Interlocked.Increment(ref _decodeCount);
            var image = new MagickImage(MagickColors.Black, (uint)width, (uint)height)
            {
                ColorSpace = ColorSpace.RGB
            };
            using var pixels = image.GetPixels();
            for (var y = 0; y < height; y++)
            for (var x = width / 2; x < width; x++)
                pixels.SetPixel(x, y, [ushort.MaxValue, ushort.MaxValue, ushort.MaxValue]);
            return new BaseImage(image, new BaseImageInfo(
                BaseSourceKind.Standard, false, decode, null, null, 6504, 0,
                false, null, 1, width, height));
        }
    }
}
