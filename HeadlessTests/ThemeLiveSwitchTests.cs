using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class ThemeLiveSwitchTests
{
    [AvaloniaFact]
    public async Task SameWindow_RepaintsCodeBuiltAndRealizedContentWithoutRestart()
    {
        var root = NewRoot();
        using var catalog = new CatalogService(Path.Combine(root, "catalog"));
        var vm = new MainWindowViewModel(
            catalog,
            baseLoader: null,
            loadMetadataAsync: _ => Task.CompletedTask);
        var image = new ImageFile(Path.Combine(root, "burst.jpg"))
        {
            IsSelected = true,
            IsActive = true,
            BurstGroupOrdinal = 1,
            BurstIndex = 1,
            BurstSize = 2
        };
        vm.Library.SetImages([image]);
        vm.SelectedImage = image;
        vm.ShowWorkspaceReady(MainWindowViewModel.CurrentFirstRunExperienceVersion);

        var window = new MainWindow { DataContext = vm };
        var originalWindow = window;
        var persistCount = 0;
        vm.PersistAppSettingsAsync = () =>
        {
            persistCount++;
            return Task.CompletedTask;
        };

        try
        {
            Application.Current!.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark;
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var titleBar = window.GetLogicalDescendants()
                .OfType<HappyPhotonTitleBar>()
                .Single();
            var appearance = titleBar.FindControl<Button>("AppearanceButton")!;
            Assert.False(appearance.IsEffectivelyEnabled);
            Assert.True(appearance.Focusable);

            vm.RestoreAppTheme(AppTheme.Dark);
            Dispatcher.UIThread.RunJobs();
            Assert.True(appearance.IsEffectivelyEnabled);
            Assert.Equal(0, persistCount);

            var library = window.FindControl<LibraryGridView>("LibraryGridView")!;
            var presetHeader = window.GetLogicalDescendants()
                .OfType<TextBlock>()
                .Single(text => text.Text == "My Presets");
            var burstStripe = window.GetVisualDescendants()
                .OfType<Border>()
                .Single(border => border.Classes.Contains("burst-stripe"));
            var thumbnail = window.GetVisualDescendants()
                .OfType<Border>()
                .Single(border => border.Classes.Contains("thumbnail"));
            var undo = window.GetLogicalDescendants()
                .OfType<Button>()
                .Single(button => button.Name == "UndoEditButton");
            var reset = window.GetLogicalDescendants()
                .OfType<Button>()
                .Single(button => button.Name == "ResetAdjustmentsButton");

            Assert.Equal(
                ThemeResourceTests.Brush("ViewerSurround", Avalonia.Styling.ThemeVariant.Dark).Color,
                ColorOf(library.Background));
            Assert.Equal(Color.Parse("#e4e1e9"), ColorOf(presetHeader.Foreground));
            Assert.Equal(Color.Parse("#00f0ff"), ColorOf(burstStripe.Background));
            Assert.Equal(Color.Parse("#4b4a52"), ColorOf(thumbnail.Background));
            Assert.Equal(Color.Parse("#00f0ff"), ColorOf(thumbnail.BorderBrush));
            Assert.Equal(0.32, undo.Opacity);
            Assert.Equal(0.32, reset.Opacity);
            Assert.Equal(Color.Parse("#849495"), ColorOf(reset.Foreground));

            vm.SetAppThemeCommand.Execute(AppTheme.MidGrey);
            Dispatcher.UIThread.RunJobs();

            Assert.Same(originalWindow, window);
            Assert.Equal(HappyPhotonThemes.MidGrey, Application.Current.RequestedThemeVariant);
            Assert.Equal(AppTheme.MidGrey, vm.AppTheme);
            Assert.True(vm.IsMidGreyTheme);
            Assert.False(vm.IsDarkTheme);
            Assert.Equal(1, persistCount);
            Assert.Equal(Color.Parse("#777777"), ColorOf(library.Background));
            Assert.Equal(Color.Parse("#ffffff"), ColorOf(presetHeader.Foreground));
            Assert.Equal(Color.Parse("#00dbe9"), ColorOf(burstStripe.Background));
            Assert.Equal(Color.Parse("#616161"), ColorOf(thumbnail.Background));
            Assert.Equal(Color.Parse("#00f0ff"), ColorOf(thumbnail.BorderBrush));
            Assert.Equal(0.62, undo.Opacity);
            Assert.Equal(0.62, reset.Opacity);
            Assert.Equal(Color.Parse("#c4cccc"), ColorOf(reset.Foreground));

            var flyout = Assert.IsType<MenuFlyout>(appearance.Flyout);
            flyout.ShowAt(appearance);
            Dispatcher.UIThread.RunJobs();
            var choices = flyout.Items.OfType<MenuItem>().ToArray();
            Assert.Equal(2, choices.Length);
            Assert.False(choices[0].IsChecked);
            Assert.True(choices[1].IsChecked);
            flyout.Hide();

            var confirmation = new ConfirmationDialog(
                "Confirm",
                "Continue?",
                ConfirmationDialogButtons.Ok,
                destructive: false);
            var input = new TextInputDialog("Name", "Preset name", "Value");
            Assert.Equal(Color.Parse("#3d3d3d"), ColorOf(confirmation.Background));
            Assert.Equal(Color.Parse("#3d3d3d"), ColorOf(input.Background));
            Assert.All(
                confirmation.GetLogicalDescendants().OfType<TextBlock>(),
                text => Assert.Equal(Color.Parse("#ffffff"), ColorOf(text.Foreground)));
            Assert.Equal(
                Color.Parse("#ffffff"),
                ColorOf(input.GetLogicalDescendants().OfType<TextBlock>().Single().Foreground));
            confirmation.Close();
            input.Close();
        }
        finally
        {
            Application.Current!.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark;
            window.DataContext = null;
            window.Close();
            await vm.DisposeAsync();
            Directory.Delete(root, recursive: true);
        }
    }

    [AvaloniaFact]
    public void DisabledControlFamilies_RemainLegibleUnderBothVariants()
    {
        var button = new Button { IsEnabled = false };
        var controls = new Control[]
        {
            button,
            new ToggleButton { IsEnabled = false },
            new RadioButton { IsEnabled = false },
            new TextBox { IsEnabled = false },
            new ComboBox { IsEnabled = false },
            new Slider { IsEnabled = false }
        };
        var panel = new StackPanel();
        foreach (var control in controls)
        {
            panel.Children.Add(control);
        }

        var window = new Window { Content = panel };
        try
        {
            Application.Current!.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark;
            window.Show();
            Dispatcher.UIThread.RunJobs();
            AssertDisabledControls(controls, Avalonia.Styling.ThemeVariant.Dark);

            Application.Current.RequestedThemeVariant = HappyPhotonThemes.MidGrey;
            Dispatcher.UIThread.RunJobs();
            AssertDisabledControls(controls, HappyPhotonThemes.MidGrey);
        }
        finally
        {
            Application.Current!.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark;
            window.Close();
        }
    }

    private static Color ColorOf(IBrush? brush) =>
        Assert.IsAssignableFrom<ISolidColorBrush>(brush).Color;

    private static void AssertDisabledControls(
        IEnumerable<Control> controls,
        Avalonia.Styling.ThemeVariant variant)
    {
        var parent = ThemeResourceTests.Brush("SurfaceLow", variant).Color;
        foreach (var control in controls)
        {
            Assert.False(control.IsEffectivelyEnabled);
            if (control is not TemplatedControl templated ||
                templated.Foreground is not ISolidColorBrush foreground)
            {
                continue;
            }

            var background = templated.Background is ISolidColorBrush brush
                ? Composite(brush.Color, parent)
                : parent;
            var foregroundColor = Composite(foreground.Color, background);
            background = Composite(background, parent, control.Opacity);
            foregroundColor = Composite(foregroundColor, parent, control.Opacity);
            var contrast = ThemeResourceTests.Contrast(foregroundColor, background);
            Assert.True(
                contrast >= 1.5,
                $"{control.GetType().Name} disabled content resolved to " +
                $"{foregroundColor} on {background} ({contrast:F2}:1).");
        }
    }

    private static Color Composite(Color color, Color background) =>
        Composite(color, background, color.A / 255d);

    private static Color Composite(Color color, Color background, double opacity) =>
        Color.FromRgb(
            Blend(color.R, background.R, opacity),
            Blend(color.G, background.G, opacity),
            Blend(color.B, background.B, opacity));

    private static byte Blend(byte foreground, byte background, double opacity) =>
        (byte)Math.Round(foreground * opacity + background * (1 - opacity));

    private static string NewRoot() =>
        Directory.CreateDirectory(Path.Combine(
            Path.GetTempPath(),
            $"happy-photon-theme-{Guid.NewGuid():N}")).FullName;
}
