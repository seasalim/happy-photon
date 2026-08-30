using System.Reflection;
using Avalonia.Headless.XUnit;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class EditHistoryViewModelTests : IDisposable
{
    private readonly CatalogVmFixture _fixture = new("history-vm");

    [AvaloniaFact]
    public async Task EveryCommittedEditPathAddsExactlyOneLabeledStep()
    {
        using var catalog = await _fixture.CreateCatalogAsync("all-paths");
        var clock = new TestTimeProvider();
        await using var vm = CreateViewModel(catalog, clock);
        await vm.PresetService.UseDirectoryAsync(_fixture.Path("presets"));
        var preset = await vm.PresetService.SaveUserPresetAsync(
            "History preset",
            new EditSettings { Contrast = 18 });
        var image = await CreateImageAsync(catalog, "all-paths.jpg");
        vm.Browse.SetImages([image]);
        vm.SelectedImage = image;
        await WaitForHistoryAsync(vm);

        await AssertStepAsync(vm, "Exposure +0.30 (+0.30)", () =>
            CommitDebouncedAsync(vm, clock, () => vm.Exposure = 0.3));
        await AssertStepAsync(vm, "Curve", async () =>
        {
            vm.OnCurveEditStarted();
            vm.CurrentCurve!.AddPointAndReturnIndex(0.5, 0.7);
            await vm.OnCurveChangedAsync();
        });
        await AssertStepAsync(vm, "Red hue +12 (+12)", () =>
            CommitDebouncedAsync(vm, clock, () => vm.MixerHue = 12));
        await AssertStepAsync(vm, "Luma NR +14 (+14)", () =>
            CommitDebouncedAsync(vm, clock, () => vm.LuminanceNr = 14));
        await AssertStepAsync(vm, "Grain +9 (+9)", () =>
            CommitDebouncedAsync(vm, clock, () => vm.Grain = 9));

        var profile = new RawProfileOptionViewModel(new DcpProfileOption(
            "History profile",
            new RawProfileSelection
            {
                Source = RawProfileSource.UserFile,
                Location = _fixture.Path("history.dcp"),
                ContentHash = new string('a', 64)
            },
            DcpProfileErrorCode.None,
            null));
        await AssertStepAsync(vm, "Profile: History profile", () =>
            vm.SelectRawProfileAsync(profile));

        await AssertStepAsync(vm, "White balance: Daylight", () =>
            CommitDebouncedAsync(
                vm,
                clock,
                () => vm.SelectedWhiteBalanceMode = "Daylight"));
        await TestWaits.UntilAsync(() => vm.IsWhiteBalanceReady);
        await AssertStepAsync(vm, "Auto white balance", () =>
            vm.AutoWhiteBalanceCommand.ExecuteAsync(null));
        await AssertStepAsync(vm, "White balance: Cloudy", () =>
            CommitDebouncedAsync(
                vm,
                clock,
                () => vm.SelectedWhiteBalanceMode = "Cloudy"));
        await AssertStepAsync(vm, "White balance pick", () =>
            vm.ApplyWhiteBalancePickAsync(0.5, 0.5));

        await AssertStepAsync(vm, "Preset: History preset", () =>
            vm.ApplyPresetAsync(preset.Id));
        await AssertStepAsync(vm, "Preset: None", () =>
            vm.ApplyPresetAsync(preset.Id));

        var source = await CreateImageAsync(
            catalog,
            "paste-source.jpg",
            new EditSettings { Vibrance = 22 });
        vm.Browse.SetImages([image, source]);
        vm.SelectedImage = source;
        await WaitForHistoryAsync(vm);
        vm.CopyEditSettingsCommand.Execute(null);
        vm.SelectedImage = image;
        await WaitForHistoryAsync(vm);
        await AssertStepAsync(vm, "Paste settings", () =>
            vm.PasteEditSettingsCommand.ExecuteAsync(null));
        await AssertStepAsync(vm, "Reset", () =>
            vm.ResetEditsCommand.ExecuteAsync(null));
    }

    [AvaloniaFact]
    public async Task ResetThenImmediateUndoWaitsForTheResetCommit()
    {
        using var catalog = await _fixture.CreateCatalogAsync("reset-undo");
        await using var vm = CreateViewModel(catalog);
        var image = await CreateImageAsync(catalog, "reset-undo.jpg");
        vm.SelectedImage = image;
        await WaitForHistoryAsync(vm);

        vm.OnCurveEditStarted();
        vm.CurrentCurve!.AddPointAndReturnIndex(0.5, 0.8);
        await vm.OnCurveChangedAsync();
        var reset = vm.ResetEditsCommand.ExecuteAsync(null);
        var undo = vm.UndoCommand.ExecuteAsync(null);

        await Task.WhenAll(reset, undo);
        Assert.Equal(0.8, image.EditSettings.Curve.Points[1].Y);
        Assert.Equal("Curve", Current(vm).Label);
    }

    [AvaloniaFact]
    public async Task SwitchingAwayAndBackRestoresListAndPosition()
    {
        using var catalog = await _fixture.CreateCatalogAsync("switch");
        var first = await SeedHistoryAsync(catalog, "first.jpg", 2, 1);
        var second = await SeedHistoryAsync(catalog, "second.jpg", 4, 2);
        await using var vm = CreateViewModel(catalog);
        vm.Browse.SetImages([first, second]);

        vm.SelectedImage = first;
        await WaitForHistoryAsync(vm);
        Assert.Equal("Exposure +1.00", Current(vm).Label);
        vm.SelectedImage = second;
        await WaitForHistoryAsync(vm);
        Assert.Equal("Exposure +2.00", Current(vm).Label);
        vm.SelectedImage = first;
        await WaitForHistoryAsync(vm);

        Assert.Equal(3, vm.HistoryEntries.Count);
        Assert.Equal("Exposure +1.00", Current(vm).Label);
        Assert.True(vm.CanUndo);
        Assert.True(vm.CanRedo);
    }

    [AvaloniaFact]
    public async Task BatchPasteIncludingSubjectThenEditKeepsBothStepsUndoable()
    {
        using var catalog = await _fixture.CreateCatalogAsync("batch");
        var clock = new TestTimeProvider();
        await using var vm = CreateViewModel(catalog, clock);
        var source = await CreateImageAsync(
            catalog,
            "batch-source.jpg",
            new EditSettings { Contrast = 24 });
        var target = await CreateImageAsync(catalog, "batch-target.jpg");
        vm.Browse.SetImages([source, target]);
        vm.SelectedImage = source;
        await WaitForHistoryAsync(vm);
        vm.CopyEditSettingsCommand.Execute(null);
        vm.SelectedImage = target;
        await WaitForHistoryAsync(vm);

        var paste = typeof(MainWindowViewModel).GetMethod(
            "PasteToSelectionAsync",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        vm.ConfirmBatchApplyAsync = _ => Task.FromResult(true);
        await (Task)paste.Invoke(vm, [new[] { target }])!;
        await CommitDebouncedAsync(vm, clock, () => vm.Exposure = 0.4);

        Assert.Equal(
            ["Exposure +0.40 (+0.40)", "Paste settings", "Original"],
            vm.HistoryEntries.Select(entry => entry.Label));
        await vm.UndoCommand.ExecuteAsync(null);
        Assert.Equal(24, target.EditSettings.Contrast);
        Assert.Equal(0, target.EditSettings.Exposure);
        await vm.UndoCommand.ExecuteAsync(null);
        Assert.Equal(0, target.EditSettings.Contrast);
    }

    [AvaloniaFact]
    public async Task VersionCreatedDuringPendingDebounceFlushesSourceAndStartsEmpty()
    {
        using var catalog = await _fixture.CreateCatalogAsync("version");
        var clock = new TestTimeProvider();
        await using var vm = CreateViewModel(catalog, clock);
        var source = await CreateImageAsync(catalog, "version.jpg");
        vm.Browse.SetImages([source]);
        vm.Browse.SelectOnly(source);
        vm.SelectedImage = source;
        await WaitForHistoryAsync(vm);

        vm.Exposure = 0.6;
        await vm.NewVersionFromCurrentCommand.ExecuteAsync(null);
        var version = Assert.IsType<ImageFile>(vm.SelectedImage);
        Assert.NotSame(source, version);
        clock.Advance(TimeSpan.FromMilliseconds(200));
        if (vm.PendingPreviewDebounceTask is { } pending) await pending;

        var sourceHistory = await catalog.LoadEditHistoryAsync(source.CatalogId);
        var versionHistory = await catalog.LoadEditHistoryAsync(version.CatalogId);
        Assert.Equal(["Original", "Exposure +0.60 (+0.60)"],
            sourceHistory.Entries.Select(entry => entry.Label));
        Assert.Empty(versionHistory.Entries);
    }

    [AvaloniaFact]
    public async Task SaveCurrentAsPresetDoesNotAddOrDivergeHistory()
    {
        using var catalog = await _fixture.CreateCatalogAsync("save-preset");
        var clock = new TestTimeProvider();
        await using var vm = CreateViewModel(catalog, clock);
        await vm.PresetService.UseDirectoryAsync(_fixture.Path("save-preset-presets"));
        var image = await CreateImageAsync(catalog, "save-preset.jpg");
        vm.Browse.SetImages([image]);
        vm.SelectedImage = image;
        await WaitForHistoryAsync(vm);

        await CommitDebouncedAsync(vm, clock, () => vm.Exposure = 0.3);
        var count = vm.HistoryEntries.Count;
        await vm.SaveCurrentAsPresetAsync("Current look");
        Assert.Equal(count, vm.HistoryEntries.Count);

        await CommitDebouncedAsync(vm, clock, () => vm.Contrast = 10);
        Assert.Equal(1, vm.HistoryEntries.Count(entry => entry.Label == "Original"));
        Assert.Equal("Contrast +10 (+10)", vm.HistoryEntries[0].Label);
    }

    [AvaloniaFact]
    public async Task SliderDragCommitsOneStepWithFinalValueAndTotalDelta()
    {
        using var catalog = await _fixture.CreateCatalogAsync("slider-drag");
        var clock = new TestTimeProvider();
        await using var vm = CreateViewModel(catalog, clock);
        var image = await CreateImageAsync(catalog, "slider-drag.jpg");
        vm.SelectedImage = image;
        await WaitForHistoryAsync(vm);

        vm.OnSliderEditStarted();
        foreach (var exposure in new[] { 0.1, 0.2, 0.3 })
        {
            vm.Exposure = exposure;
            clock.Advance(TimeSpan.FromMilliseconds(200));
            await Assert.IsAssignableFrom<Task>(vm.PendingPreviewDebounceTask);
            Assert.Empty(vm.HistoryEntries);
        }

        vm.OnSliderEditCompleted();
        clock.Advance(TimeSpan.FromMilliseconds(200));
        await Assert.IsAssignableFrom<Task>(vm.PendingPreviewDebounceTask);
        if (vm.PendingHistoryCommitTask is { } pending) await pending;

        Assert.Equal(
            ["Exposure +0.30 (+0.30)", "Original"],
            vm.HistoryEntries.Select(entry => entry.Label));
    }

    [AvaloniaFact]
    public async Task SliderChangeWithoutDragStillCommitsNormally()
    {
        using var catalog = await _fixture.CreateCatalogAsync("slider-no-drag");
        var clock = new TestTimeProvider();
        await using var vm = CreateViewModel(catalog, clock);
        var image = await CreateImageAsync(catalog, "slider-no-drag.jpg");
        vm.SelectedImage = image;
        await WaitForHistoryAsync(vm);

        await CommitDebouncedAsync(vm, clock, () => vm.Exposure = 0.2);

        Assert.Equal(
            ["Exposure +0.20 (+0.20)", "Original"],
            vm.HistoryEntries.Select(entry => entry.Label));
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

    private async Task<ImageFile> SeedHistoryAsync(
        CatalogService catalog,
        string name,
        double tip,
        int position)
    {
        var image = await CreateImageAsync(
            catalog,
            name,
            new EditSettings { Exposure = position });
        await catalog.SaveEditSettingsWithHistoryAsync(
            image.CatalogId,
            image.EditSettings,
            new CatalogEditHistoryMutation(
                -1,
                Enumerable.Range(0, (int)tip + 1)
                    .Select(index => new CatalogEditHistoryEntry(
                        index,
                        index == 0 ? "Original" : $"Exposure +{index}.00",
                        new EditSettings { Exposure = index }))
                    .ToArray(),
                position));
        return image;
    }

    private static async Task AssertStepAsync(
        MainWindowViewModel vm,
        string expectedLabel,
        Func<Task> commit)
    {
        var before = vm.HistoryEntries.Count(entry => entry.Label != "Original");
        await commit();
        if (vm.PendingHistoryCommitTask is { } pending) await pending;
        Assert.Equal(before + 1,
            vm.HistoryEntries.Count(entry => entry.Label != "Original"));
        Assert.Equal(expectedLabel, vm.HistoryEntries[0].Label);
    }

    private static async Task CommitDebouncedAsync(
        MainWindowViewModel vm,
        TestTimeProvider clock,
        Action change)
    {
        change();
        clock.Advance(TimeSpan.FromMilliseconds(200));
        await Assert.IsAssignableFrom<Task>(vm.PendingPreviewDebounceTask);
    }

    private static Task WaitForHistoryAsync(MainWindowViewModel vm) =>
        TestWaits.UntilAsync(() => vm.IsHistoryLoaded);

    private static EditHistoryEntry Current(MainWindowViewModel vm) =>
        Assert.Single(vm.HistoryEntries, entry => entry.IsCurrent);

    public void Dispose() => _fixture.Dispose();

    private sealed class TinyBaseLoader : IBaseImageLoader
    {
        public bool CanLoad(ImageFile file) => true;

        public BaseImage LoadPreviewBase(
            ImageFile file,
            BaseDecodeSettings decode,
            CancellationToken cancellationToken) =>
            Create(decode);

        BaseImageLoadOutcome IBaseImageLoader.LoadPreviewBaseWithOutcome(
            ImageFile file,
            BaseDecodeSettings decode,
            CancellationToken cancellationToken) =>
            BaseImageLoadOutcome.Loaded(Create(decode));

        public BaseImage? LoadFullBase(
            ImageFile file,
            BaseDecodeSettings decode,
            CancellationToken cancellationToken) =>
            Create(decode);

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
