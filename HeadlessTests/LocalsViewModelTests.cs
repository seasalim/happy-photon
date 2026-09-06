using Avalonia;
using Avalonia.Headless.XUnit;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

public sealed partial class LocalsViewModelTests : IDisposable
{
    private readonly CatalogVmFixture _fixture = new("locals-vm");

    [AvaloniaFact]
    public async Task LocalOperationsPreserveHistoryAndPresetPolicy()
    {
        using var catalog = await _fixture.CreateCatalogAsync();
        await using var vm = CreateVm(catalog);
        await Prepare(vm, catalog);
        await vm.ToggleLocalsModeCommand.ExecuteAsync(null);
        Assert.True(vm.CanAddLocal);
        await vm.PlaceLocalAtCenterCommand.ExecuteAsync(null);
        var id = Assert.Single(vm.Locals).Id;
        Assert.Equal("Add Linear", vm.HistoryEntries[0].Label);
        await vm.ToggleLocalEnabledCommand.ExecuteAsync(vm.SelectedLocal);
        Assert.False(vm.SelectedLocal!.Enabled);
        await vm.UndoCommand.ExecuteAsync(null);
        Assert.True(vm.SelectedLocal!.Enabled);
        Assert.Equal(id, vm.SelectedLocal.Id);
        await vm.PresetService.UseDirectoryAsync(_fixture.Path("presets"));
        var preset = await vm.PresetService.SaveUserPresetAsync("Test", new EditSettings { Exposure = 1 });
        await vm.ApplyPresetAsync(preset.Id);
        Assert.Equal(id, vm.SelectedLocal.Id);
        await vm.ApplyPresetAsync(preset.Id);
        Assert.Equal(id, vm.SelectedLocal.Id);
        await vm.ResetEditsCommand.ExecuteAsync(null);
        Assert.Empty(vm.Locals);
        await vm.UndoCommand.ExecuteAsync(null);
        Assert.Equal(id, vm.SelectedLocal!.Id);
        vm.CloseLocalsCommand.Execute(null);
        Assert.True(vm.HasLocals);
    }

    [AvaloniaFact]
    public async Task DragIsOneStepAndUndoWithoutHistoryOnlyCancels()
    {
        using var catalog = await _fixture.CreateCatalogAsync();
        await using var vm = CreateVm(catalog);
        await Prepare(vm, catalog);
        await vm.ToggleLocalsModeCommand.ExecuteAsync(null);
        Assert.NotNull(vm.LocalsFrame);
        vm.AddLinearCommand.Execute(null);
        Assert.True(vm.BeginLocalsGesture(LocalHandle.Create, new(.2, .2)));
        vm.MoveLocalsGesture(new(.8, .8), 100);
        Assert.True(vm.UndoCommand.CanExecute(null));
        await vm.UndoCommand.ExecuteAsync(null);
        Assert.Empty(vm.Locals);
        vm.AddLinearCommand.Execute(null);
        vm.BeginLocalsGesture(LocalHandle.Create, new(.2, .2));
        vm.MoveLocalsGesture(new(.201, .201), 1);
        await vm.CompleteLocalsGestureAsync();
        Assert.Empty(vm.Locals);
        vm.AddLinearCommand.Execute(null);
        vm.BeginLocalsGesture(LocalHandle.Create, new(.2, .2));
        vm.MoveLocalsGesture(new(.8, .8), 100);
        await vm.CompleteLocalsGestureAsync();
        Assert.Single(vm.Locals);
        Assert.Equal(2, vm.HistoryEntries.Count);
        var before = vm.SelectedLocal! with { };
        vm.BeginLocalsGesture(LocalHandle.Center, new(.5, .5));
        vm.MoveLocalsGesture(new(.6, .7), 100);
        vm.EscapeLocals();
        Assert.Equal(before, vm.SelectedLocal);
        Assert.Equal(2, vm.HistoryEntries.Count);
        vm.RotateRightCommand.Execute(null);
        if (vm.PendingHistoryCommitTask is { } pending) await pending;
        Assert.Equal((before.Angle + 90) % 360, vm.SelectedLocal!.Angle);
    }

    private MainWindowViewModel CreateVm(CatalogService catalog, TimeProvider? clock = null,
        bool raw = false, bool mono = false)
    {
        var vm = _fixture.CreateViewModel(catalog, new LocalTestLoader(raw, mono),
            _ => Task.CompletedTask, new TestSourceAvailabilityService(SourceAvailability.AvailableLocally), timeProvider: clock);
        vm.IsDevelopMode = true;
        return vm;
    }
    private async Task Prepare(MainWindowViewModel vm, CatalogService catalog)
    {
        var image = new ImageFile(_fixture.Path("photo.jpg"));
        image.CatalogId = await catalog.GetOrCreateImageAsync(image.FilePath);
        vm.Browse.SetImages([image]);
        vm.SelectedImage = image;
        await TestWaits.UntilAsync(() => vm.IsHistoryLoaded && vm.PreviewImage != null);
    }
    public void Dispose() => _fixture.Dispose();
}

internal sealed class LocalTestLoader(bool raw = false, bool mono = false) : IBaseImageLoader
{
    public bool CanLoad(ImageFile file) => true;
    public BaseImageLoadOutcome LoadPreviewBaseWithOutcome(ImageFile file,
        BaseDecodeSettings decode, CancellationToken cancellationToken) =>
        BaseImageLoadOutcome.Loaded(Create(decode));
    public BaseImage LoadFullBase(ImageFile file, BaseDecodeSettings decode,
        CancellationToken cancellationToken) => Create(decode);
    private BaseImage Create(BaseDecodeSettings decode) => new(
        new MagickImage(MagickColors.Gray, 64, 48) { ColorSpace = ColorSpace.RGB },
        new BaseImageInfo(raw ? BaseSourceKind.RawLibRaw : BaseSourceKind.Standard, raw, decode, null, null,
            6504, 0, false, null, 1, 64, 48) { IsMonochrome = mono });
}
