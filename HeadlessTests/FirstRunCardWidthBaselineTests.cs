using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Styling;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class FirstRunCardWidthBaselineTests(ITestOutputHelper output)
{
    [AvaloniaFact]
    public async Task ReportRealizedCardWidthsAndCaptureDarkSteps()
    {
        using var root = new TemporaryDirectory();
        var pictures = Directory.CreateDirectory(Path.Combine(root.Path, "Pictures")).FullName;
        var locations = new AppDataLocationService(new AppDataPlatformPaths(
            pictures, Path.Combine(root.Path, "pointer"),
            Path.Combine(root.Path, "data"), Path.Combine(root.Path, "cache")), _ => null);
        using var catalog = new CatalogService(Path.Combine(root.Path, "catalog"));
        await using var vm = new MainWindowViewModel(catalog);
        vm.PrepareFirstRunStorage(locations, null);
        vm.DetectLightroomAsync = (_, _) => Task.FromResult(new LightroomDetectionResult(
            true, [Path.Combine(root.Path, "Lightroom", "Sample.lrcat")]));
        vm.RequestFirstRunCatalogImportAsync = _ => Task.FromResult(false);
        vm.RequestFirstRunCatalogPathAsync = () => Task.FromResult<string?>(null);
        vm.CompleteDataLocationSetupAsync = () =>
        {
            vm.MarkFirstRunStorageCommitted();
            vm.ResumeFirstRunAfterStorage(pictures);

            return Task.CompletedTask;
        };
        vm.ShowFirstRunWelcome(pictures);
        var widths = new List<double>();
        Measure(FirstRunStep.Welcome, "WelcomeContinueButton", "welcome");
        await vm.ContinueFirstRunCommand.ExecuteAsync(null);
        Measure(FirstRunStep.Storage, "StorageContinueButton", "storage");
        await vm.ContinueFirstRunCommand.ExecuteAsync(null);
        Measure(FirstRunStep.Pictures, "PicturesDefaultButton", "pictures");
        await vm.StartInDefaultLocationCommand.ExecuteAsync(null);
        Measure(FirstRunStep.Lightroom, "LightroomImportButton", "lightroom");
        vm.SkipDetectedLightroomCommand.Execute(null);
        Measure(FirstRunStep.AllSet, "StartBrowsingButton", "all-set");
        output.WriteLine($"VISUALS-WP11 G2 max-min={widths.Max() - widths.Min():F3}");
        Assert.Equal(0, widths.Max() - widths.Min());

        void Measure(FirstRunStep step, string buttonName, string scene)
        {
            Assert.Equal(step, vm.FirstRunStep);
            var view = new FirstRunView { DataContext = vm };
            var window = new Window { Content = view };
            ShowcaseTestHelper.Capture($"wp11-first-run-{scene}-dark", window,
                new PixelSize(1600, 1000), ThemeVariant.Dark, _ =>
                {
                    var card = Assert.IsType<Border>(
                        Assert.IsType<ScrollViewer>(view.Content).Content);
                    var content = Assert.IsType<StackPanel>(card.Child);
                    var button = view.FindControl<Button>(buttonName)!;
                    var panel = button.GetLogicalAncestors().OfType<StackPanel>()
                        .Single(candidate => ReferenceEquals(candidate.Parent, content));
                    window.UpdateLayout();
                    Assert.True(panel.IsEffectivelyVisible, $"{step} was not realized");
                    Assert.True(panel.Bounds.Width > 0);
                    Assert.True(card.Bounds.Width > 0);
                    Assert.DoesNotContain(view.GetLogicalDescendants().OfType<TextBlock>(),
                        text => text.Text == "First Run");

                    if (step == FirstRunStep.Welcome)
                    {
                        var heading = panel.Children.OfType<TextBlock>().First();
                        Assert.Equal("Welcome to Happy Photon", heading.Text);
                        Assert.Equal(28, heading.FontSize);
                        Assert.Equal(TextWrapping.NoWrap, heading.TextWrapping);
                        Assert.Single(heading.TextLayout.TextLines);
                        Assert.True(heading.TextLayout.Width <= heading.Bounds.Width);
                    }

                    if (step == FirstRunStep.AllSet)
                    {
                        Assert.Equal("Start browsing", button.Content);
                        Assert.Equal(new[] { "quiet-button", "accent" }, button.Classes.Where(name => !name.StartsWith(':')));
                        Assert.Single(panel.GetLogicalDescendants().OfType<Button>());
                    }

                    if (step == FirstRunStep.Lightroom)
                    {
                        var skip = panel.GetLogicalDescendants().OfType<Button>()
                            .Single(candidate => Equals(candidate.Content, "Skip"));
                        Assert.Contains("quiet-button", skip.Classes);
                        Assert.True(skip.TranslatePoint(default, window)!.Value.X <
                                    button.TranslatePoint(default, window)!.Value.X);
                    }

                    widths.Add(card.Bounds.Width);
                    output.WriteLine($"VISUALS-WP11 G2 {step} width={card.Bounds.Width:F3}");
                });
        }
    }
}
