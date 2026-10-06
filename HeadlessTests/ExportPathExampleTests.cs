using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Styling;
using Avalonia.Threading;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class ExportPathExampleTests
{
    [AvaloniaFact]
    public async Task ExampleSitsUnderFilenamesAndShowsOnlyWithRealPath()
    {
        using var fixture = new CatalogVmFixture("export-path-example-ui");
        using var catalog = fixture.CreateCatalog();
        await using var vm = CreateVm(fixture, catalog);
        Stage(vm, fixture, destination: false);
        var window = new MainWindow { Width = 1200, Height = 700 };
        using var scope = TestUiScope.ForMainWindow(window, vm);
        var pane = window.FindControl<ExportSettingsPane>("ExportSettingsPane")!;
        var more = pane.FindControl<Expander>("ExportMoreOptions")!;
        var label = pane.GetLogicalDescendants().OfType<TextBlock>()
            .Single(text => text.Text == "Example for this photo");
        var section = (Panel)label.Parent!;
        var content = (Panel)more.Content!;
        var hint = content.Children.OfType<TextBlock>()
            .Single(text => text.Text == "{name} = original name · {date} = export date");

        Assert.Same(content, section.Parent);
        Assert.Equal(content.Children.IndexOf(hint) + 1, content.Children.IndexOf(section));
        var expanders = (Panel)more.Parent!;
        Assert.Same(expanders, ((Panel)expanders.Parent!).Children[^1]);

        var path = (TextBlock)section.Children[1];
        var help = (TextBlock)section.Children[2];
        Settle(window);
        Assert.False(more.IsExpanded);
        Assert.False(section.IsEffectivelyVisible);

        more.IsExpanded = true;
        Settle(window);
        Assert.False(section.IsVisible);
        Assert.Single(VisibleTexts(pane), text => text.Text?.Contains("destination") == true);

        vm.ExportSettings.OutputFolder = fixture.Path("copies");
        Settle(window);
        Assert.True(section.IsEffectivelyVisible);
        Assert.EndsWith(".jpg", path.Text);
        Assert.False(help.IsVisible);

        vm.ExportSettings.ExportWeb = true;
        Settle(window);
        Assert.True(help.IsEffectivelyVisible);
        Assert.Equal("Each size gets its own subfolder.", help.Text);

        var capture = vm.ActiveExportCapture!;
        vm.ActiveExportCapture = null;
        Settle(window);
        Assert.False(section.IsVisible);

        vm.ActiveExportCapture = capture;
        Settle(window);
        Assert.True(section.IsEffectivelyVisible);

        more.IsExpanded = false;
        Settle(window);
        Assert.False(section.IsEffectivelyVisible);
    }

    [AvaloniaTheory]
    [InlineData("export-more-options-example", false)]
    [InlineData("export-more-options-example-gray", true)]
    public async Task RenderExampleUnderFilenames(string scene, bool gray)
    {
        using var fixture = new CatalogVmFixture("export-path-example-shot");
        using var catalog = fixture.CreateCatalog();
        await using var vm = CreateVm(fixture, catalog);
        Stage(vm, fixture, destination: true);
        vm.ExportSettings.ExportWeb = true;
        var window = new MainWindow();
        using var scope = TestUiScope.ForMainWindow(window, vm, show: false);
        ShowcaseTestHelper.Capture(scene, scope, new PixelSize(1200, 700),
            gray ? HappyPhotonThemes.MidGray : ThemeVariant.Dark, shown =>
            {
                var pane = shown.FindControl<ExportSettingsPane>("ExportSettingsPane")!;
                pane.FindControl<Expander>("ExportMoreOptions")!.IsExpanded = true;
                Settle(shown);
                var section = pane.FindControl<StackPanel>("ExportPathExampleSection")!;
                section.BringIntoView();
                Settle(shown);
                Assert.True(section.IsEffectivelyVisible);
                var scroll = pane.FindControl<ScrollViewer>("ExportSettingsScroll")!;
                var bottom = section.TranslatePoint(new Point(0, section.Bounds.Height), scroll)!.Value.Y;
                Assert.InRange(bottom, 0, scroll.Viewport.Height);
            });
    }

    private static IEnumerable<TextBlock> VisibleTexts(Control root) =>
        root.GetLogicalDescendants().OfType<TextBlock>().Where(text => text.IsEffectivelyVisible);

    private static void Settle(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
    }

    private static MainWindowViewModel CreateVm(CatalogVmFixture fixture, CatalogService catalog) =>
        fixture.CreateViewModel(catalog, new NullBaseLoader(),
            loadMetadataAsync: _ => Task.CompletedTask,
            availabilityService: new TestSourceAvailabilityService(SourceAvailability.RequiresHydration));

    private static void Stage(MainWindowViewModel vm, CatalogVmFixture fixture, bool destination)
    {
        vm.ShowWorkspaceReady(MainWindowViewModel.CurrentFirstRunExperienceVersion);
        vm.Browse.SetImages([new ImageFile(fixture.Path("photo.jpg")) { PixelWidth = 800, PixelHeight = 600 }]);
        vm.Browse.SelectAllVisible();
        vm.RefreshSelectedCount();
        if (destination) vm.ExportSettings.OutputFolder = fixture.Path("copies");
        vm.SwitchToExportCommand.Execute(null);
    }
}
