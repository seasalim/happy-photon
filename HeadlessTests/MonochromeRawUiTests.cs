using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using HappyPhoton.Views;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class MonochromeRawUiTests : IDisposable
{
    private readonly CatalogVmFixture _fixture = new("mono-ui");

    [AvaloniaFact]
    public async Task RefreshCapability_DisablesColorControlsAndInstallsOnce()
    {
        using var catalog = await _fixture.CreateCatalogAsync();
        var loader = new BaseLoaderRouter(
            new RawBaseLoader(isAvailable: false),
            new StandardBaseLoader(
                (_, _) => new MagickImage(MagickColors.Gray, 16, 12)));
        var vm = _fixture.CreateViewModel(
            catalog,
            loader,
            loadMetadataAsync: _ => Task.CompletedTask,
            availabilityService: new TestSourceAvailabilityService(
                SourceAvailability.AvailableLocally));
        vm.IsDevelopMode = true;
        var image = new ImageFile(_fixture.Path("mono.dng"));
        vm.SelectedImage = image;
        vm.ActiveCurveChannel = ToneCurveChannel.Red;
        var statusInstalls = 0;
        vm.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(vm.TransientStatus) &&
                vm.TransientStatus != null)
            {
                statusInstalls++;
            }
        };

        ApplyRefresh(vm, image);
        ApplyRefresh(vm, image);

        Assert.True(vm.IsMonochromeSource);
        Assert.False(vm.IsColorEditingEnabled);
        Assert.Equal(ToneCurveChannel.Composite, vm.ActiveCurveChannel);
        vm.ActiveCurveChannel = ToneCurveChannel.Blue;
        Assert.Equal(ToneCurveChannel.Composite, vm.ActiveCurveChannel);
        Assert.False(vm.AutoWhiteBalanceCommand.CanExecute(null));
        Assert.False(vm.ToggleWhiteBalancePickerCommand.CanExecute(null));
        Assert.Equal(1, statusInstalls);
        Assert.Contains("Monochrome RAW", vm.TransientStatus);

        var panel = new DevelopEditPanel { DataContext = vm };
        var window = new Window
        {
            Width = 250,
            Height = 660,
            Content = panel
        };
        window.Show();
        panel.Measure(new Size(250, 660));
        panel.Arrange(new Rect(0, 0, 250, 660));

        Assert.False(panel.FindControl<RawProfilePicker>("RawProfilePicker")!.IsEnabled);
        Assert.False(panel.FindControl<StackPanel>("WhiteBalanceControls")!.IsEnabled);
        Assert.False(panel.FindControl<CompactSlider>("SaturationSlider")!.IsEnabled);
        Assert.False(panel.FindControl<CompactSlider>("VibranceSlider")!.IsEnabled);
        Assert.False(panel.FindControl<MixerEditGroup>("MixerEditGroup")!.IsEnabled);
        var curve = panel.FindControl<CurveView>("ToneCurveView")!;
        Assert.True(curve.FindControl<Button>("CompositeChannelButton")!.IsEnabled);
        Assert.False(curve.FindControl<Button>("RedChannelButton")!.IsEnabled);
        Assert.False(curve.FindControl<Button>("GreenChannelButton")!.IsEnabled);
        Assert.False(curve.FindControl<Button>("BlueChannelButton")!.IsEnabled);

        window.Close();
        panel.DataContext = null;
        await vm.DisposeAsync();
    }

    [AvaloniaFact]
    public async Task CapabilityInstallMidCurveGesture_DiscardsTheGesture()
    {
        using var catalog = await _fixture.CreateCatalogAsync();
        var loader = new BaseLoaderRouter(
            new RawBaseLoader(isAvailable: false),
            new StandardBaseLoader(
                (_, _) => new MagickImage(MagickColors.Gray, 16, 12)));
        var vm = _fixture.CreateViewModel(
            catalog,
            loader,
            loadMetadataAsync: _ => Task.CompletedTask,
            availabilityService: new TestSourceAvailabilityService(
                SourceAvailability.AvailableLocally));
        vm.IsDevelopMode = true;
        var image = new ImageFile(_fixture.Path("mono-gesture.dng"));
        vm.SelectedImage = image;
        vm.ActiveCurveChannel = ToneCurveChannel.Red;
        vm.OnCurveEditStarted();

        ApplyRefresh(vm, image);
        Assert.Equal(ToneCurveChannel.Composite, vm.ActiveCurveChannel);
        vm.CurrentCurve!.AddPointAndReturnIndex(0.3, 0.9);
        await vm.OnCurveChangedAsync();

        Assert.True(image.EditSettings.Curve.IsIdentity());
        Assert.Null(image.EditSettings.CurveRed);
        Assert.False(image.HasEdits);

        vm.OnCurveEditStarted();
        vm.CurrentCurve!.AddPointAndReturnIndex(0.3, 0.9);
        await vm.OnCurveChangedAsync();
        Assert.False(image.EditSettings.Curve.IsIdentity());

        await vm.DisposeAsync();
    }

    public void Dispose() => _fixture.Dispose();

    private static void ApplyRefresh(MainWindowViewModel vm, ImageFile image)
    {
        using var pixels = new MagickImage(MagickColors.Gray, 16, 12);
        var bitmap = BitmapConversionService.ConvertToBitmap(pixels)!;
        vm.ApplyPreviewRefresh(
            image,
            bitmap,
            new HistogramData(),
            hasHistogram: true,
            rawHistogram: null,
            vm.LatestPreviewOutcomeGeneration,
            isRawSource: true,
            isMonochrome: true);
    }
}
