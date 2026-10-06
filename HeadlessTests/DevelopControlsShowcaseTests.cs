using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HappyPhoton.Models;
using HappyPhoton.Views;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class DevelopControlsShowcaseTests
{
    [AvaloniaTheory]
    [InlineData("group-blocks-develop", false)]
    [InlineData("group-blocks-develop", true)]
    [InlineData("group-blocks-hover-collapsed", false)]
    [InlineData("group-blocks-hover-collapsed", true)]
    [InlineData("group-blocks-export", false)]
    [InlineData("group-blocks-export", true)]
    [InlineData("bottom-band-develop", false)]
    [InlineData("bottom-band-develop", true)]
    [InlineData("bottom-band-browse", false)]
    [InlineData("bottom-band-browse", true)]
    public async Task GroupBlocksAndBottomBands(string scene, bool gray)
    {
        await DevelopToolsBaselineTests.WithScene("normal", 1200, 700, (vm, scope) =>
        {
            vm.AppTheme = gray ? AppTheme.MidGray : AppTheme.Dark;

            if (scene == "bottom-band-browse") vm.IsDevelopMode = false;
            if (scene == "group-blocks-export")
            {
                vm.Browse.SelectAllVisible();
                vm.RefreshSelectedCount();
                vm.SwitchToExportCommand.Execute(null);
                Assert.Single(vm.ExportCaptures);
            }

            ShowcaseTestHelper.Capture(scene + (gray ? "-gray" : "-dark"), scope,
                new PixelSize(1200, 700), gray ? HappyPhotonThemes.MidGray : ThemeVariant.Dark, window =>
                {
                    var editPanel = window.GetVisualDescendants().OfType<DevelopEditPanel>().Single();
                    editPanel.FindControl<ScrollViewer>("DevelopControlsScrollViewer")!.Offset = default;

                    if (scene == "group-blocks-hover-collapsed")
                    {
                        vm.ProfileGroup.IsExpanded = false;
                        vm.WhiteBalanceGroup.IsExpanded = false;
                        window.UpdateLayout();
                        var header = window.GetVisualDescendants().OfType<DevelopGroup>()
                            .First(group => group.Header is "Adjustments");
                        var toggle = DevelopCollapseBaselineTests.Header(header);
                        window.MouseMove(toggle.TranslatePoint(new Point(20, 16), window)!.Value);
                    }

                    if (scene == "group-blocks-export")
                    {
                        var pane = window.GetVisualDescendants().OfType<ExportSettingsPane>().Single();
                        pane.FindControl<Expander>("ExportMoreOptions")!.IsExpanded = false;
                        pane.FindControl<Expander>("ExportWatermarkExpander")!.IsExpanded = false;
                    }

                    window.UpdateLayout();
                    if (scene != "bottom-band-browse") ShowcaseTestHelper.SettleExpanderChevrons(window);
                });

            return Task.CompletedTask;
        });
    }

    [AvaloniaTheory]
    [InlineData("crop-ratio-closed", false)]
    [InlineData("crop-ratio-closed", true)]
    [InlineData("crop-ratio-3x2", false)]
    [InlineData("crop-ratio-3x2", true)]
    public async Task CropRatioScenes(string scene, bool gray)
    {
        var size = new PixelSize(1200, 700);
        using var directory = new TemporaryDirectory();
        var sourcePath = Path.Combine(directory.Path, "crop-scene.jpg");

        using (var source = new MagickImage(GoldenTestPaths.Asset("srgb-reference.jpg")))
        {
            source.Crop(new MagickGeometry(68, 0, 1064, 798));
            source.ResetPage();
            source.Write(sourcePath);
        }

        await DevelopToolsBaselineTests.WithScene("crop", size.Width, size.Height, async (vm, scope) =>
        {
            vm.AppTheme = gray ? AppTheme.MidGray : AppTheme.Dark;
            vm.CurrentCrop = new CropRegion { Left = .1, Right = .71, Top = .2, Bottom = .9 };

            if (scene == "crop-ratio-3x2") vm.ChooseCropRatio("3:2");

            ShowcaseTestHelper.Capture(scene + (gray ? "-gray" : "-dark"), scope, size,
                gray ? HappyPhotonThemes.MidGray : ThemeVariant.Dark, window =>
                {
                    window.Focus();
                    var scroll = window.GetVisualDescendants().OfType<ScrollViewer>()
                        .Single(control => control.Name == "DevelopControlsScrollViewer");
                    scroll.Offset = default;
                    Dispatcher.UIThread.RunJobs();
                    window.UpdateLayout();
                    var section = window.GetVisualDescendants().OfType<CropEditSection>().Single();
                    var picker = section.FindControl<ComboBox>("CropRatioPicker")!;
                    var aspectLock = section.FindControl<ToggleButton>("CropAspectLockButton")!;
                    Assert.False(picker.IsDropDownOpen);
                    Assert.Equal(scene == "crop-ratio-3x2" ? "3:2" : "Custom", picker.SelectedItem);
                    Assert.Equal(scene == "crop-ratio-3x2", vm.SwapCropRatioCommand.CanExecute(null));
                    Assert.Equal(vm.IsCropAspectLocked, aspectLock.IsChecked);
                    Assert.True(picker.IsEffectivelyVisible);
                    Assert.True(aspectLock.IsEffectivelyVisible);
                    var origin = picker.TranslatePoint(default, scroll)!.Value;
                    Assert.InRange(origin.Y, 0, scroll.Bounds.Height - picker.Bounds.Height);
                });
            await Task.CompletedTask;
        }, sourcePath);
    }

    // FIXES-DEVELOP-WP10 visual review: the top of both side panes against the scope-band mockup.
    [AvaloniaTheory]
    [InlineData("develop", false)]
    [InlineData("develop", true)]
    [InlineData("browse", false)]
    [InlineData("browse", true)]
    public async Task ScopeBandScenes(string mode, bool gray)
    {
        var size = new PixelSize(1200, 700);
        await DevelopToolsBaselineTests.WithScene("normal", size.Width, size.Height, async (vm, scope) =>
        {
            vm.AppTheme = gray ? AppTheme.MidGray : AppTheme.Dark;
            ShootingInfoNavigatorTests.SetExif(vm.SelectedImage!);
            var histogram = vm.EffectiveHistogram;
            vm.IsDevelopMode = mode == "develop";

            ShowcaseTestHelper.Capture($"scope-band-{mode}-{(gray ? "gray" : "dark")}", scope, size,
                gray ? HappyPhotonThemes.MidGray : ThemeVariant.Dark, window =>
                {
                    var scroll = window.GetVisualDescendants().OfType<ScrollViewer>()
                        .SingleOrDefault(control => control.Name == "DevelopControlsScrollViewer");

                    if (scroll is not null) scroll.Offset = default;

                    // The thumbnail histogram needs a decoded thumbnail; show the preview's instead.
                    if (mode == "browse") vm.Histogram = histogram;

                    Dispatcher.UIThread.RunJobs();
                    window.UpdateLayout();
                    var band = mode == "develop" ? "DevelopScopeBox" : "BrowseHistogramBox";
                    Assert.True(window.GetVisualDescendants().OfType<Border>().Single(border => border.Name == band)
                        .IsEffectivelyVisible);
                });
            await Task.CompletedTask;
        });
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExpandedLocalsRemainReadable(bool gray)
    {
        await DevelopToolsBaselineTests.WithScene("locals", 1200, 700, async (vm, mainScope) =>
        {
            vm.IsLocalGeometryExpanded = true;
            vm.IsLocalLuminanceExpanded = true;
            vm.IsLocalHueExpanded = true;
            var theme = gray ? HappyPhotonThemes.MidGray : ThemeVariant.Dark;
            var section = new LocalsEditSection { DataContext = vm, Margin = new Thickness(15) };
            var window = new Window { Content = section, Background = ThemeResourceTests.Brush("SurfaceMid", theme) };
            ShowcaseTestHelper.Capture($"08-develop-locals-expanded-wp7-{(gray ? "gray" : "dark")}",
                window, new PixelSize(300, 1500), theme, _ =>
                {
                    foreach (var name in new[] { "LocalGeometryDisclosure", "LocalLuminanceDisclosure", "LocalHueDisclosure" })
                    {
                        var button = section.FindControl<ToggleButton>(name)!;
                        Assert.True(button.IsChecked);
                        Assert.True(button.IsEffectivelyVisible);
                    }
                });
            section.DataContext = null;
            await Task.CompletedTask;
        });
    }

    [AvaloniaTheory]
    [InlineData("05-develop-dark", "normal", false)]
    [InlineData("06-develop-crop", "crop", false)]
    [InlineData("07-develop-spots", "spots", false)]
    [InlineData("08-develop-locals", "locals", false)]
    [InlineData("09-develop-panel-mid", "curve", false)]
    [InlineData("10-develop-panel-lower", "lower", false)]
    [InlineData("11-develop-panel-bottom", "collapsed", false)]
    [InlineData("12-develop-optics", "optics", false)]
    [InlineData("13-develop-color-mixer", "mixer", false)]
    [InlineData("23-develop-midgray", "normal", true)]
    [InlineData("25-develop-dark-100pct", "normal", false)]
    public async Task DevelopScenes(string scene, string mode, bool gray)
    {
        var size = new PixelSize(2200, 1260);
        await DevelopToolsBaselineTests.WithScene(mode, size.Width, size.Height, async (vm, scope) =>
        {
            var theme = gray ? HappyPhotonThemes.MidGray : ThemeVariant.Dark;
            vm.AppTheme = gray ? AppTheme.MidGray : AppTheme.Dark;

            if (mode == "spots") await vm.ToggleSpotsModeCommand.ExecuteAsync(null);

            ShowcaseTestHelper.Capture(scene + "-wp7", scope, size, theme, window =>
            {
                var panel = window.GetVisualDescendants().OfType<DevelopEditPanel>().Single();
                var groups = panel.GetVisualDescendants().OfType<DevelopGroup>().ToArray();
                Assert.All(panel.GetVisualDescendants().OfType<CompactSlider>()
                    .Where(slider => slider.IsEffectivelyVisible), slider =>
                    Assert.Equal(20, slider.FindControl<Border>("ValueCell")!.Bounds.Height));
                var navigator = window.FindControl<Border>("NavigatorPanel")!;
                var navigatorHeader = window.FindControl<Grid>("NavigatorHeader")!;
                var history = window.GetVisualDescendants().OfType<EditHistoryPanel>().Single();
                var historyHeader = history.FindControl<Grid>("HistoryHeader")!;
                var presets = window.GetVisualDescendants().OfType<PresetsPanel>().Single();
                var presetsHeader = presets.GetVisualDescendants().OfType<TextBlock>()
                    .Single(text => text.Classes.Contains("section-label"));

                foreach (var (header, host) in new (Control, Control)[]
                    { (navigatorHeader, navigator), (historyHeader, history), (presetsHeader, presets) })
                {
                    Assert.Equal(24, header.Bounds.Height);
                    Assert.Equal(10, header.TranslatePoint(default, host)!.Value.X);
                }

                Assert.All(panel.GetVisualDescendants().OfType<Button>()
                    .Where(button => button is not CheckBox && button.Name != "ExpanderHeader"), button =>
                    Assert.True(button.Classes.Any(name => name is "compact-button" or "quiet-button" or "icon-button" or "link-button"),
                        $"{button.Name} must use a shared button class."));

                if (mode is "curve" or "lower" or "collapsed" or "optics" or "mixer")
                {
                    foreach (var group in groups)
                    {
                        group.IsExpanded = mode switch
                        {
                            "curve" => Equals(group.Header, "Tone Curve"),
                            "lower" => group.Header is "Detail" or "Effects" or "Geometry",
                            "optics" => Equals(group.Header, "Optics"),
                            "mixer" => Equals(group.Header, "Color Mixer"),
                            _ => false
                        };
                    }
                }

                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                panel.FindControl<ScrollViewer>("DevelopControlsScrollViewer")!.Offset = default;
                ShowcaseTestHelper.SettleExpanderChevrons(panel);
            });
        });
    }

    [AvaloniaTheory]
    [InlineData("14-loupe", false)]
    [InlineData("27-compare", false)]
    [InlineData("27-compare", true)]
    public async Task ViewerScenes(string scene, bool gray)
    {
        var size = new PixelSize(2200, 1260);
        await DevelopToolsBaselineTests.WithScene("normal", size.Width, size.Height, async (vm, scope) =>
        {
            var theme = gray ? HappyPhotonThemes.MidGray : ThemeVariant.Dark;
            vm.AppTheme = gray ? AppTheme.MidGray : AppTheme.Dark;
            var first = vm.SelectedImage!;
            using var thumbnail = new Bitmap(GoldenTestPaths.Asset("srgb-reference.jpg"));
            first.Thumbnail = thumbnail;
            var second = new ImageFile(GoldenTestPaths.Asset("display-p3-reference.jpg")) { Thumbnail = thumbnail };
            var third = new ImageFile(GoldenTestPaths.Asset("adobe-rgb-reference.jpg")) { Thumbnail = thumbnail };
            vm.Browse.SetImages([first, second, third]);
            vm.SwitchToBrowseCommand.Execute(null);
            vm.ToggleImageSelection(first);
            vm.ToggleImageSelection(second);
            vm.ToggleImageSelection(third);

            if (scene == "14-loupe") vm.ToggleLoupeCommand.Execute(null);
            else vm.ToggleCompareCommand.Execute(null);

            ShowcaseTestHelper.Capture(scene + "-wp7" + (gray ? "-gray" : ""), scope, size, theme);
            first.Thumbnail = null;
            second.Thumbnail = null;
            third.Thumbnail = null;
            await Task.CompletedTask;
        });
    }

    [AvaloniaTheory]
    [InlineData(250, false)]
    [InlineData(200, false)]
    [InlineData(250, true)]
    [InlineData(200, true)]
    public async Task CropLockFitsAndToggles(int width, bool gray)
    {
        await DevelopToolsBaselineTests.WithScene("crop", 1200, 700, async (vm, mainScope) =>
        {
            var panel = new DevelopEditPanel { DataContext = vm };
            var window = new Window { Width = width, Height = 660, Content = panel };
            var theme = gray ? HappyPhotonThemes.MidGray : ThemeVariant.Dark;
            using var scope = new TestUiScope(window, theme);
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            var crop = panel.GetVisualDescendants().OfType<CropEditSection>().Single();
            var toggle = crop.FindControl<ToggleButton>("CropAspectLockButton")!;
            var row = toggle.GetVisualAncestors().OfType<Grid>().First();
            var picker = crop.FindControl<ComboBox>("CropRatioPicker")!;
            Assert.Same(row, picker.Parent);
            Assert.Equal(24, toggle.Bounds.Height);
            Assert.True(picker.Bounds.Right <= toggle.Bounds.Left);
            Assert.True(toggle.Bounds.Right <= row.Bounds.Width);
            var auto = crop.FindControl<Button>("AutoStraightenButton")!;
            var actions = auto.GetVisualAncestors().OfType<Grid>().First();
            var buttons = actions.Children.OfType<Button>().ToArray();
            Assert.Equal(2, buttons.Length);
            Assert.All(buttons, button => Assert.Equal(24, button.Bounds.Height));
            Assert.True(buttons[0].Bounds.Right <= buttons[1].Bounds.Left);
            Assert.True(buttons[1].Bounds.Right <= actions.Bounds.Width);

            foreach (var button in buttons)
            {
                var text = button.GetVisualDescendants().OfType<TextBlock>().Single();
                Assert.True(text.Bounds.Width >= text.TextLayout.WidthIncludingTrailingWhitespace);
            }

            foreach (var locked in new[] { false, true })
            {
                vm.IsCropAspectLocked = locked;
                Dispatcher.UIThread.RunJobs();
                Assert.Equal("Lock aspect ratio, " + (locked ? "locked" : "unlocked"), AutomationProperties.GetName(toggle));
                Assert.Equal("Lock aspect ratio", ToolTip.GetTip(toggle));
                Assert.Equal(locked, toggle.IsChecked);
                Assert.True(toggle.IsEffectivelyEnabled);
                toggle.BringIntoView();
                ShowcaseTestHelper.Settle(() =>
                {
                    var center = toggle.TranslatePoint(new Point(12, 12), window)!.Value;
                    var hit = window.InputHitTest(center) as Visual;

                    return ReferenceEquals(hit, toggle) || hit?.GetVisualAncestors().Contains(toggle) == true;
                }, "Crop lock hit target");
                using var frame = window.CaptureRenderedFrame();
                Assert.NotNull(frame);
                var directory = Path.Combine(GoldenTestPaths.RepositoryRoot, "artifacts", "shots");
                Directory.CreateDirectory(directory);
                Assert.Equal(new PixelSize(width, 660), frame.PixelSize);
                frame.Save(Path.Combine(directory,
                    $"06-develop-crop-wp7-{width}-{(gray ? "gray" : "dark")}-{(locked ? "locked" : "unlocked")}.png"), PngBitmapEncoderOptions.Default);
                var position = toggle.TranslatePoint(new Point(12, 12), window)!.Value;
                window.MouseMove(position);
                window.MouseDown(position, MouseButton.Left);
                window.MouseUp(position, MouseButton.Left);
                Assert.Equal(!locked, vm.IsCropAspectLocked);
            }

            ShowcaseTestHelper.Capture($"06-develop-crop-wp7-{width}-{(gray ? "gray" : "dark")}",
                scope, new PixelSize(width, 660), theme);
            panel.DataContext = null;
            await Task.CompletedTask;
        });
    }
}
