using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.LogicalTree;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class ExportBatchLayoutTests(ITestOutputHelper output)
{
    [AvaloniaTheory]
    [InlineData(1200, 700, false, false)]
    [InlineData(1200, 700, true, false)]
    [InlineData(800, 500, false, false)]
    [InlineData(800, 500, true, false)]
    [InlineData(800, 500, false, true)]
    [InlineData(800, 500, true, true)]
    public async Task FooterStaysVisibleWithFailuresAndWarnings(int width, int height, bool expanded, bool longSummary)
    {
        using var fixture = new CatalogVmFixture("export-layout");
        using var catalog = fixture.CreateCatalog();
        await using var vm = CreateVm(fixture, catalog);
        Stage(vm, fixture, empty: false);
        var window = new MainWindow { Width = width, Height = height };
        using var scope = TestUiScope.ForMainWindow(window, vm);
        using var theme = new TestUiScope(theme: HappyPhotonThemes.MidGray);
        var pane = window.FindControl<ExportSettingsPane>("ExportSettingsPane")!;
        var scroll = pane.FindControl<ScrollViewer>("ExportSettingsScroll")!;
        window.UpdateLayout();
        Assert.False(pane.FindControl<Expander>("ExportMoreOptions")!.IsExpanded);
        output.WriteLine($"shell={width}x{height} settings extent={scroll.Extent.Height} viewport={scroll.Viewport.Height} overflow={scroll.Extent.Height - scroll.Viewport.Height}");
        if (width == 1200) Assert.True(scroll.Extent.Height <= scroll.Viewport.Height);
        vm.ExportReport = Report(vm);
        vm.IsExportJobRunning = true;
        vm.ExportProgressText = "Exporting 4 of 12 files";
        if (longSummary)
        {
            var basename = new string('a', 240);
            vm.ExportReport = vm.ExportReport with
            {
                Heading = "Export blocked",
                Summary = $"{basename}.CR3 and {basename}.jpg are one capture shot RAW+JPEG. " +
                    $"Both would export to {basename}.jpg. Choose one in Browse and run Export again."
            };
        }
        vm.ExportSettings.WebMaxSizeText = "abc";
        var report = pane.FindControl<ExportReportCard>("ExportReport")!;
        report.FindControl<Expander>("ExportReportDetails")!.IsExpanded = expanded;
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        var button = pane.FindControl<Button>("RunExportButton")!;
        output.WriteLine($"longSummary={longSummary} button bottom={button.TranslatePoint(default, window)!.Value.Y + button.Bounds.Height} window height={height}");
        foreach (var control in new Control[] {
            pane.FindControl<TextBlock>("ExportCountLineText")!,
            pane.FindControl<TextBlock>("ExportValidationText")!,
            pane.FindControl<Button>("RunExportButton")!,
            report.FindControl<TextBlock>("ExportReportHeading")!,
            report.FindControl<Button>("OpenExportFolderButton")!,
            report.FindControl<Button>("RetryFailedExportButton")!,
            window.GetLogicalDescendants().OfType<ExportQueueStrip>().Single() })
        {
            Assert.True(control.IsEffectivelyVisible);
            var origin = control.TranslatePoint(default, window)!.Value;
            Assert.InRange(origin.X, 0, width - control.Bounds.Width);
            Assert.InRange(origin.Y, 0, height - control.Bounds.Height);
            Assert.True(control.Bounds.Height > 0);
        }
        output.WriteLine($"footer visible=true detailsExpanded={expanded}");
    }

    [AvaloniaFact]
    public async Task CustomFilenameEditorStaysVisibleWhileTypingNameAndDateTokens()
    {
        using var fixture = new CatalogVmFixture("export-filename-input");
        using var catalog = fixture.CreateCatalog();
        await using var vm = CreateVm(fixture, catalog);
        Stage(vm, fixture, empty: false);
        var window = new MainWindow { Width = 1200, Height = 700 };
        using var scope = TestUiScope.ForMainWindow(window, vm);
        var pane = window.FindControl<ExportSettingsPane>("ExportSettingsPane")!;
        pane.FindControl<Expander>("ExportMoreOptions")!.IsExpanded = true;
        var choice = pane.GetLogicalDescendants().OfType<ComboBox>().Single(box => box.ItemCount == 2);
        choice.SelectedIndex = 1;
        Dispatcher.UIThread.RunJobs();
        var field = pane.FindControl<TextBox>("ExportNamingPatternField")!;
        field.Focus();
        field.SelectAll();
        var typed = "";
        foreach (var character in "{name}_{date}")
        {
            typed += character;
            window.KeyTextInput(character.ToString());
            Dispatcher.UIThread.RunJobs();
            output.WriteLine($"typed={typed} choice={vm.ExportFilenameChoice} visible={field.IsEffectivelyVisible}");
            Assert.Equal(typed, field.Text);
            Assert.Equal(typed, vm.ExportSettings.NamingPattern);
            Assert.Equal(1, vm.ExportFilenameChoice);
            Assert.Equal(1, choice.SelectedIndex);
            Assert.True(field.IsEffectivelyVisible);
        }
    }

    [AvaloniaTheory]
    [InlineData("WebMaxSizeField", "web")]
    [InlineData("SmallMaxSizeField", "small")]
    public async Task TypedSizesAreValidatedBeforeEnterCanCreateAJob(string fieldName, string variantName)
    {
        using var fixture = new CatalogVmFixture("export-size-input");
        using var catalog = fixture.CreateCatalog();
        await using var vm = CreateVm(fixture, catalog);
        Stage(vm, fixture, empty: false);
        vm.ExportSettings.ExportHiRes = false;
        vm.ExportSettings.ExportWeb = variantName == "web";
        vm.ExportSettings.ExportSmall = variantName == "small";
        var window = new MainWindow { Width = 1200, Height = 700 };
        using var scope = TestUiScope.ForMainWindow(window, vm);
        var pane = window.FindControl<ExportSettingsPane>("ExportSettingsPane")!;
        var field = pane.FindControl<TextBox>(fieldName)!;
        foreach (var text in new[] { "abc", "0", "70000", "3000" })
        {
            field.Focus();
            field.SelectAll();
            window.KeyTextInput(text);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(text, field.Text);
            if (text == "3000")
            {
                Assert.True(vm.CanRunExport);
                var job = vm.ExportSettings.CreateJob(vm.ExportCaptures.Select(c => c.Image));
                Assert.All(job.Targets, target => Assert.Equal(3000, target.Recipe.MaxDimension));
            }
            else
            {
                Assert.False(vm.CanRunExport);
                Assert.False(pane.FindControl<Button>("RunExportButton")!.IsEffectivelyEnabled);
                Assert.True(pane.FindControl<TextBlock>("ExportValidationText")!.IsEffectivelyVisible);
                Assert.Contains("long edge", vm.ExportValidationReason);
                await vm.HandleEnterCommand.ExecuteAsync(null);
                Assert.Null(vm.ActiveExportJobTask);
                Assert.Equal(0, vm.ExportActivityScopeStartCount);
                Assert.Throws<InvalidOperationException>(() => vm.ExportSettings.CreateJob([]));
            }
        }
    }

    [AvaloniaTheory]
    [InlineData(1200, 700)]
    [InlineData(800, 500)]
    public async Task ProofToolbarStaysInsidePreview(int width, int height)
    {
        using var fixture = new CatalogVmFixture("export-proof-toolbar");
        using var catalog = fixture.CreateCatalog();
        await catalog.InitializeAsync();
        await using var vm = CreateVm(fixture, catalog);
        Stage(vm, fixture, empty: false);
        vm.ExportSettings.ShowProof = true;
        var window = new MainWindow { Width = width, Height = height };
        using var scope = TestUiScope.ForMainWindow(window, vm);
        var pane = window.FindControl<ExportPreviewPane>("ExportPreviewPane")!;
        var help = pane.FindControl<TextBlock>("ExportProofHelp")!;
        var chooser = pane.FindControl<ComboBox>("ExportProofSizeChooser")!;
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        var origin = chooser.TranslatePoint(default, pane)!.Value;
        output.WriteLine($"shell={width}x{height} pane={pane.Bounds.Size} helpWidth={help.Bounds.Width} chooser={origin} size={chooser.Bounds.Size}");
        Assert.True(help.IsEffectivelyVisible);
        Assert.True(help.Bounds.Width > 0);
        Assert.True(help.Bounds.Height > 0);
        Assert.Equal(176, chooser.Bounds.Width);
        Assert.InRange(origin.X, 0, pane.Bounds.Width - chooser.Bounds.Width);
        Assert.InRange(origin.Y, 0, pane.Bounds.Height - chooser.Bounds.Height);
    }

    [AvaloniaFact]
    public async Task ProofChooser_DoesNotStealBatchArrowNavigation()
    {
        using var fixture = new CatalogVmFixture("export-proof-navigation");
        using var catalog = fixture.CreateCatalog();
        await catalog.InitializeAsync();
        await using var vm = CreateVm(fixture, catalog);
        Stage(vm, fixture, empty: false);
        var window = new MainWindow { Width = 1200, Height = 700 };
        using var scope = TestUiScope.ForMainWindow(window, vm);
        var chooser = window.FindControl<ExportPreviewPane>("ExportPreviewPane")!
            .FindControl<ComboBox>("ExportProofSizeChooser")!;
        Assert.False(vm.ExportSettings.ShowProof);
        vm.ExportSettings.ShowProof = true;
        chooser.SelectedIndex = 1;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("Web", vm.SelectedExportProofSize!.Name);
        var batch = window.GetLogicalDescendants().OfType<ExportCapturePane>().Single()
            .GetLogicalDescendants().OfType<ListBox>().Single();
        batch.ContainerFromIndex(0)!.Focus();
        window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
        window.KeyRelease(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
        Dispatcher.UIThread.RunJobs();
        Assert.Same(vm.ExportCaptures[1], vm.ActiveExportCapture);
        Assert.Equal("Web", vm.SelectedExportProofSize.Name);
    }

    [AvaloniaFact]
    public async Task ChangePhotosMenu_OffersExactlyTwoScopedActions()
    {
        using var fixture = new CatalogVmFixture("export-menu");
        using var catalog = fixture.CreateCatalog();
        await using var vm = CreateVm(fixture, catalog);
        Stage(vm, fixture, empty: false);
        var pane = new ExportCapturePane { DataContext = vm };
        var window = new Window { Width = 250, Height = 636, Content = pane };
        using var scope = new TestUiScope(window);
        var button = pane.FindControl<Button>("ChangeExportPhotosButton")!;
        var flyout = Assert.IsType<MenuFlyout>(button.Flyout);
        flyout.ShowAt(button);
        try
        {
            Dispatcher.UIThread.RunJobs();
            var items = flyout.Items.Cast<MenuItem>().ToArray();
            Assert.Equal(2, items.Length);
            Assert.Equal("Choose in Browse…", items[0].Header);
            Assert.Same(vm.ChooseExportPhotosInBrowseCommand, items[0].Command);
            Assert.Equal("Use picked photos (0)", items[1].Header);
            Assert.False(items[1].IsEffectivelyEnabled);
            Assert.True(ToolTip.GetShowOnDisabled(items[1]));
            Assert.Equal("No picked photos in the current view", ToolTip.GetTip(items[1]));
            vm.ExportCaptures[0].Image.Flag = ImageFlag.Picked;
            vm.Browse.RefreshFilters();
            Dispatcher.UIThread.RunJobs();
            Assert.True(items[1].IsEffectivelyEnabled);
            Assert.Equal("Use picked photos (1)", items[1].Header);
            Assert.Contains("current Browse view", Assert.IsType<string>(ToolTip.GetTip(items[1])));
        }
        finally { flyout.Hide(); }
    }

    [AvaloniaTheory]
    [InlineData("export-ready-dark", 1200, 700, false, false)]
    [InlineData("export-min-report-gray", 800, 500, true, false)]
    [InlineData("export-empty-dark", 1200, 700, false, true)]
    public async Task RenderShowcase(string scene, int width, int height, bool report, bool empty)
    {
        using var fixture = new CatalogVmFixture("export-showcase");
        using var catalog = fixture.CreateCatalog();
        await using var vm = CreateVm(fixture, catalog);
        Stage(vm, fixture, empty);
        foreach (var capture in vm.ExportCaptures)
            vm.Browse.ReplaceThumbnail(capture.Image, new Bitmap(GoldenTestPaths.Asset("srgb-reference.jpg")));
        if (report) vm.ExportReport = Report(vm);
        var window = new MainWindow();
        using var scope = TestUiScope.ForMainWindow(window, vm, show: false);
        ShowcaseTestHelper.Capture(scene, scope, new PixelSize(width, height),
            report ? HappyPhotonThemes.MidGray : ThemeVariant.Dark);
    }

    private static MainWindowViewModel CreateVm(CatalogVmFixture fixture, CatalogService catalog) =>
        fixture.CreateViewModel(catalog, new NullBaseLoader(),
            loadMetadataAsync: _ => Task.CompletedTask,
            availabilityService: new TestSourceAvailabilityService(SourceAvailability.RequiresHydration));

    private static void Stage(MainWindowViewModel vm, CatalogVmFixture fixture, bool empty)
    {
        vm.ShowWorkspaceReady(MainWindowViewModel.CurrentFirstRunExperienceVersion);
        var photos = Enumerable.Range(0, empty ? 0 : 6).Select(index =>
            new ImageFile(fixture.Path($"photo-{Math.Max(0, index - 1)}.jpg"))
            {
                Version = index == 1 ? 2 : 1,
                VersionCount = index < 2 ? 2 : 1,
                VersionLabel = index == 1 ? "Warm" : "Natural",
                PixelWidth = 800, PixelHeight = 600
            }).ToArray();
        vm.Browse.SetImages(photos);
        vm.Browse.SelectAllVisible();
        vm.RefreshSelectedCount();
        vm.ExportSettings.ExportWeb = true;
        vm.ExportSettings.OutputFolder = fixture.Path("copies");
        vm.SwitchToExportCommand.Execute(null);
    }

    private static ExportRunReport Report(MainWindowViewModel vm)
    {
        var image = vm.ExportCaptures[0].Image;
        return new("Export finished with failures", "11 of 12 files exported.",
            [new(image, new("web", 2048), "web/photo-0-V1.jpg", "Destination unavailable")],
            [new(image, "profile", "Source profile unavailable; used sRGB")],
            vm.ExportSettings.OutputFolder, 11);
    }
}
