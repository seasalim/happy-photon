using Avalonia;
using Avalonia.Headless.XUnit;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using Xunit;

namespace HappyPhoton.Tests;

public sealed partial class LocalsViewModelTests
{
    private MainWindowViewModel CreateHueVm(CatalogService catalog) => _fixture.CreateViewModel(catalog,
        new LocalTestLoader(saturated: true), _ => Task.CompletedTask,
        new TestSourceAvailabilityService(SourceAvailability.AvailableLocally));

    [AvaloniaFact]
    public async Task HuePickCommitsOncePreservesWidthsAndRestoresMask()
    {
        using var catalog = await _fixture.CreateCatalogAsync();
        await using var vm = CreateHueVm(catalog); vm.IsDevelopMode = true;
        await Prepare(vm, catalog); await vm.ToggleLocalsModeCommand.ExecuteAsync(null);
        await vm.PlaceLocalAtCenterCommand.ExecuteAsync(null);
        Assert.False(vm.IsLocalHueExpanded); Assert.Null(vm.SelectedLocal!.Hue);
        await vm.ToggleLocalHueCommand.ExecuteAsync(null);
        Assert.Equal(new HueRange { Enabled = true }, vm.SelectedLocal.Hue);
        vm.LocalHueWidth = 80; vm.LocalHueSoftness = 12;
        await vm.ToggleLocalHueCommand.ExecuteAsync(null);
        var before = vm.SelectedLocal.Hue; var count = vm.HistoryEntries.Count;
        Assert.True(vm.CanPickLocalHue); Assert.Empty(vm.LocalHuePickAvailability);
        vm.AddLinearCommand.Execute(null); vm.ToggleLocalHuePickCommand.Execute(null);
        Assert.Equal("Click a color in the image · Escape cancels", vm.LocalHuePickAvailability);
        Assert.False(vm.IsLocalCreationArmed); Assert.True(vm.ShowLocalMask); Assert.True(vm.IsLocalHuePicking);
        await vm.PickLocalHueAsync(new(.5, .5));
        Assert.False(vm.IsLocalHuePicking); Assert.False(vm.ShowLocalMask);
        Assert.Empty(vm.LocalHuePickAvailability);
        Assert.True(vm.IsLocalHueEnabled); Assert.Equal(80, vm.LocalHueWidth); Assert.Equal(12, vm.LocalHueSoftness);
        Assert.Equal(count + 1, vm.HistoryEntries.Count); Assert.Equal("Pick Hue", vm.HistoryEntries[0].Label);
        await vm.UndoCommand.ExecuteAsync(null); Assert.Equal(before, vm.SelectedLocal!.Hue);
        await vm.RedoCommand.ExecuteAsync(null); var hue = vm.SelectedLocal!.Hue;
        await vm.ResetLocalAdjustmentsCommand.ExecuteAsync(null); Assert.Equal(hue, vm.SelectedLocal!.Hue);
        vm.CopyEditSettingsCommand.Execute(null); await vm.PasteEditSettingsCommand.ExecuteAsync(null);
        Assert.Equal(hue, vm.SelectedLocal!.Hue);
        await vm.PresetService.UseDirectoryAsync(_fixture.Path("hue-presets"));
        var preset = await vm.PresetService.SaveUserPresetAsync("Hue", new() { Exposure = 1 });
        await vm.ApplyPresetAsync(preset.Id); Assert.Equal(hue, vm.SelectedLocal!.Hue);
        var original = vm.SelectedImage!;
        await vm.NewVersionFromCurrentCommand.ExecuteAsync(null); Assert.Equal(hue, vm.SelectedLocal!.Hue);
        vm.LocalHueCenter = 350; Assert.Equal(hue, original.EditSettings.Locals![0].Hue);
        await vm.ResetEditsCommand.ExecuteAsync(null); Assert.Empty(vm.Locals);
        await vm.UndoCommand.ExecuteAsync(null); Assert.Equal(350, vm.LocalHueCenter);
    }

    [AvaloniaTheory]
    [InlineData("escape")] [InlineData("creation")] [InlineData("wb")] [InlineData("crop")]
    [InlineData("exit")] [InlineData("gesture")] [InlineData("settings")] [InlineData("selection")]
    [InlineData("navigation")] [InlineData("transient")] [InlineData("base")] [InlineData("reverted")]
    public async Task PendingHuePickCannotCrossCancellationOrSettingsChange(string action)
    {
        using var catalog = await _fixture.CreateCatalogAsync();
        await using var vm = CreateHueVm(catalog); vm.IsDevelopMode = true;
        await Prepare(vm, catalog); await vm.ToggleLocalsModeCommand.ExecuteAsync(null);
        await vm.PlaceLocalAtCenterCommand.ExecuteAsync(null);
        await vm.PlaceLocalAtCenterCommand.ExecuteAsync(null);
        var target = vm.SelectedLocal!;
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        vm.LocalHuePickGateAsync = () => release.Task;
        vm.ShowLocalMask = true; vm.ToggleLocalHuePickCommand.Execute(null);
        var pending = vm.PickLocalHueAsync(new(.5, .5));
        try
        {
            switch (action)
            {
                case "escape": vm.EscapeLocals(); Assert.True(vm.IsLocalsMode); break;
                case "creation": vm.AddRadialCommand.Execute(null); break;
                case "wb": vm.IsWhiteBalancePicking = true; break;
                case "crop": await vm.ToggleCropModeCommand.ExecuteAsync(null); break;
                case "exit": vm.CloseLocalsCommand.Execute(null); break;
                case "gesture": vm.BeginLocalsGesture(LocalHandle.Center, new(.5, .5)); break;
                case "base":
                    var surface = vm.PreviewImage; vm.PreviewImage = null; vm.PreviewImage = surface; break;
                case "reverted":
                    await vm.ToggleLocalHueCommand.ExecuteAsync(null); await vm.ToggleLocalHueCommand.ExecuteAsync(null); break;
                case "settings": vm.SelectedLocal!.Luminance = new() { Enabled = true, Lower = .5 }; break;
                case "selection": vm.SelectedLocal = vm.Locals[0]; break;
                case "navigation": vm.SelectedImage = null; break;
                case "transient": vm.IsFullScreenMode = true; break;
            }
        }
        finally { release.TrySetResult(); }
        await pending;
        if (action != "reverted") Assert.Null(target.Hue);
        else Assert.False(target.Hue!.Enabled);
        Assert.True(vm.ShowLocalMask);
        if (action is not ("settings" or "base")) Assert.False(vm.IsLocalHuePicking);
    }

    [AvaloniaTheory]
    [InlineData(false)] [InlineData(true)]
    public async Task PendingHuePickCannotCrossSurfaceResizeOnSameBase(bool revert)
    {
        using var catalog = await _fixture.CreateCatalogAsync();
        await using var vm = _fixture.CreateViewModel(catalog, new CountingPairLoader(), _ => Task.CompletedTask,
            new TestSourceAvailabilityService(SourceAvailability.AvailableLocally));
        vm.IsDevelopMode = true;
        await Prepare(vm, catalog); await vm.ToggleLocalsModeCommand.ExecuteAsync(null);
        await vm.PlaceLocalAtCenterCommand.ExecuteAsync(null);
        var image = vm.SelectedImage!;
        var previews = vm.ImageService.Previews;
        var parent = previews.TryGetPreviewRenderIdentity(vm.PreviewImage!)!;
        using var first = await previews.RenderRestingPreviewAsync(image, image.EditSettings, 240, parent, CancellationToken.None);
        using var second = await previews.RenderRestingPreviewAsync(image, image.EditSettings, 200, parent, CancellationToken.None);
        Assert.NotNull(first); Assert.NotNull(second);
        using var firstLease = previews.AcquireLocalRangeBase(image, image.EditSettings, 240, first.Bitmap);
        using var secondLease = previews.AcquireLocalRangeBase(image, image.EditSettings, 200, second.Bitmap);
        Assert.NotNull(firstLease); Assert.NotNull(secondLease); Assert.Same(firstLease.Base, secondLease.Base);
        vm.PreviewImage = first.DetachBitmap();
        var original = vm.PreviewImage;
        vm.ToggleLocalHuePickCommand.Execute(null);
        var count = vm.HistoryEntries.Count;
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        vm.LocalHuePickGateAsync = () => release.Task;
        var pending = vm.PickLocalHueAsync(new(.5, .5));
        try
        {
            vm.PreviewImage = second.DetachBitmap();
            Assert.True(vm.CanPickLocalHue);
            if (revert) vm.PreviewImage = original;
        }
        finally { release.TrySetResult(); }
        await pending;
        Assert.Null(vm.SelectedLocal!.Hue); Assert.Equal(count, vm.HistoryEntries.Count);
        Assert.True(vm.IsLocalHuePicking);
        vm.LocalHuePickGateAsync = null;
        await vm.PickLocalHueAsync(new(.5, .5));
        Assert.True(vm.IsLocalHueEnabled); Assert.Equal(count + 1, vm.HistoryEntries.Count);
    }

    [AvaloniaFact]
    public async Task RejectionKeepsPickerArmedAndMonochromePreservesDormantValues()
    {
        using var catalog = await _fixture.CreateCatalogAsync();
        await using var vm = CreateVm(catalog);
        await Prepare(vm, catalog); await vm.ToggleLocalsModeCommand.ExecuteAsync(null);
        await vm.PlaceLocalAtCenterCommand.ExecuteAsync(null);
        vm.ToggleLocalHuePickCommand.Execute(null);
        await vm.PickLocalHueAsync(new(-.1, .5)); Assert.True(vm.IsLocalHuePicking); Assert.Null(vm.SelectedLocal!.Hue);
        await vm.PickLocalHueAsync(new(.5, .5)); Assert.True(vm.IsLocalHuePicking); Assert.Null(vm.SelectedLocal.Hue);
        vm.EscapeLocals(); Assert.False(vm.ShowLocalMask);
        await using var mono = CreateVm(catalog, raw: true, mono: true);
        await Prepare(mono, catalog); await mono.ToggleLocalsModeCommand.ExecuteAsync(null);
        await mono.PlaceLocalAtCenterCommand.ExecuteAsync(null);
        mono.SelectedLocal!.Hue = new() { Enabled = true, Center = 350 };
        Assert.False(mono.CanEditLocalHue); Assert.False(mono.CanPickLocalHue);
        Assert.Equal("Monochrome RAW — color controls unavailable", mono.LocalHuePickAvailability);
        await mono.ToggleLocalHueCommand.ExecuteAsync(null); Assert.Equal(350, mono.LocalHueCenter);
    }
}
