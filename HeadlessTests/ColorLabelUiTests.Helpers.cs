using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using HappyPhoton.Views;

namespace HappyPhoton.Tests;

public sealed partial class ColorLabelUiTests
{
    private static List<Button> SwatchButtons(ImageAssessmentControl control) =>
        control.GetLogicalDescendants()
            .OfType<Button>()
            .Where(button => button.CommandParameter is ColorLabel)
            .ToList();

    private static void Click(Button button) =>
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static string NewRoot() =>
        Directory.CreateDirectory(Path.Combine(
            Path.GetTempPath(),
            $"happy-photon-label-ui-{Guid.NewGuid():N}")).FullName;

    private static MainWindowViewModel NewViewModel(CatalogService catalog) =>
        new(catalog, baseLoader: null, loadMetadataAsync: _ => Task.CompletedTask);
}
