using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using HappyPhoton.Models;
using HappyPhoton.ViewModels;

namespace HappyPhoton.Views;

public partial class BrowseGridView
{
    private void UpdateFilterBar()
    {
        UpdateFilterButtons();
        UpdateFlagFilterButtons();
        UpdateFilterOverflowFades();
    }

    private void UpdateFilterButtons()
    {
        FilterRawButton.Classes.Set(
            "active",
            FileTypeFilter == ImageFileTypeFilter.Raw);
        FilterJpegButton.Classes.Set(
            "active",
            FileTypeFilter == ImageFileTypeFilter.Jpeg);
    }

    private void UpdateFlagFilterButtons()
    {
        FlagFilterPickedButton.Classes.Set(
            "active",
            FlagFilter == HappyPhoton.Models.FlagFilter.Picked);
        FlagFilterRejectedButton.Classes.Set(
            "active",
            FlagFilter == HappyPhoton.Models.FlagFilter.Rejected);
    }

    private void UpdateFilterOverflowFades()
    {
        const double tolerance = 0.5;
        var maximumOffset = Math.Max(
            0,
            FilterScrollViewer.Extent.Width - FilterScrollViewer.Viewport.Width);
        var overflows = maximumOffset > tolerance;
        FilterLeftFade.IsVisible =
            overflows && FilterScrollViewer.Offset.X > tolerance;
        FilterRightFade.IsVisible =
            overflows && FilterScrollViewer.Offset.X < maximumOffset - tolerance;
    }

    private void OnFilterScrollChanged(object? sender, ScrollChangedEventArgs e) =>
        UpdateFilterOverflowFades();

    private void OnFilterScrollViewerSizeChanged(object? sender, SizeChangedEventArgs e) =>
        UpdateFilterOverflowFades();

    private void OnClearFiltersClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel)
            viewModel.Browse.ClearFilters();
    }

    private void OnFilterRawClick(object? sender, RoutedEventArgs e) =>
        FileTypeFilter = FileTypeFilter == ImageFileTypeFilter.Raw
            ? ImageFileTypeFilter.All
            : ImageFileTypeFilter.Raw;

    private void OnFilterJpegClick(object? sender, RoutedEventArgs e) =>
        FileTypeFilter = FileTypeFilter == ImageFileTypeFilter.Jpeg
            ? ImageFileTypeFilter.All
            : ImageFileTypeFilter.Jpeg;

    private void OnFlagFilterPickedClick(object? sender, RoutedEventArgs e) =>
        FlagFilter = FlagFilter == HappyPhoton.Models.FlagFilter.Picked
            ? HappyPhoton.Models.FlagFilter.All
            : HappyPhoton.Models.FlagFilter.Picked;

    private void OnFlagFilterRejectedClick(object? sender, RoutedEventArgs e) =>
        FlagFilter = FlagFilter == HappyPhoton.Models.FlagFilter.Rejected
            ? HappyPhoton.Models.FlagFilter.All
            : HappyPhoton.Models.FlagFilter.Rejected;

}
