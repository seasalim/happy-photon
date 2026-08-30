using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using HappyPhoton.Views;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class HistoryGeometryViewModelTests : IDisposable
{
    private readonly CatalogVmFixture _fixture = new("history-geometry-vm");

    [AvaloniaFact]
    public async Task RapidRotatesCommitSeparatelyBeforeCropMode()
    {
        using var catalog = await _fixture.CreateCatalogAsync("rapid-rotate");
        await using var vm = CreateViewModel(catalog);
        var image = await CreateImageAsync(catalog, "rapid.jpg");
        vm.SelectedImage = image;
        await WaitForHistoryAsync(vm);

        vm.RotateRightCommand.Execute(null);
        vm.RotateRightCommand.Execute(null);
        await Assert.IsAssignableFrom<Task>(vm.PendingHistoryCommitTask);

        Assert.Equal(180, image.EditSettings.Rotation);
        Assert.Equal(
            ["Rotate right", "Rotate right", "Original"],
            vm.HistoryEntries.Select(entry => entry.Label));

        await vm.ToggleCropModeCommand.ExecuteAsync(null);
        Assert.True(vm.IsCropMode);
        Assert.Equal(3, vm.HistoryEntries.Count);
    }

    [AvaloniaFact]
    public async Task CropApplyCommitsAfterMidModeExposureWithCommittedGeometry()
    {
        using var catalog = await _fixture.CreateCatalogAsync("crop-order");
        var clock = new TestTimeProvider();
        await using var vm = CreateViewModel(catalog, clock);
        var image = await CreateImageAsync(catalog, "crop-order.jpg");
        vm.SelectedImage = image;
        await WaitForHistoryAsync(vm);

        await vm.ToggleCropModeCommand.ExecuteAsync(null);
        vm.HorizonRotation = 1.5;
        vm.Exposure = .4;
        await SettleDebounceAsync(vm, clock);

        var exposure = Assert.Single(vm.HistoryEntries,
            entry => entry.Label.StartsWith("Exposure"));
        Assert.Equal(0, exposure.Settings.HorizonRotation);
        Assert.Null(exposure.Settings.Crop);

        vm.CurrentCrop = new CropRegion
        {
            Left = .1,
            Top = .2,
            Right = .9,
            Bottom = .8
        };
        await vm.ApplyCropCommand.ExecuteAsync(null);
        if (vm.PendingHistoryCommitTask is { } pending) await pending;

        Assert.Equal(
            ["Crop", "Exposure +0.40 (+0.40)", "Original"],
            vm.HistoryEntries.Select(entry => entry.Label));
        var crop = vm.HistoryEntries[0].Settings;
        Assert.Equal(1.5, crop.HorizonRotation);
        Assert.Equal(.1, crop.Crop!.Left);
        Assert.Equal(.4, crop.Exposure);
    }

    [AvaloniaFact]
    public async Task EnterAndApplyDrainPendingEditsInHistoryOrder()
    {
        using var catalog = await _fixture.CreateCatalogAsync("pending-order");
        var clock = new TestTimeProvider();
        await using var vm = CreateViewModel(catalog, clock);
        var image = await CreateImageAsync(catalog, "pending-order.jpg");
        vm.SelectedImage = image;
        await WaitForHistoryAsync(vm);

        vm.HorizonRotation = .75;
        var enter = vm.ToggleCropModeCommand.ExecuteAsync(null);
        clock.Advance(TimeSpan.FromMilliseconds(200));
        await enter;
        Assert.Equal("Horizon +0.75° (+0.75°)", vm.HistoryEntries[0].Label);

        vm.HorizonRotation = 1.25;
        vm.CurrentCrop = new CropRegion { Left = .1, Right = .9 };
        vm.Exposure = .2;
        var apply = vm.ApplyCropCommand.ExecuteAsync(null);
        clock.Advance(TimeSpan.FromMilliseconds(200));
        await apply;
        if (vm.PendingHistoryCommitTask is { } pending) await pending;

        Assert.Equal(
            ["Crop", "Exposure +0.20 (+0.20)",
             "Horizon +0.75° (+0.75°)", "Original"],
            vm.HistoryEntries.Select(entry => entry.Label));
        Assert.Equal(.75, vm.HistoryEntries[1].Settings.HorizonRotation);
        Assert.Equal(1.25, vm.HistoryEntries[0].Settings.HorizonRotation);
    }

    [AvaloniaFact]
    public async Task CancelCropRestoresGeometryWithoutHistory()
    {
        using var catalog = await _fixture.CreateCatalogAsync("crop-cancel");
        await using var vm = CreateViewModel(catalog);
        var committed = Geometry(90, .5, .1);
        var image = await CreateImageAsync(catalog, "crop-cancel.jpg", committed);
        vm.SelectedImage = image;
        await WaitForHistoryAsync(vm);

        await vm.ToggleCropModeCommand.ExecuteAsync(null);
        vm.HorizonRotation = 2;
        vm.CurrentCrop = new CropRegion { Left = .3, Right = .7 };
        await vm.CancelCropCommand.ExecuteAsync(null);

        Assert.Empty(vm.HistoryEntries);
        AssertGeometry(vm, image, committed);
    }

    [AvaloniaFact]
    public async Task FailedCropRenderKeepsMidModeCommitAndCommittedGeometry()
    {
        using var catalog = await _fixture.CreateCatalogAsync("crop-failure");
        var clock = new TestTimeProvider();
        var committed = Geometry(90, .5, .1);
        var expected = committed.Clone();
        await using var vm = CreateViewModel(catalog, clock);
        var image = await CreateImageAsync(catalog, "crop-failure.jpg", committed);
        vm.SelectedImage = image;
        await WaitForHistoryAsync(vm);
        await TestWaits.UntilAsync(() => vm.PreviewImage != null);

        await vm.ToggleCropModeCommand.ExecuteAsync(null);
        vm.Exposure = .3;
        await SettleDebounceAsync(vm, clock);
        vm.HorizonRotation = 2;
        await SettleDebounceAsync(vm, clock);
        var draft = new CropRegion { Left = .25, Right = .75 };
        vm.CurrentCrop = draft;
        var labels = vm.HistoryEntries.Select(entry => entry.Label).ToArray();
        var current = vm.HistoryEntries.Single(entry => entry.IsCurrent);
        vm.ImageService.Previews.RenderGateAsync = () =>
            Task.FromException(new InvalidOperationException("render failed"));

        await vm.ApplyCropCommand.ExecuteAsync(null);

        Assert.True(vm.IsCropMode);
        Assert.Same(draft, vm.CurrentCrop);
        Assert.Equal(2, vm.HorizonRotation);
        Assert.Equal(labels, vm.HistoryEntries.Select(entry => entry.Label));
        Assert.Same(current, vm.HistoryEntries.Single(entry => entry.IsCurrent));
        Assert.Equal(.3, image.EditSettings.Exposure);
        Assert.Equal(expected.Rotation, vm.Rotation);
        Assert.Equal(expected.Rotation, image.EditSettings.Rotation);
        Assert.Equal(expected.HorizonRotation, image.EditSettings.HorizonRotation);
        Assert.Equal(expected.Crop!.Left, image.EditSettings.Crop!.Left);
        var persisted = (await catalog.LoadImageStatesAsync([image.FilePath]))
            [image.FilePath].Single().EditSettings;
        Assert.Equal(.3, persisted.Exposure);
        Assert.Equal(.5, persisted.HorizonRotation);
        Assert.Equal(.1, persisted.Crop!.Left);
    }

    [AvaloniaFact]
    public async Task HistoryNavigationRestoresGeometryAndCropModeGuardsCommands()
    {
        using var catalog = await _fixture.CreateCatalogAsync("navigation");
        var original = new EditSettings();
        var middle = Geometry(90, 1, .1);
        var latest = Geometry(180, 2, .2);
        var image = await CreateImageAsync(catalog, "navigation.jpg", middle);
        await catalog.SaveEditSettingsWithHistoryAsync(
            image.CatalogId,
            middle,
            new CatalogEditHistoryMutation(-1,
            [
                new(0, "Original", original),
                new(1, "Crop", middle),
                new(2, "Rotate right", latest)
            ], 1));
        await using var vm = CreateViewModel(catalog);
        vm.SelectedImage = image;
        await WaitForHistoryAsync(vm);
        var originalEntry = vm.HistoryEntries.Single(entry => entry.Sequence == 0);
        var middleEntry = vm.HistoryEntries.Single(entry => entry.Sequence == 1);
        var latestEntry = vm.HistoryEntries.Single(entry => entry.Sequence == 2);

        Assert.True(vm.UndoCommand.CanExecute(null));
        Assert.True(vm.RedoCommand.CanExecute(null));
        Assert.True(vm.JumpToHistoryStepCommand.CanExecute(originalEntry));
        Assert.True(vm.ClearHistoryCommand.CanExecute(null));
        Assert.True(vm.ClearHistoryAboveStepCommand.CanExecute(originalEntry));
        Assert.True(vm.RotateLeftCommand.CanExecute(null));
        Assert.True(vm.RotateRightCommand.CanExecute(null));

        await vm.ToggleCropModeCommand.ExecuteAsync(null);
        Assert.False(vm.UndoCommand.CanExecute(null));
        Assert.False(vm.RedoCommand.CanExecute(null));
        Assert.False(vm.JumpToHistoryStepCommand.CanExecute(originalEntry));
        Assert.False(vm.ClearHistoryCommand.CanExecute(null));
        Assert.False(vm.ClearHistoryAboveStepCommand.CanExecute(originalEntry));
        Assert.False(vm.RotateLeftCommand.CanExecute(null));
        Assert.False(vm.RotateRightCommand.CanExecute(null));
        await vm.CancelCropCommand.ExecuteAsync(null);
        Assert.True(vm.RotateLeftCommand.CanExecute(null));
        Assert.True(vm.RotateRightCommand.CanExecute(null));

        await vm.RedoCommand.ExecuteAsync(null);
        AssertGeometry(vm, image, latest);
        await vm.UndoCommand.ExecuteAsync(null);
        AssertGeometry(vm, image, middle);
        await vm.JumpToHistoryStepCommand.ExecuteAsync(originalEntry);
        AssertGeometry(vm, image, original);
        await vm.ClearHistoryAboveStepCommand.ExecuteAsync(middleEntry);
        AssertGeometry(vm, image, middle);
        Assert.DoesNotContain(vm.HistoryEntries, entry => entry == latestEntry);
    }

    [AvaloniaTheory]
    [InlineData("jump")]
    [InlineData("clear")]
    [InlineData("trim")]
    public async Task PendingHistoryMutationStopsWhenCropEntryIsRequested(
        string command)
    {
        using var catalog = await _fixture.CreateCatalogAsync($"blocked-{command}");
        var clock = new TestTimeProvider();
        var original = new EditSettings();
        var current = Geometry(90, 1, .1);
        var image = await CreateImageAsync(catalog, $"blocked-{command}.jpg", current);
        await catalog.SaveEditSettingsWithHistoryAsync(
            image.CatalogId,
            current,
            new CatalogEditHistoryMutation(-1,
            [
                new(0, "Original", original),
                new(1, "Crop", current)
            ], 1));
        await using var vm = CreateViewModel(catalog, clock);
        vm.SelectedImage = image;
        await WaitForHistoryAsync(vm);
        var originalEntry = vm.HistoryEntries.Single(entry => entry.Sequence == 0);
        var labels = vm.HistoryEntries.Select(entry => entry.Label).ToArray();

        vm.OnSliderEditStarted();
        vm.Exposure = .1;
        Assert.NotNull(vm.PendingPreviewDebounceTask);
        var blocked = command switch
        {
            "jump" => vm.JumpToHistoryStepCommand.ExecuteAsync(originalEntry),
            "clear" => vm.ClearHistoryCommand.ExecuteAsync(null),
            "trim" => vm.ClearHistoryAboveStepCommand.ExecuteAsync(originalEntry),
            _ => throw new ArgumentOutOfRangeException(nameof(command))
        };
        var enter = vm.ToggleCropModeCommand.ExecuteAsync(null);

        clock.Advance(TimeSpan.FromMilliseconds(200));
        await Task.WhenAll(blocked, enter);

        Assert.True(vm.IsCropMode);
        Assert.Equal(labels, vm.HistoryEntries.Select(entry => entry.Label));
        Assert.Equal(90, image.EditSettings.Rotation);
        Assert.Equal(1, image.EditSettings.HorizonRotation);

        vm.OnSliderEditCompleted();
        clock.Advance(TimeSpan.FromMilliseconds(200));
        if (vm.PendingPreviewDebounceTask is { } pending) await pending;
    }

    [AvaloniaFact]
    public async Task HistoryPanelShowsGeometryRowsAndCropRowRestoresOverlay()
    {
        using var catalog = await _fixture.CreateCatalogAsync("geometry-panel");
        var original = new EditSettings();
        var rotated = Geometry(90, 0, 0);
        rotated.Crop = null;
        var cropped = Geometry(90, 1, .1);
        var latest = Geometry(180, 1, .1);
        var image = await CreateImageAsync(catalog, "geometry-panel.jpg", latest);
        await catalog.SaveEditSettingsWithHistoryAsync(
            image.CatalogId,
            latest,
            new CatalogEditHistoryMutation(-1,
            [
                new(0, "Original", original),
                new(1, "Rotate right", rotated),
                new(2, "Crop", cropped),
                new(3, "Rotate right", latest)
            ], 3));
        await using var vm = CreateViewModel(catalog);
        vm.ShowWorkspaceReady(MainWindowViewModel.CurrentFirstRunExperienceVersion);
        vm.Browse.SetImages([image]);
        vm.SelectedImage = image;
        await WaitForHistoryAsync(vm);

        var window = new MainWindow
        {
            Width = 900,
            Height = 650,
            DataContext = vm
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            var panel = window.FindControl<EditHistoryPanel>("EditHistoryPanel")!;
            var rows = panel.GetVisualDescendants().OfType<Button>()
                .Where(button => button.Classes.Contains("history-row"))
                .ToArray();
            Assert.Equal(2, rows.Count(row =>
                ((EditHistoryEntry)row.DataContext!).Label == "Rotate right"));
            var cropRow = Assert.Single(rows, row =>
                ((EditHistoryEntry)row.DataContext!).Label == "Crop");

            cropRow.Command!.Execute(cropRow.CommandParameter);
            await Assert.IsAssignableFrom<Task>(
                vm.JumpToHistoryStepCommand.ExecutionTask);

            Assert.Equal(90, vm.Rotation);
            Assert.Equal(1, vm.HorizonRotation);
            Assert.Equal(.1, vm.CurrentCrop!.Left);
        }
        finally
        {
            window.DataContext = null;
            window.Close();
        }
    }

    private MainWindowViewModel CreateViewModel(
        CatalogService catalog,
        TimeProvider? clock = null)
    {
        var vm = _fixture.CreateViewModel(
            catalog,
            new TinyBaseLoader(),
            loadMetadataAsync: _ => Task.CompletedTask,
            availabilityService: new TestSourceAvailabilityService(
                SourceAvailability.AvailableLocally),
            timeProvider: clock);
        vm.IsDevelopMode = true;
        return vm;
    }

    private async Task<ImageFile> CreateImageAsync(
        CatalogService catalog,
        string name,
        EditSettings? settings = null)
    {
        var image = new ImageFile(_fixture.Path(name))
        {
            EditSettings = settings ?? new EditSettings()
        };
        image.CatalogId = await catalog.GetOrCreateImageAsync(image.FilePath);
        await catalog.SaveEditSettingsAsync(image.CatalogId, image.EditSettings);
        return image;
    }

    private static EditSettings Geometry(int rotation, double horizon, double inset) =>
        new()
        {
            Rotation = rotation,
            HorizonRotation = horizon,
            Crop = new CropRegion
            {
                Left = inset,
                Top = inset,
                Right = 1 - inset,
                Bottom = 1 - inset
            }
        };

    private static void AssertGeometry(
        MainWindowViewModel vm,
        ImageFile image,
        EditSettings expected)
    {
        Assert.Equal(expected.Rotation, vm.Rotation);
        Assert.Equal(expected.Rotation, image.EditSettings.Rotation);
        Assert.Equal(expected.HorizonRotation, vm.HorizonRotation);
        Assert.Equal(expected.HorizonRotation, image.EditSettings.HorizonRotation);
        Assert.Equal(expected.Crop?.Left, vm.CurrentCrop?.Left);
        Assert.Equal(expected.Crop?.Left, image.EditSettings.Crop?.Left);
    }

    private static async Task SettleDebounceAsync(
        MainWindowViewModel vm,
        TestTimeProvider clock)
    {
        clock.Advance(TimeSpan.FromMilliseconds(200));
        if (vm.PendingPreviewDebounceTask is { } debounce) await debounce;
        if (vm.PendingHistoryCommitTask is { } pending) await pending;
    }

    private static Task WaitForHistoryAsync(MainWindowViewModel vm) =>
        TestWaits.UntilAsync(() => vm.IsHistoryLoaded);

    public void Dispose() => _fixture.Dispose();

    private sealed class TinyBaseLoader : IBaseImageLoader
    {
        public bool CanLoad(ImageFile file) => true;

        public BaseImage LoadPreviewBase(
            ImageFile file,
            BaseDecodeSettings decode,
            CancellationToken cancellationToken) => Create(decode);

        BaseImageLoadOutcome IBaseImageLoader.LoadPreviewBaseWithOutcome(
            ImageFile file,
            BaseDecodeSettings decode,
            CancellationToken cancellationToken) =>
            BaseImageLoadOutcome.Loaded(Create(decode));

        public BaseImage? LoadFullBase(
            ImageFile file,
            BaseDecodeSettings decode,
            CancellationToken cancellationToken) => Create(decode);

        private static BaseImage Create(BaseDecodeSettings decode) =>
            new(
                new MagickImage(MagickColors.Gray, 16, 12)
                {
                    ColorSpace = ColorSpace.RGB
                },
                new BaseImageInfo(
                    BaseSourceKind.Standard,
                    false,
                    decode,
                    null,
                    null,
                    6504,
                    0,
                    false,
                    null,
                    1,
                    16,
                    12));
    }
}
