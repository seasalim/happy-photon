using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.Views;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

public sealed partial class LocalsViewModelTests
{
    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task GlobalLockRestoresPinnedEnablement(bool raw, bool mono)
    {
        using var catalog = await _fixture.CreateCatalogAsync();
        await using var vm = CreateVm(catalog, raw: raw, mono: mono);
        vm.ShowWorkspaceReady(HappyPhoton.ViewModels.MainWindowViewModel.CurrentFirstRunExperienceVersion);
        await Prepare(vm, catalog);
        using var pixels = new MagickImage(MagickColors.Gray, 64, 48);
        vm.ApplyPreviewRefresh(vm.SelectedImage!, BitmapConversionService.ConvertToBitmap(pixels)!,
            new HistogramData(), true, null, vm.LatestPreviewOutcomeGeneration,
            isRawSource: raw, isMonochrome: mono);
        var panel = new DevelopEditPanel { DataContext = vm };
        using var scope = new TestUiScope(new Window { Width = 250, Height = 660, Content = panel });
        string[] names = ["RawProfilePicker", "WhiteBalanceControls", "BrightnessSlider",
            "SaturationSlider", "VibranceSlider", "HighlightHandlingRow", "ToneCurveView",
            "MixerEditGroup", "DetailEditGroup", "EffectsEditGroup", "GeometryEditGroup", "LensEditGroup"];
        var controls = names.Select(name => panel.GetVisualDescendants().OfType<Control>()
            .Single(control => control.Name == name)).Concat(panel.GetVisualDescendants()
            .OfType<CompactSlider>().Where(slider => slider.Label is "Exposure" or "Contrast" or "Shadows" or "Highlights")
            .Where(slider => !slider.GetVisualAncestors().OfType<LocalsEditSection>().Any())).ToArray();
        // BASELINE at 5a9af39: entry and transient views retained these states.
        bool[] baseline = (raw, mono) switch
        {
            (false, false) => [true, true, true, true, true, false, true, true, true, true, true, true, true, true, true, true],
            (true, false) => [true, true, false, true, true, true, true, true, true, true, true, true, true, true, true, true],
            _ => [false, false, false, false, false, true, true, false, true, true, true, true, true, true, true, true]
        };
        Assert.Equal(baseline, controls.Select(control => control.IsEffectivelyEnabled));
        var live = new Control[] { panel.FindControl<Border>("DevelopScopeBox")!,
            panel.FindControl<DevelopActionBar>("DevelopActionBar")! };
        Assert.All(live, control => Assert.True(control.IsEffectivelyEnabled));
        var before = vm.SelectedImage!.EditSettings.Clone();
        await vm.ToggleLocalsModeCommand.ExecuteAsync(null);
        Dispatcher.UIThread.RunJobs();
        Assert.All(controls, control => Assert.False(control.IsEffectivelyEnabled));
        Assert.Equal(1, panel.FindControl<StackPanel>("WhiteBalanceControls")!.Opacity);
        await vm.ToggleBeforeAfterCommand.ExecuteAsync(null);
        Assert.All(controls, control => Assert.False(control.IsEffectivelyEnabled));
        Assert.All(live, control => Assert.True(control.IsEffectivelyEnabled));
        await vm.ResumeLocalEditingCommand.ExecuteAsync(null);
        vm.SelectedImage!.SourceRequiresHydration = true;
        Assert.False(vm.CanEditLocals);
        Assert.All(controls, control => Assert.False(control.IsEffectivelyEnabled));
        vm.SelectedImage.SourceRequiresHydration = false;
        vm.CloseLocalsCommand.Execute(null);
        Assert.Equal(baseline, controls.Select(control => control.IsEffectivelyEnabled));
        Assert.True(before.HasSameEdits(vm.SelectedImage.EditSettings));
        if (mono)
        {
            await vm.ToggleLocalsModeCommand.ExecuteAsync(null);
            ShowcaseTestHelper.Capture("develop-locals-monochrome-lock", scope,
                new Avalonia.PixelSize(250, 660), Avalonia.Styling.ThemeVariant.Dark);
        }
    }
}
