using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class BrowseFooterLayoutTests
{
    [AvaloniaFact]
    public async Task ControlsRemainLaidOutAtSupportedWindowWidths()
    {
        using var root = new TemporaryDirectory();
        using var catalog = new CatalogService(Path.Combine(root.Path, "catalog"));
        await catalog.InitializeAsync();
        await using var vm = new MainWindowViewModel(
            catalog,
            new NullBaseLoader(),
            _ => Task.CompletedTask);
        vm.ShowWorkspaceReady(MainWindowViewModel.CurrentFirstRunExperienceVersion);
        var images = new[]
        {
            new ImageFile(Path.Combine(root.Path, "first.jpg")),
            new ImageFile(Path.Combine(root.Path, "second.jpg"))
        };
        vm.Browse.SetImages(images);
        vm.SelectedImage = images[0];
        images[0].Flag = ImageFlag.Rejected;
        foreach (var image in images) vm.ToggleImageSelection(image);
        var window = new MainWindow
        {
            Width = 1400,
            Height = 700,
            DataContext = vm
        };
        window.Show();
        Drain();

        try
        {
            var browse = Descendant<BrowseGridView>(window, "BrowseGridView");
            var reject = Descendant<Button>(browse, "RejectImageButton");
            Assert.Equal(
                ThemeResourceTests.Brush("RejectSurface", ThemeVariant.Dark).Color,
                Assert.IsType<SolidColorBrush>(reject.Background).Color);
            Assert.Equal(
                ThemeResourceTests.Brush("RejectOutline", ThemeVariant.Dark).Color,
                Assert.IsType<SolidColorBrush>(reject.BorderBrush).Color);
            Assert.Equal(new Thickness(1), reject.BorderThickness);
            var message = Descendant<TextBlock>(browse, "OnlineOnlyMessage");
            message.Text = "A deliberately long cloud-only status message that must " +
                "truncate before the assessment controls instead of rendering beneath them.";
            message.IsVisible = true;
            foreach (var width in new[] { 900d, 1100d, 1400d })
            {
                window.Width = width;
                Drain();
                AssertControlsFit(browse, width, expectedChecked: false);
                AssertMessageFitsBetweenControlGroups(browse, width);

                var compareToggle = Descendant<ToggleButton>(
                    browse,
                    "CompareViewButton");
                compareToggle.Command!.Execute(compareToggle.CommandParameter);
                Drain();
                AssertControlsFit(browse, width, expectedChecked: true);

                compareToggle.Command!.Execute(compareToggle.CommandParameter);
                Drain();
                Assert.False(compareToggle.IsChecked);
            }
        }
        finally
        {
            window.DataContext = null;
            window.Close();
        }
    }

    private static void AssertMessageFitsBetweenControlGroups(
        BrowseGridView browse,
        double windowWidth)
    {
        var message = Descendant<TextBlock>(browse, "OnlineOnlyMessage");
        var state = Descendant<StackPanel>(browse, "ThumbnailSizePanel");
        var footer = Assert.IsAssignableFrom<Panel>(state.Parent);
        var messageOrigin = message.TranslatePoint(default, footer);
        var stateOrigin = state.TranslatePoint(default, footer);

        Assert.True(messageOrigin.HasValue && stateOrigin.HasValue);
        Assert.True(
            message.Bounds.Width == 0 ||
            (messageOrigin.Value.X >= 0 &&
             messageOrigin.Value.X + message.Bounds.Width <= stateOrigin.Value.X),
            $"OnlineOnlyMessage right edge " +
            $"{messageOrigin.Value.X + message.Bounds.Width} overlaps " +
            $"ThumbnailSizePanel left edge {stateOrigin.Value.X} at " +
            $"window width {windowWidth}.");
    }

    private static void AssertControlsFit(
        BrowseGridView browse,
        double windowWidth,
        bool expectedChecked)
    {
        var sizePanel = Descendant<StackPanel>(browse, "ThumbnailSizePanel");
        var footer = Assert.IsAssignableFrom<Panel>(sizePanel.Parent);
        var compareToggle = Descendant<ToggleButton>(
            browse,
            "CompareViewButton");
        Assert.Equal(expectedChecked, compareToggle.IsChecked);
        Assert.True(compareToggle.IsEffectivelyVisible);
        Assert.True(compareToggle.IsEffectivelyEnabled);
        var buttons = sizePanel.Children
            .OfType<Button>()
            .Where(button => button.IsEffectivelyVisible)
            .ToArray();
        Assert.Equal(
            [
                "LoupeViewButton",
                "CompareViewButton",
                "BurstsButton",
                "PairsButton",
                "SmallThumbnailButton",
                "MediumThumbnailButton",
                "LargeThumbnailButton"
            ],
            buttons.Select(button => button.Name));

        var panelOrigin = sizePanel.TranslatePoint(default, footer);
        Assert.True(sizePanel.Bounds.Width > 0,
            $"ThumbnailSizePanel has zero width at window width {windowWidth}.");
        Assert.True(panelOrigin.HasValue &&
            panelOrigin.Value.X >= 0 && panelOrigin.Value.Y >= 0 &&
            panelOrigin.Value.X + sizePanel.Bounds.Width <= footer.Bounds.Width &&
            panelOrigin.Value.Y + sizePanel.Bounds.Height <= footer.Bounds.Height,
            $"ThumbnailSizePanel at {panelOrigin} with bounds {sizePanel.Bounds} " +
            $"exceeds footer bounds {footer.Bounds} at window width {windowWidth}.");

        foreach (var button in buttons)
        {
            Assert.True(button.Bounds.Width > 0,
                $"{button.Name} has zero width at window width {windowWidth}.");
            Assert.True(button.Bounds.X >= 0 && button.Bounds.Y >= 0 &&
                button.Bounds.Right <= sizePanel.Bounds.Width &&
                button.Bounds.Bottom <= sizePanel.Bounds.Height,
                $"{button.Name} bounds {button.Bounds} exceed parent bounds " +
                $"{sizePanel.Bounds} at window width {windowWidth}.");
        }
    }

    private static T Descendant<T>(Control root, string name)
        where T : Control =>
        root.GetVisualDescendants().Prepend(root)
            .OfType<T>()
            .Single(control => control.Name == name);

    private static void Drain() => Dispatcher.UIThread.RunJobs();
}
