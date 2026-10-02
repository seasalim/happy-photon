using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Styling;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class ImportChromeShowcaseTests
{
    [AvaloniaFact]
    public async Task BlockedMessageAppearsOnce()
    {
        const string message = "Close Lightroom before importing this catalog.";
        using var flow = new CatalogImportFlowViewModel(new CatalogImportFlowOperations(
            (_, _) => throw new InvalidDataException(message),
            (_, _) => throw new InvalidOperationException(),
            (_, _, _, _, _) => throw new InvalidOperationException(),
            (_, _) => throw new InvalidOperationException()), "C:\\Pictures\\Lightroom\\Sample.lrcat");
        var dialog = new ImportCatalogDialog(flow, "C:\\Pictures\\Lightroom\\Sample.lrcat");
        await WithDialogAsync(dialog, flow, async () =>
        {
            await TestWaits.UntilAsync(() => !flow.IsBusy && flow.FailureText != null);
            Assert.Single(dialog.GetLogicalDescendants().OfType<TextBlock>(),
                text => text.IsEffectivelyVisible && text.Text == message);
            Assert.False(dialog.FindControl<TextBlock>("ReportCountNote")!.IsVisible);
            // Only report numbers are Mono; the blocking prose keeps the body font.
            Assert.NotEqual(ThemeResourceTests.Resource<FontFamily>("FontLabel", ThemeVariant.Dark),
                dialog.FindControl<TextBlock>("OutcomeText")!.FontFamily);
            Capture("wp11-import-blocked", dialog);
        });
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ServiceProducedPreviewAndReportExplainChangedPhotoCount(bool applied)
    {
        using var directory = new TemporaryDirectory();
        using var catalog = new CatalogService(Path.Combine(directory.Path, "catalog"));
        await catalog.InitializeAsync();
        var pictures = Directory.CreateDirectory(Path.Combine(directory.Path, "Pictures")).FullName;
        var unchanged = Path.Combine(pictures, "unchanged.jpg");
        var id = await catalog.GetOrCreateImageAsync(unchanged);
        await catalog.MutateAssessmentsAsync([
            new AssessmentMutation(id, AssessmentAxes.Rating, Rating: 4)
        ]);
        var source = new LightroomCatalogContents("C:\\Pictures\\Lightroom\\Sample.lrcat",
            1303001, 13, true, AssessmentAxes.All, [new CatalogSourceRoot(pictures, 2)],
            [Record(pictures, "changed.jpg", 5), Record(pictures, "unchanged.jpg", 4)], []);
        var service = new CatalogImportService(catalog, _ => true);
        using var flow = new CatalogImportFlowViewModel(new CatalogImportFlowOperations(
            (_, _) => Task.FromResult(source),
            (_, _) => Task.FromResult<CatalogImportStoredSettings?>(null),
            (contents, mappings, policy, crops, token) =>
                service.CreatePreviewAsync(contents, mappings, policy, crops, token),
            (preview, token) => service.ApplyAsync(preview, token)), source.CatalogPath);
        var dialog = new ImportCatalogDialog(flow, source.CatalogPath);
        await WithDialogAsync(dialog, flow, async () =>
        {
            await TestWaits.UntilAsync(() => flow.CanApply);
            Assert.Equal(1, flow.Report!.UpdatedPhotos);
            Assert.Equal(1, flow.Report.Rating.Written);
            Assert.Equal(1, flow.Report.Rating.Unchanged);
            Assert.Equal("1 photos will be updated", dialog.FindControl<TextBlock>("HeadlineText")!.Text);
            var outcome = dialog.FindControl<TextBlock>("OutcomeText")!;
            Assert.StartsWith("Photos changing: 1", outcome.Text);
            Assert.Contains("Ratings · 1 to update · 1 already match", outcome.Text);
            Assert.Equal(ThemeResourceTests.Resource<FontFamily>("FontLabel", ThemeVariant.Dark), outcome.FontFamily);
            Assert.Contains("every Lightroom photo found at its mapped path",
                dialog.FindControl<TextBlock>("ReportCountNote")!.Text);
            Assert.Contains("Lightroom wins replaces", dialog.FindControl<TextBlock>("PolicyHelpText")!.Text);
            Assert.Equal(ThemeResourceTests.Resource<Color>("SurfaceLowColor", ThemeVariant.Dark),
                Assert.IsAssignableFrom<ISolidColorBrush>(dialog.Background).Color);
            var picker = dialog.FindControl<ComboBox>("PolicyPicker")!;
            picker.SelectedIndex = 1;
            await TestWaits.UntilAsync(() => flow.CanApply && flow.Policy == CatalogImportPolicy.FillEmptyOnly);
            Assert.StartsWith("Fill empty applies", dialog.FindControl<TextBlock>("PolicyHelpText")!.Text);
            picker.SelectedIndex = 0;
            await TestWaits.UntilAsync(() => flow.CanApply && flow.Policy == CatalogImportPolicy.LightroomWins);
            Assert.StartsWith("Lightroom wins replaces", dialog.FindControl<TextBlock>("PolicyHelpText")!.Text);

            if (!applied)
            {
                Capture("wp11-import-preview", dialog);
                return;
            }

            dialog.FindControl<Button>("ApplyButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await TestWaits.UntilAsync(() => flow.IsApplied && !flow.IsBusy);
            Assert.Equal("Imported metadata for 1 photos", dialog.FindControl<TextBlock>("HeadlineText")!.Text);
            Assert.StartsWith("Photos changing: 1", outcome.Text);
            Capture("wp11-import-report", dialog);
        });
    }

    [AvaloniaFact]
    public async Task SettingsStorageUsesSameCacheName()
    {
        using var directory = new TemporaryDirectory();
        var locations = new AppDataLocations(Path.Combine(directory.Path, "catalog"),
            Path.Combine(directory.Path, "cache"), AppDataLocationOrigin.Environment,
            AppDataLocationOrigin.Environment);
        using var catalog = new CatalogService(locations.CatalogRoot);
        await catalog.InitializeAsync();
        await using var vm = new MainWindowViewModel(catalog);
        vm.SetResolvedDataLocations(locations,
            new CatalogLocationMigrator(RestoreTestSupport.Service(directory.Path)));
        var dialog = new SettingsDialog(vm);
        dialog.FindControl<TabControl>("SettingsTabs")!.SelectedIndex = 2;
        ShowcaseTestHelper.Capture("wp11-settings-storage", dialog,
            new PixelSize(650, 610), ThemeVariant.Dark, _ =>
            {
                Assert.Contains(dialog.GetLogicalDescendants().OfType<TextBlock>(),
                    text => text.IsEffectivelyVisible && text.Text == "Regenerable cache");
            });
    }

    private static CatalogImportRecord Record(string root, string path, int rating) =>
        new(root, path, CatalogImportFact<int>.Mapped(rating),
            CatalogImportFact<ImageFlag>.Empty, CatalogImportFact<ColorLabel>.Empty, false);

    private static void Capture(string scene, ImportCatalogDialog dialog) =>
        ShowcaseTestHelper.Capture(scene, dialog, new PixelSize(720, 650), ThemeVariant.Dark);

    private static async Task WithDialogAsync(
        ImportCatalogDialog dialog, CatalogImportFlowViewModel flow, Func<Task> body)
    {
        using var theme = new TestUiScope(theme: ThemeVariant.Dark);

        try
        {
            // test-teardown-policy: allow - finally cancels, drains and closes the import flow.
            dialog.Show();
            await body();
        }
        finally
        {
            flow.CancelCurrentOperation();
            await TestWaits.UntilAsync(() => !flow.HasInFlightOperation);
            dialog.Close();
        }
    }
}
