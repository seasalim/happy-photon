using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using HappyPhoton.Views;
using Xunit;
using Rectangle = Avalonia.Controls.Shapes.Rectangle;

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
            var brandMark = titleBar.FindControl<Border>("BrandMark")!;
            var brandWordmark = titleBar.FindControl<TextBlock>("BrandWordmark")!;
            var photonWordmark = brandWordmark.Inlines!
                .OfType<Run>()
                .Single(run => run.Text == "Photon");
            var libraryUnderline = titleBar.FindControl<Rectangle>(
                "LibraryTabUnderline")!;
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
            var export = window.GetLogicalDescendants()
                .OfType<Button>()
                .Single(button => button.Name == "LibraryExportButton");
            var exportPresenter = export.GetVisualDescendants()
                .OfType<ContentPresenter>()
                .Single(presenter => presenter.Name == "PART_ContentPresenter");

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
            AssertBrandSurfaces(
                brandMark,
                photonWordmark,
                libraryUnderline,
                exportPresenter,
                ThemeVariant.Dark,
                pointerOver: false);

            var darkMarkSource = Assert.IsType<ImageBrush>(brandMark.Background).Source;
            ((IPseudoClasses)export.Classes).Set(":pointerover", true);
            Dispatcher.UIThread.RunJobs();
            AssertBrandSurfaces(
                brandMark,
                photonWordmark,
                libraryUnderline,
                exportPresenter,
                ThemeVariant.Dark,
                pointerOver: true);

            vm.SetAppThemeCommand.Execute(AppTheme.MidGray);
            Dispatcher.UIThread.RunJobs();
            // Border.thumbnail is the only asserted control that transitions
            // (Background and BorderBrush, 130ms each). Pump the render clock
            // until both reach their end colors so the assertions below sample
            // settled brushes instead of a guessed instant mid-fade.
            await SettleAsync(() =>
                ColorOf(thumbnail.Background) == Color.Parse("#616161") &&
                ColorOf(thumbnail.BorderBrush) == Color.Parse("#bbbbbb"));

            Assert.Same(originalWindow, window);
            Assert.Equal(HappyPhotonThemes.MidGray, Application.Current.RequestedThemeVariant);
            Assert.Equal(AppTheme.MidGray, vm.AppTheme);
            Assert.True(vm.IsMidGrayTheme);
            Assert.False(vm.IsDarkTheme);
            Assert.Equal(1, persistCount);
            Assert.Equal(Color.Parse("#777777"), ColorOf(library.Background));
            Assert.Equal(Color.Parse("#ffffff"), ColorOf(presetHeader.Foreground));
            Assert.Equal(Color.Parse("#00dbe9"), ColorOf(burstStripe.Background));
            Assert.Equal(Color.Parse("#616161"), ColorOf(thumbnail.Background));
            Assert.Equal(Color.Parse("#bbbbbb"), ColorOf(thumbnail.BorderBrush));
            Assert.Equal(0.62, undo.Opacity);
            Assert.Equal(0.62, reset.Opacity);
            Assert.Equal(Color.Parse("#c8c8c8"), ColorOf(reset.Foreground));
            AssertBrandSurfaces(
                brandMark,
                photonWordmark,
                libraryUnderline,
                exportPresenter,
                HappyPhotonThemes.MidGray,
                pointerOver: true);
            Assert.NotSame(
                darkMarkSource,
                Assert.IsType<ImageBrush>(brandMark.Background).Source);

            ((IPseudoClasses)export.Classes).Set(":pointerover", false);
            Dispatcher.UIThread.RunJobs();
            AssertBrandSurfaces(
                brandMark,
                photonWordmark,
                libraryUnderline,
                exportPresenter,
                HappyPhotonThemes.MidGray,
                pointerOver: false);

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

            Application.Current.RequestedThemeVariant = HappyPhotonThemes.MidGray;
            Dispatcher.UIThread.RunJobs();
            AssertDisabledControls(controls, HappyPhotonThemes.MidGray);
        }
        finally
        {
            Application.Current!.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark;
            window.Close();
        }
    }

    private static async Task SettleAsync(Func<bool> settled)
    {
        var deadline = DateTime.UtcNow + TestWaits.Condition;
        while (DateTime.UtcNow < deadline)
        {
            Avalonia.Headless.AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
            if (settled()) return;
            await Task.Delay(10);
        }

        Assert.True(settled(), "A theme transition never reached its end color.");
    }

    private static Color ColorOf(IBrush? brush) =>
        Assert.IsAssignableFrom<ISolidColorBrush>(brush).Color;

    private static void AssertBrandSurfaces(
        Border mark,
        Run photonWordmark,
        Rectangle underline,
        ContentPresenter accentPresenter,
        ThemeVariant variant,
        bool pointerOver)
    {
        var expectedMark = ThemeResourceTests.Resource<ImageBrush>("BrandMark", variant);
        Assert.Same(
            expectedMark.Source,
            Assert.IsType<ImageBrush>(mark.Background).Source);
        Assert.Equal(
            ThemeResourceTests.Brush("BrandAccent", variant).Color,
            ColorOf(photonWordmark.Foreground));
        Assert.Equal(
            ThemeResourceTests.Brush("BrandAccent", variant).Color,
            ColorOf(underline.Fill));
        Assert.Equal(
            ThemeResourceTests.Brush(
                pointerOver ? "BrandAccentHover" : "BrandAccent",
                variant).Color,
            ColorOf(accentPresenter.Background));
        Assert.Equal(
            ThemeResourceTests.Brush("OnBrandAccent", variant).Color,
            ColorOf(accentPresenter.Foreground));
    }

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
