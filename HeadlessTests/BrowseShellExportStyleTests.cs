using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using HappyPhoton.Views;
using Xunit;
using ShapePath = Avalonia.Controls.Shapes.Path;

namespace HappyPhoton.Tests;

public sealed class BrowseShellExportStyleTests
{
    [AvaloniaTheory]
    [MemberData(nameof(ThemeResourceTests.Variants), MemberType = typeof(ThemeResourceTests))]
    public async Task ProductionIconsUseSharedStatesAndMetrics(ThemeVariant theme)
    {
        using var fixture = new CatalogVmFixture("wp8-states");
        using var catalog = fixture.CreateCatalog();
        await using var vm = fixture.CreateViewModel(catalog, new NullBaseLoader(), _ => Task.CompletedTask);
        vm.ShowWorkspaceReady(HappyPhoton.ViewModels.MainWindowViewModel.CurrentFirstRunExperienceVersion);
        var image = new ImageFile(fixture.Path("unloaded.jpg")) { MetadataLoaded = true };
        vm.Browse.SetImages([image]);
        vm.SelectedImage = image;
        var browse = new BrowseGridView { DataContext = vm, Images = vm.Browse.VisibleImages };
        var folders = new FolderTreePanel();
        var panel = new DockPanel();
        DockPanel.SetDock(folders, Dock.Left);
        folders.Width = 200;
        panel.Children.Add(folders);
        panel.Children.Add(browse);
        var window = new Window { Width = 1200, Height = 700, Content = panel };
        using var scope = new TestUiScope(window, theme);
        var buttons = panel.GetVisualDescendants().OfType<Button>()
            .Where(button => button.Classes.Contains("icon-button")).ToArray();
        Assert.Equal(10, buttons.Length);

        foreach (var button in buttons)
        {
            button.Command = null;
            button.IsEnabled = true;
            var compact = button.Classes.Contains("compact");
            var size = compact ? 20 : 24;
            Assert.Equal(new Size(size, size), button.Bounds.Size);
            var icon = Assert.IsType<Viewbox>(button.Content);
            Assert.Equal(compact ? 12 : 14, icon.Width);
            var path = Assert.Single(icon.GetVisualDescendants().OfType<ShapePath>());
            Assert.NotNull(path.Data);
            var presenter = button.GetVisualDescendants().OfType<ContentPresenter>()
                .Single(control => control.Name == "PART_ContentPresenter");
            var toggle = button as ToggleButton;

            if (toggle is not null)
            {
                toggle.IsChecked = false;
            }

            window.MouseMove(button.TranslatePoint(new Point(size / 2, size / 2), window)!.Value);
            Dispatcher.UIThread.RunJobs();
            Assert.Contains(":pointerover", button.Classes);
            AssertBrush("SurfaceHigh", presenter.Background, theme);
            AssertBrush("TextPrimary", path.Stroke, theme);

            if (toggle is not null)
            {
                toggle.IsChecked = true;
                AssertBrush("ControlActive", presenter.Background, theme);
                AssertBrush("OnControlActive", path.Stroke, theme);
                window.MouseMove(new Point(1, 1));
                Dispatcher.UIThread.RunJobs();
                AssertBrush("ControlActive", presenter.Background, theme);
                AssertBrush("OnControlActive", path.Stroke, theme);
            }

            button.IsEnabled = false;
            Assert.Equal(ThemeResourceTests.Resource<double>("DisabledOpacity", theme), button.Opacity);
            AssertBrush(toggle is null ? "SurfaceHigh" : "ControlActive", presenter.Background, theme);
            button.IsEnabled = true;
        }

        var rating = browse.FindControl<BrowseRatingFilter>("RatingFilter")!;
        var assessment = browse.FindControl<ImageAssessmentControl>("ImageAssessment")!;
        Assert.Equal(rating.FindControl<Button>("RatingFilter1Button")!.Bounds.Size,
            assessment.FindControl<Button>("Rating1Button")!.Bounds.Size);
        var swatches = panel.GetVisualDescendants().OfType<Button>()
            .Where(button => button.Classes.Contains("swatch") || button.Name == "ColorLabelButton");
        Assert.All(swatches, button => Assert.Equal(new Size(20, 20), button.Bounds.Size));

        var preview = new ExportPreviewPane { DataContext = vm };
        window.Content = preview;
        window.UpdateLayout();
        var proof = preview.FindControl<ToggleButton>("ExportProofToggle")!;
        var proofPresenter = proof.GetVisualDescendants().OfType<ContentPresenter>()
            .Single(control => control.Name == "PART_ContentPresenter");
        Assert.Equal(24, proof.Bounds.Height);
        proof.IsChecked = false;
        window.MouseMove(proof.TranslatePoint(new Point(12, 12), window)!.Value);
        Dispatcher.UIThread.RunJobs();
        AssertBrush("SurfaceHigh", proofPresenter.Background, theme);
        proof.IsChecked = true;
        AssertBrush("ControlActive", proofPresenter.Background, theme);
        AssertBrush("OnControlActive", proofPresenter.Foreground, theme);
        window.MouseMove(new Point(1, 1));
        proof.IsEnabled = false;
        Assert.Equal(ThemeResourceTests.Resource<double>("DisabledOpacity", theme), proof.Opacity);
        AssertBrush("ControlActive", proofPresenter.Background, theme);
    }

    [AvaloniaTheory]
    [MemberData(nameof(ThemeResourceTests.Variants), MemberType = typeof(ThemeResourceTests))]
    public async Task ProductionMenusKeepSharedTypographyAndKeyboardFocus(ThemeVariant theme)
    {
        await DevelopToolsBaselineTests.WithScene("normal", 1440, 900, async (vm, scope) =>
        {
            using var themeScope = new TestUiScope(theme: theme);
            vm.RestoreAppTheme(theme == HappyPhotonThemes.MidGray ? AppTheme.MidGray : AppTheme.Dark);
            var window = scope.Window!;
            vm.SwitchToBrowseCommand.Execute(null);
            // test-teardown-policy: allow - WithScene owns this MainWindow scope.
            scope.Show();
            var browse = window.FindControl<BrowseGridView>("BrowseGridView")!;
            var tile = browse.GetVisualDescendants().OfType<Border>()
                .First(border => border.Name == "ThumbnailTile");
            CheckContext(tile, theme);
            CheckFlyout(browse.FindControl<Button>("BrowseActionsButton")!, theme);
            var review = window.FindControl<BrowseReviewPane>("BrowseReviewPane")!;
            CheckContext(review.FindControl<StackPanel>("ReviewMetadataPanel")!, theme);
            var title = window.GetVisualDescendants().OfType<HappyPhotonTitleBar>().Single();
            CheckFlyout(title.FindControl<Button>("AppearanceButton")!, theme);
            var folders = new FolderTreePanel { RootFolders = [new FolderNode("test-folder")] };
            using (var folderScope = new TestUiScope(new Window { Width = 300, Height = 400, Content = folders }, theme))
            {
                CheckFlyout(folders.FindControl<Button>("FolderActionsButton")!, theme);
                CheckContext(folders.GetVisualDescendants().OfType<Border>()
                    .Single(border => border.Classes.Contains("folder-row")), theme);
            }

            vm.SelectedImage!.IsSelected = true;
            await vm.SwitchToExportCommand.ExecuteAsync(null);
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            CheckFlyout(window.FindControl<ExportCapturePane>("ExportCapturePane")!
                .FindControl<Button>("ChangeExportPhotosButton")!, theme);
        });
    }

    [AvaloniaTheory]
    [MemberData(nameof(ThemeResourceTests.Variants), MemberType = typeof(ThemeResourceTests))]
    public async Task HistoryMenuKeepsSharedTypographyAndKeyboardFocus(ThemeVariant theme)
    {
        using var fixture = new CatalogVmFixture("wp8-history-menu");
        using var catalog = await fixture.CreateCatalogAsync();
        var path = fixture.Path("history.jpg");
        var id = await catalog.GetOrCreateImageAsync(path);
        var edited = new EditSettings { Exposure = .5 };
        await catalog.SaveEditSettingsWithHistoryAsync(id, edited,
            new CatalogEditHistoryMutation(-1,
                [new(0, "Original", new()), new(1, "Exposure +0.5", edited)], 1));
        await using var vm = fixture.CreateViewModel(catalog, new NullBaseLoader(), _ => Task.CompletedTask);
        vm.ShowWorkspaceReady(MainWindowViewModel.CurrentFirstRunExperienceVersion);
        vm.SelectedImage = new ImageFile(path) { CatalogId = id, EditSettings = edited };
        vm.IsDevelopMode = true;
        await TestWaits.UntilAsync(() => vm.IsHistoryLoaded);
        var history = new EditHistoryPanel { DataContext = vm };
        using var scope = new TestUiScope(new Window { Width = 300, Height = 300, Content = history }, theme);
        var original = history.GetVisualDescendants().OfType<Button>()
            .Single(button => button.Classes.Contains("history-row") &&
                ((EditHistoryEntry)button.DataContext!).Sequence == 0);
        CheckContext(original, theme);
    }

    private static void CheckContext(Control target, ThemeVariant theme)
    {
        var menu = target.ContextMenu!;

        try
        {
            target.Focusable = true;
            Assert.True(target.Focus(NavigationMethod.Tab));
            SharedControlFocusTests.Press(TopLevel.GetTopLevel(target)!, Key.Apps);
            Assert.True(menu.IsOpen);
            Dispatcher.UIThread.RunJobs();
            CheckItems(menu.Items.OfType<MenuItem>().ToArray(), theme);
        }
        finally
        {
            menu.Close();
        }
    }

    private static void CheckFlyout(Button target, ThemeVariant theme)
    {
        var menu = Assert.IsType<MenuFlyout>(target.Flyout);

        try
        {
            Assert.True(target.Focus(NavigationMethod.Tab));
            SharedControlFocusTests.Press(TopLevel.GetTopLevel(target)!, Key.Space);
            Assert.True(menu.IsOpen);
            Dispatcher.UIThread.RunJobs();
            CheckItems(menu.Items.OfType<MenuItem>().ToArray(), theme);
        }
        finally
        {
            menu.Hide();
        }
    }

    private static void CheckItems(MenuItem[] items, ThemeVariant theme)
    {
        Assert.NotEmpty(items);

        foreach (var item in items)
        {
            Assert.Equal(24, item.Bounds.Height);
            Assert.Equal(11, item.FontSize);
            var gesture = item.GetVisualDescendants().OfType<TextBlock>()
                .Single(text => text.Name == "PART_InputGestureText");
            Assert.Equal(ThemeResourceTests.Resource<FontFamily>("FontLabel", theme), gesture.FontFamily);
            AssertBrush("TextMuted", gesture.Foreground, theme);
        }

        var enabled = items.FirstOrDefault(item => item.IsEffectivelyEnabled);
        if (enabled is null) return;

        var popup = TopLevel.GetTopLevel(enabled)!;
        SharedControlFocusTests.Press(popup, Key.Down);
        var focused = Assert.IsType<MenuItem>(popup.FocusManager!.GetFocusedElement());
        Assert.Contains(focused, items);
        Assert.True(focused.IsEffectivelyEnabled);
        SharedControlFocusTests.AssertOutline(focused, theme);
    }

    private static void AssertBrush(string resource, IBrush? actual, ThemeVariant theme) =>
        Assert.Equal(ThemeResourceTests.Brush(resource, theme).Color,
            Assert.IsAssignableFrom<ISolidColorBrush>(actual).Color);
}
