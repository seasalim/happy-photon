using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.Threading;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

/// <summary>Opt-in gate measurements; compare with the recorded pre-change baseline.</summary>
public sealed class ExportWorkspaceBaselineMeasurements(ITestOutputHelper output)
{
    [AvaloniaTheory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task MeasureSettingsFitAndSizeBinding(int sample)
    {
        if (Environment.GetEnvironmentVariable("HP_EXPORT_BASELINE") != "1") return;
        using var fixture = new CatalogVmFixture("export-baseline");
        using var catalog = fixture.CreateCatalog();
        await using var vm = fixture.CreateViewModel(catalog, new NullBaseLoader(),
            loadMetadataAsync: _ => Task.CompletedTask,
            availabilityService: new TestSourceAvailabilityService(SourceAvailability.RequiresHydration));
        vm.ShowWorkspaceReady(MainWindowViewModel.CurrentFirstRunExperienceVersion);
        var photos = Enumerable.Range(0, 6).Select(index =>
            new ImageFile(fixture.Path($"photo-{Math.Max(0, index - 1)}.jpg"))
            {
                Version = index == 1 ? 2 : 1,
                VersionCount = index < 2 ? 2 : 1
            }).ToArray();
        vm.Browse.SetImages(photos);
        vm.Browse.SelectAllVisible();
        vm.RefreshSelectedCount();
        vm.ExportSettings.ExportWeb = true;
        vm.ExportSettings.OutputFolder = fixture.Path("copies");
        vm.SwitchToExportCommand.Execute(null);
        Assert.Equal(6, vm.ExportCaptures.Count);
        Assert.Equal(2, photos.Count(photo => photo.FilePath == photos[0].FilePath));
        var window = new MainWindow { Width = 1200, Height = 700 };
        using var scope = TestUiScope.ForMainWindow(window, vm);
        using var themeScope = new TestUiScope(theme: ThemeVariant.Dark);
        var pane = window.FindControl<ExportSettingsPane>("ExportSettingsPane")!;
        var scroll = pane.FindControl<ScrollViewer>("ExportSettingsScroll")!;
        foreach (var (width, height) in new[] { (1200, 700), (800, 500) })
        {
            window.Width = width;
            window.Height = height;
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            Assert.True(vm.IsExportMode);
            Assert.True(pane.IsEffectivelyVisible);
            Assert.Equal(250, pane.Bounds.Width);
            Assert.Equal(width, window.ClientSize.Width);
            Assert.Equal(height, window.ClientSize.Height);
            Assert.Equal(ThemeVariant.Dark, pane.ActualThemeVariant);
            output.WriteLine($"G-1 sample={sample} shell={width}x{height} paneWidth={pane.Bounds.Width} " +
                $"extent={scroll.Extent.Height} viewport={scroll.Viewport.Height} " +
                $"overflow={scroll.Extent.Height - scroll.Viewport.Height}");
        }
        window.Width = 1200;
        window.Height = 700;
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        var field = pane.FindControl<TextBox>("WebMaxSizeField")!;
        Assert.True(field.IsEnabled);
        Assert.Equal(2048, vm.ExportSettings.WebMaxSize);
        foreach (var text in new[] { "0", "abc", "70000", "3000" })
        {
            field.Text = text;
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            var variant = vm.ExportSettings.GetActiveVariants().Single(v => v.Name == "web");
            if (text != "3000")
            {
                Assert.Throws<InvalidOperationException>(() => vm.ExportSettings.CreateJob(photos));
                Assert.False(vm.CanRunExport);
                Assert.NotEmpty(vm.ExportValidationReason);
                output.WriteLine($"G-2 sample={sample} input={text} field={field.Text} jobCreated=false reason={vm.ExportValidationReason}");
                continue;
            }
            var job = vm.ExportSettings.CreateJob(photos);
            Assert.All(job.Targets.Where(target => target.Recipe.Name == "web"),
                target => Assert.Equal(3000, target.Recipe.MaxDimension));
            output.WriteLine($"G-2 sample={sample} input={text} field={field.Text} jobCreated=true jobMaxDimension={variant.MaxDimension}");
        }
    }
}

