using HappyPhoton.ViewModels;

namespace HappyPhoton.Views;

internal static class WorkspaceKeyRouting
{
    internal static bool TryHandleSpace(
        MainWindowViewModel? viewModel,
        bool toggleSelection)
    {
        if (viewModel is not { IsWorkspaceInteractionEnabled: true })
        {
            return false;
        }

        if (toggleSelection && !viewModel.IsExportMode)
        {
            viewModel.ToggleSelectionCommand.Execute(null);
        }

        return true;
    }
}
