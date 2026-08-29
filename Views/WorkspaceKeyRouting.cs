using Avalonia.Controls;
using Avalonia.VisualTree;
using HappyPhoton.ViewModels;

namespace HappyPhoton.Views;

internal static class WorkspaceKeyRouting
{
    internal static bool TryHandleSpace(
        MainWindowViewModel? viewModel,
        object? focusedElement)
    {
        if (viewModel is not { IsWorkspaceInteractionEnabled: true })
        {
            return false;
        }

        if (focusedElement is not null &&
            (focusedElement is not Avalonia.Visual focused ||
             !IsWorkspaceSurfaceFocus(focused)))
        {
            return false;
        }

        if (viewModel.IsBrowseGridVisible)
        {
            viewModel.EnterLoupeCommand.Execute(null);
            return true;
        }

        if (viewModel.IsLoupeMode || viewModel.IsDevelopMode)
        {
            viewModel.ToggleActualSizeCommand.Execute(null);
            return true;
        }

        return false;
    }

    private static bool IsWorkspaceSurfaceFocus(Avalonia.Visual focused)
    {
        if (focused is TextBox or Button) return false;
        if (focused is BrowseGridView or DevelopViewerPane) return true;

        var ancestors = focused.GetVisualAncestors().ToArray();
        return !ancestors.Any(control => control is TextBox or Button) &&
               ancestors.Any(control =>
                   control is BrowseGridView or DevelopViewerPane);
    }
}
