using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Styling;
using Avalonia.Threading;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class FirstRunChromeShowcaseTests
{
    [AvaloniaTheory]
    [InlineData(FirstRunStep.Welcome, "welcome")]
    [InlineData(FirstRunStep.Lightroom, "lightroom")]
    public async Task MiddleGray(FirstRunStep step, string scene)
    {
        using var root = new TemporaryDirectory();
        using var catalog = new CatalogService(Path.Combine(root.Path, "catalog"));
        await using var vm = new MainWindowViewModel(catalog);
        Stage(vm, step);
        var view = new FirstRunView { DataContext = vm };

        ShowcaseTestHelper.Capture($"wp11-first-run-{scene}-midgray",
            new Window { Content = view }, new PixelSize(1600, 1000), HappyPhotonThemes.MidGray);
    }

    [AvaloniaTheory]
    [InlineData("Back", "back")]
    [InlineData("Skip", "skip")]
    [InlineData("Import", "primary")]
    public async Task FooterHover(string content, string scene)
    {
        using var root = new TemporaryDirectory();
        using var catalog = new CatalogService(Path.Combine(root.Path, "catalog"));
        await using var vm = new MainWindowViewModel(catalog);
        Stage(vm, FirstRunStep.Lightroom);
        var view = new FirstRunView { DataContext = vm };

        ShowcaseTestHelper.Capture($"wp11-first-run-hover-{scene}",
            new Window { Content = view }, new PixelSize(1600, 1000), ThemeVariant.Dark, window =>
            {
                var button = VisibleButton(view, content);
                var point = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window);
                window.MouseMove(point!.Value);
                Dispatcher.UIThread.RunJobs();
                Assert.True(button.IsPointerOver);
                Assert.All(view.GetLogicalDescendants().OfType<Button>()
                        .Where(candidate => candidate.IsEffectivelyVisible && candidate.Classes.Contains("quiet-button")),
                    candidate => Assert.Equal(Avalonia.Layout.HorizontalAlignment.Center,
                        candidate.HorizontalContentAlignment));
            });
    }

    [AvaloniaFact]
    public async Task FooterDisabled()
    {
        using var root = new TemporaryDirectory();
        using var catalog = new CatalogService(Path.Combine(root.Path, "catalog"));
        await using var vm = new MainWindowViewModel(catalog);
        Stage(vm, FirstRunStep.Lightroom);
        vm.IsFirstRunBusy = true;
        var view = new FirstRunView { DataContext = vm };

        ShowcaseTestHelper.Capture("wp11-first-run-disabled-footer",
            new Window { Content = view }, new PixelSize(1600, 1000), ThemeVariant.Dark, _ =>
            {
                foreach (var label in new[] { "Back", "Skip", "Import" })
                {
                    Assert.False(VisibleButton(view, label).IsEffectivelyEnabled);
                }
            });
    }

    [AvaloniaFact]
    public async Task CardFitsNarrowWindowAndBackRestoresPrimaryFocus()
    {
        using var root = new TemporaryDirectory();
        using var catalog = new CatalogService(Path.Combine(root.Path, "catalog"));
        await using var vm = new MainWindowViewModel(catalog);
        Stage(vm, FirstRunStep.Pictures);
        var view = new FirstRunView { DataContext = vm };
        var window = new Window { Content = view, Width = 620, Height = 700 };
        using var scope = new TestUiScope(window);
        Dispatcher.UIThread.RunJobs();
        var card = Assert.IsType<Border>(Assert.IsType<ScrollViewer>(view.Content).Content);
        Assert.True(card.Bounds.Width <= 572);
        var position = card.TranslatePoint(default, window)!.Value;
        Assert.True(position.X >= 24);
        Assert.True(position.X + card.Bounds.Width <= 596);
        vm.BackFirstRunCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.True(view.FindControl<Button>("StorageContinueButton")!.IsFocused);
        Assert.DoesNotContain(view.GetLogicalDescendants().OfType<Button>(),
            button => Equals(button.Content, "Change…") && button.IsEffectivelyVisible);
    }

    [AvaloniaFact]
    public void EmptyFolderContextIsHiddenUntilSet()
    {
        var panel = new FolderTreePanel();
        using var scope = new TestUiScope(new Window { Content = panel });
        var labels = panel.GetLogicalDescendants().OfType<TextBlock>()
            .Where(text => text.Text?.StartsWith("Browsing from:") == true ||
                           text.Text?.StartsWith("Viewing:") == true).ToArray();
        Assert.Equal(2, labels.Length);
        Assert.All(labels, label => Assert.False(label.IsVisible));
        panel.BrowsingFolderName = "Pictures";
        panel.ViewingFolderName = "Holiday";
        Assert.All(labels, label => Assert.True(label.IsVisible));
    }

    private static Button VisibleButton(FirstRunView view, string content) =>
        view.GetLogicalDescendants().OfType<Button>().Single(button =>
            Equals(button.Content, content) && button.IsEffectivelyVisible);

    private static void Stage(MainWindowViewModel vm, FirstRunStep step)
    {
        vm.ShowFirstRunWelcome("C:\\Pictures");
        vm.ResumeFirstRunAfterStorage("C:\\Pictures");
        vm.DetectedLightroomCatalogPaths = ["C:\\Pictures\\Lightroom\\Sample.lrcat"];
        vm.DetectedLightroomCatalogPath = vm.DetectedLightroomCatalogPaths[0];
        vm.RequestFirstRunCatalogImportAsync = _ => Task.FromResult(false);
        vm.RequestFirstRunCatalogPathAsync = () => Task.FromResult<string?>(null);
        vm.FirstRunStep = step;
    }
}
