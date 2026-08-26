using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Avalonia.Threading;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using HappyPhoton.Views;
using Xunit;
using Ellipse = Avalonia.Controls.Shapes.Ellipse;

namespace HappyPhoton.Tests;

public sealed class EffectsControlTests : IDisposable
{
    private readonly TemporaryDirectory _root = new();

    [AvaloniaFact]
    public async Task EffectsGroup_MatchesPanelOrderAndControlStates()
    {
        using var catalog = new CatalogService(Path.Combine(_root.Path, "catalog"));
        await catalog.InitializeAsync();
        await using var vm = new MainWindowViewModel(
            catalog,
            new NullBaseLoader(),
            loadMetadataAsync: _ => Task.CompletedTask);
        var panel = new DevelopEditPanel { DataContext = vm };
        var window = new Window { Width = 250, Height = 820, Content = panel };
        window.Show();
        vm.IsDevelopMode = true;
        vm.SelectedImage = new ImageFile(Path.Combine(_root.Path, "photo.jpg"));
        Dispatcher.UIThread.RunJobs();

        var detail = panel.FindControl<DetailEditGroup>("DetailEditGroup")!;
        var effects = panel.FindControl<EffectsEditGroup>("EffectsEditGroup")!;
        var stack = Assert.IsType<StackPanel>(detail.Parent);
        Assert.Equal(
            stack.Children.IndexOf(detail) + 1,
            stack.Children.IndexOf(effects));

        var vignette = effects.FindControl<CompactSlider>("VignetteSlider")!;
        var midpoint = effects.FindControl<CompactSlider>("MidpointSlider")!;
        var midpointRow = effects.FindControl<Grid>("MidpointRow")!;
        var grain = effects.FindControl<CompactSlider>("GrainSlider")!;
        var sizes = effects.FindControl<ListBox>("GrainSizeControl")!;
        Assert.Equal((-100, 100), (vignette.Minimum, vignette.Maximum));
        Assert.Equal((0, 100), (midpoint.Minimum, midpoint.Maximum));
        Assert.Equal((0, 100), (grain.Minimum, grain.Maximum));
        Assert.True(vignette.EnableDoubleClickReset);
        Assert.False(midpointRow.IsEnabled);
        Assert.Equal(0.32, midpointRow.Opacity);
        Assert.Equal(22, sizes.Height);
        Assert.Equal(new Thickness(2), sizes.Padding);
        Assert.Equal(new CornerRadius(4), sizes.CornerRadius);
        Assert.Equal(new Thickness(0), sizes.BorderThickness);
        Assert.Equal(3, sizes.ItemCount);
        Assert.Equal(GrainSize.Medium, sizes.SelectedItem);

        vm.Vignette = -1;
        Dispatcher.UIThread.RunJobs();
        Assert.True(midpointRow.IsEnabled);
        Assert.Equal(1, midpointRow.Opacity);

        await vm.ResetEditsCommand.ExecuteAsync(null);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(0, vm.Vignette);
        Assert.Equal(50, vm.Midpoint);
        Assert.Equal(0, vm.Grain);
        Assert.Equal(GrainSize.Medium, sizes.SelectedItem);
        Assert.True(effects.IsVisible);

        window.Close();
        panel.DataContext = null;
    }

    [AvaloniaFact]
    public async Task MixerGroup_SwitchesBandsResetsValuesAndResetsSelectionState()
    {
        using var catalog = new CatalogService(Path.Combine(_root.Path, "mixer-catalog"));
        await catalog.InitializeAsync();
        await using var vm = new MainWindowViewModel(
            catalog,
            new NullBaseLoader(),
            loadMetadataAsync: _ => Task.CompletedTask);
        var panel = new DevelopEditPanel { DataContext = vm };
        var window = new Window { Width = 250, Height = 1_800, Content = panel };
        window.Show();
        vm.IsDevelopMode = true;
        vm.SelectedImage = new ImageFile(Path.Combine(_root.Path, "mixer-photo.jpg"));
        Dispatcher.UIThread.RunJobs();

        var curve = panel.FindControl<CurveView>("ToneCurveView")!;
        var mixer = panel.FindControl<MixerEditGroup>("MixerEditGroup")!;
        var detail = panel.FindControl<DetailEditGroup>("DetailEditGroup")!;
        var stack = Assert.IsType<StackPanel>(mixer.Parent);
        Assert.Equal(stack.Children.IndexOf(curve) + 1, stack.Children.IndexOf(mixer));
        Assert.Equal(stack.Children.IndexOf(mixer) + 1, stack.Children.IndexOf(detail));

        var hue = mixer.FindControl<CompactSlider>("MixerHueSlider")!;
        var saturation = mixer.FindControl<CompactSlider>("MixerSaturationSlider")!;
        var luminance = mixer.FindControl<CompactSlider>("MixerLuminanceSlider")!;
        var orangeButton = mixer.FindControl<Button>("OrangeMixerButton")!;
        var touched = mixer.FindControl<Ellipse>("OrangeMixerTouchedDot")!;
        Assert.Null(mixer.FindControl<Button>("MixerResetButton"));
        Assert.Equal((-100, 100), (hue.Minimum, hue.Maximum));
        Assert.Equal((-100, 100), (saturation.Minimum, saturation.Maximum));
        Assert.Equal((-100, 100), (luminance.Minimum, luminance.Maximum));
        Assert.True(hue.EnableDoubleClickReset);
        Assert.True(saturation.EnableDoubleClickReset);
        Assert.True(luminance.EnableDoubleClickReset);
        Assert.NotNull(hue.TrackBrush);
        Assert.Equal(ColorMixerBand.Red, vm.ActiveMixerBand);

        vm.SelectMixerBandCommand.Execute(ColorMixerBand.Orange);
        vm.MixerSaturation = 22;
        Dispatcher.UIThread.RunJobs();
        Assert.Contains("active", orangeButton.Classes);
        Assert.True(touched.IsVisible);

        vm.SelectMixerBandCommand.Execute(ColorMixerBand.Blue);
        vm.MixerSaturation = -31;
        vm.SelectMixerBandCommand.Execute(ColorMixerBand.Orange);
        Assert.Equal(22, vm.MixerSaturation);

        var layout = saturation.FindControl<Grid>("LayoutGrid")!;
        PointerPressedEventArgs? samplePress = null;
        window.AddHandler(
            InputElement.PointerPressedEvent,
            (_, args) => samplePress = args,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
        window.MouseDown(new Point(10, 10), MouseButton.Left, RawInputModifiers.None);
        window.MouseUp(new Point(10, 10), MouseButton.Left, RawInputModifiers.None);
        Assert.NotNull(samplePress);
        layout.RaiseEvent(new PointerPressedEventArgs(
            layout,
            samplePress!.Pointer,
            layout,
            new Point(110, 11),
            samplePress.Timestamp + 1,
            samplePress.Properties,
            samplePress.KeyModifiers,
            clickCount: 2)
        {
            RoutedEvent = InputElement.PointerPressedEvent
        });
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(0, vm.MixerSaturation);

        await vm.ResetEditsCommand.ExecuteAsync(null);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(0, vm.MixerHue);
        Assert.Equal(0, vm.MixerSaturation);
        Assert.Equal(0, vm.MixerLuminance);
        Assert.False(touched.IsVisible);
        vm.SelectMixerBandCommand.Execute(ColorMixerBand.Blue);
        Assert.Equal(0, vm.MixerSaturation);

        vm.ActiveMixerBand = ColorMixerBand.Purple;
        vm.SelectedImage = new ImageFile(Path.Combine(_root.Path, "next-photo.jpg"));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(ColorMixerBand.Red, vm.ActiveMixerBand);

        window.Close();
        panel.DataContext = null;
    }

    [AvaloniaFact]
    public async Task MixerGroup_GeneratesMockupReviewScreenshots()
    {
        Assert.SkipWhen(
            Environment.GetEnvironmentVariable("HAPPY_PHOTON_MIXER_LOOKGATE") != "1",
            "Set HAPPY_PHOTON_MIXER_LOOKGATE=1 and " +
            "HAPPY_PHOTON_MIXER_LOOKGATE_DIR to generate mixer screenshots.");
        var outputDirectory = Environment.GetEnvironmentVariable(
            "HAPPY_PHOTON_MIXER_LOOKGATE_DIR");
        Assert.False(string.IsNullOrWhiteSpace(outputDirectory));
        outputDirectory = Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(outputDirectory);
        using var catalog = new CatalogService(Path.Combine(_root.Path, "screenshot-catalog"));
        await catalog.InitializeAsync();
        await using var vm = new MainWindowViewModel(
            catalog,
            new NullBaseLoader(),
            loadMetadataAsync: _ => Task.CompletedTask);
        vm.IsDevelopMode = true;
        vm.SelectedImage = new ImageFile(Path.Combine(_root.Path, "screenshot-photo.jpg"));
        vm.ActiveMixerBand = ColorMixerBand.Orange;
        vm.MixerHue = -5;
        vm.MixerSaturation = 22;
        vm.MixerLuminance = 10;
        var application = Application.Current!;
        try
        {
            foreach (var (theme, name) in new[]
                     {
                         (ThemeVariant.Dark, "dark"),
                         (HappyPhotonThemes.MidGray, "middle-gray")
                     })
            {
                application.RequestedThemeVariant = theme;
                Dispatcher.UIThread.RunJobs();
                var group = new MixerEditGroup
                {
                    DataContext = vm,
                    Margin = new Thickness(15)
                };
                var window = new Window
                {
                    Width = 250,
                    Height = 190,
                    Content = group,
                    Background = ThemeResourceTests.Brush("SurfaceLow", theme)
                };
                window.Show();
                Dispatcher.UIThread.RunJobs();
                using var frame = window.CaptureRenderedFrame() ??
                    throw new InvalidOperationException("Mixer screenshot was empty.");
                frame.Save(Path.Combine(
                    outputDirectory,
                    $"color-mixer-{name}.png"));
                window.Close();
                group.DataContext = null;
            }
        }
        finally
        {
            application.RequestedThemeVariant = ThemeVariant.Dark;
        }
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        _root.Dispose();
    }
}
