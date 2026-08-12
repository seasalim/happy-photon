using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

/// <summary>
/// Color-label behavior that needs a live dispatcher or a realized visual tree:
/// the agent mutation path marshals to the UI thread, and the enum-driven rows only
/// prove themselves once they actually materialize.
/// </summary>
public sealed class ColorLabelUiTests
{
    [AvaloniaFact]
    public async Task SetColorLabel_ThroughAgentService_NormalizesDuplicatesAndReportsMissing()
    {
        var root = NewRoot();
        using var catalog = new CatalogService(Path.Combine(root, "catalog"));
        await catalog.InitializeAsync();
        var vm = NewViewModel(catalog);
        await using var imageService = new ImageService(catalog);
        var service = new AgentToolService(vm, imageService, catalog);

        var image = new ImageFile(Path.Combine(root, "first.jpg"));
        image.CatalogId = await catalog.GetOrCreateImageAsync(image.FilePath);
        vm.Library.SetImages([image]);
        var refreshes = 0;
        vm.Library.FilterChanged += (_, _) => refreshes++;

        var result = await service.SetColorLabelAsync(
            [image.FilePath, image.FilePath, "missing.jpg"],
            "purple");

        Assert.Equal([image.FilePath], result.Succeeded);
        Assert.Equal("missing.jpg", Assert.Single(result.Failed).Id);
        Assert.Equal(ColorLabel.Purple, image.ColorLabel);
        Assert.Equal(
            ColorLabel.Purple,
            (await catalog.LoadImageStatesAsync([image.FilePath]))[image.FilePath]
                .ColorLabel);
        Assert.Equal(1, refreshes);
    }

    [AvaloniaFact]
    public async Task SetColorLabel_ThroughAgentService_RejectsUnknownToken()
    {
        var root = NewRoot();
        using var catalog = new CatalogService(Path.Combine(root, "catalog"));
        await catalog.InitializeAsync();
        var vm = NewViewModel(catalog);
        await using var imageService = new ImageService(catalog);
        var service = new AgentToolService(vm, imageService, catalog);

        var image = new ImageFile(Path.Combine(root, "first.jpg"));
        image.CatalogId = await catalog.GetOrCreateImageAsync(image.FilePath);
        vm.Library.SetImages([image]);

        await Assert.ThrowsAsync<AgentToolException>(() =>
            service.SetColorLabelAsync([image.FilePath], "chartreuse"));
        Assert.Equal(ColorLabel.None, image.ColorLabel);
    }

    [AvaloniaFact]
    public void AssessmentSwatches_MaterializeOneButtonPerEnumSlot()
    {
        using var catalog = new CatalogService(NewRoot());
        var vm = NewViewModel(catalog);
        var control = new ImageAssessmentControl { DataContext = vm };
        var window = new Window { Content = control };
        window.Show();

        var swatches = SwatchButtons(control);
        Assert.Equal(vm.ColorLabelChoices.Count, swatches.Count);
        Assert.Equal(
            vm.ColorLabelChoices.Select(choice => choice.Value),
            swatches.Select(button => Assert.IsType<ColorLabel>(button.CommandParameter)));
    }

    [AvaloniaFact]
    public void AssessmentSwatches_FollowRenamedSlotsIntoAccessibilityText()
    {
        using var catalog = new CatalogService(NewRoot());
        var vm = NewViewModel(catalog);
        vm.SetColorLabelNames(new Dictionary<ColorLabel, string>(ColorLabelNames.Defaults)
        {
            [ColorLabel.Red] = "Select"
        });
        var control = new ImageAssessmentControl { DataContext = vm };
        var window = new Window { Content = control };
        window.Show();

        var red = Assert.Single(
            SwatchButtons(control),
            button => Equals(button.CommandParameter, ColorLabel.Red));
        Assert.Contains(
            "select",
            Avalonia.Automation.AutomationProperties.GetName(red) ?? string.Empty,
            StringComparison.OrdinalIgnoreCase);
    }

    private static List<Button> SwatchButtons(ImageAssessmentControl control) =>
        control.GetLogicalDescendants()
            .OfType<Button>()
            .Where(button => button.CommandParameter is ColorLabel)
            .ToList();

    private static string NewRoot() =>
        Directory.CreateDirectory(Path.Combine(
            Path.GetTempPath(),
            $"happy-photon-label-ui-{Guid.NewGuid():N}")).FullName;

    private static MainWindowViewModel NewViewModel(CatalogService catalog) =>
        new(catalog, baseLoader: null, loadMetadataAsync: _ => Task.CompletedTask);
}
