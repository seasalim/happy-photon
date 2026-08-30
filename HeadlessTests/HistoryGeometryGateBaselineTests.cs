using Avalonia.Headless.XUnit;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

/// <summary>
/// Run 218 gate 3 instrument: history rows added by one rotate, one horizon drag,
/// one applied crop session, one cancelled crop session, and the Exposure control.
/// The assertions state the expected values for the code under test; the printed
/// counts are the record.
/// </summary>
public sealed class HistoryGeometryGateBaselineTests : IDisposable
{
    private readonly CatalogVmFixture _fixture = new("history-geometry-gate");

    [AvaloniaFact]
    public async Task GeometryGesturesAddTheExpectedRows()
    {
        using var catalog = await _fixture.CreateCatalogAsync("geometry-gate");
        var clock = new TestTimeProvider();
        await using var vm = CreateViewModel(catalog, clock);
        var image = await CreateImageAsync(catalog, "geometry.jpg");
        vm.Browse.SetImages([image]);
        vm.SelectedImage = image;
        await TestWaits.UntilAsync(() => vm.IsHistoryLoaded);

        var control = await RowsAddedAsync(vm, () =>
            CommitDebouncedAsync(vm, clock, () => vm.Exposure = 0.3));

        var rotate = await RowsAddedAsync(vm, async () =>
        {
            vm.RotateRightCommand.Execute(null);
            await SettleDebounceAsync(vm, clock);
        });

        var horizonDrag = await RowsAddedAsync(vm, async () =>
        {
            vm.OnSliderEditStarted();
            foreach (var degrees in new[] { 0.5, 1.0, 1.5 })
            {
                vm.HorizonRotation = degrees;
                await SettleDebounceAsync(vm, clock);
            }
            vm.OnSliderEditCompleted();
            await SettleDebounceAsync(vm, clock);
        });

        var cropApplied = await RowsAddedAsync(vm, async () =>
        {
            await vm.ToggleCropModeCommand.ExecuteAsync(null);
            await SettleDebounceAsync(vm, clock);
            vm.HorizonRotation = 2.0;
            await SettleDebounceAsync(vm, clock);
            vm.CurrentCrop = new CropRegion { Left = .1, Top = .1, Right = .9, Bottom = .9 };
            await vm.ApplyCropCommand.ExecuteAsync(null);
            await SettleDebounceAsync(vm, clock);
        });

        var cropCancelled = await RowsAddedAsync(vm, async () =>
        {
            await vm.ToggleCropModeCommand.ExecuteAsync(null);
            await SettleDebounceAsync(vm, clock);
            vm.HorizonRotation = 3.0;
            await SettleDebounceAsync(vm, clock);
            vm.CurrentCrop = new CropRegion { Left = .2, Top = .2, Right = .8, Bottom = .8 };
            await vm.CancelCropCommand.ExecuteAsync(null);
            await SettleDebounceAsync(vm, clock);
        });

        Console.WriteLine(
            $"gate3 rows: control={control} rotate={rotate} horizonDrag={horizonDrag} " +
            $"cropApplied={cropApplied} cropCancelled={cropCancelled}");

        // Reference (12110dc): 1 / 0 / 0 / 0 / 0. Slice 2 target: 1 / 1 / 1 / 1 / 0.
        Assert.Equal(1, control);
        Assert.Equal(ExpectedRotateRows, rotate);
        Assert.Equal(ExpectedHorizonRows, horizonDrag);
        Assert.Equal(ExpectedCropRows, cropApplied);
        Assert.Equal(0, cropCancelled);
    }

    private const int ExpectedRotateRows = 1;
    private const int ExpectedHorizonRows = 1;
    private const int ExpectedCropRows = 1;

    private static async Task<int> RowsAddedAsync(MainWindowViewModel vm, Func<Task> act)
    {
        var before = vm.HistoryEntries.Count;
        await act();
        if (vm.PendingHistoryCommitTask is { } pending) await pending;
        // The first commit also seeds "Original"; count only the step itself.
        var added = vm.HistoryEntries.Count - before;
        return before == 0 && added > 0 ? added - 1 : added;
    }

    private static async Task CommitDebouncedAsync(
        MainWindowViewModel vm,
        TestTimeProvider clock,
        Action change)
    {
        change();
        await SettleDebounceAsync(vm, clock);
    }

    private static async Task SettleDebounceAsync(
        MainWindowViewModel vm,
        TestTimeProvider clock)
    {
        clock.Advance(TimeSpan.FromMilliseconds(200));
        if (vm.PendingPreviewDebounceTask is { } debounce) await debounce;
        if (vm.PendingHistoryCommitTask is { } pending) await pending;
    }

    private MainWindowViewModel CreateViewModel(
        CatalogService catalog,
        TimeProvider clock)
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

    private async Task<ImageFile> CreateImageAsync(CatalogService catalog, string name)
    {
        var image = new ImageFile(_fixture.Path(name)) { EditSettings = new EditSettings() };
        image.CatalogId = await catalog.GetOrCreateImageAsync(image.FilePath);
        await catalog.SaveEditSettingsAsync(image.CatalogId, image.EditSettings);
        return image;
    }

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
