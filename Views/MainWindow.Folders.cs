using Avalonia.Platform.Storage;
using Avalonia.Threading;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;

namespace HappyPhoton.Views;

public partial class MainWindow
{
    private void OnFolderExpanding(object? sender, FolderNode node)
    {
        if (DataContext is MainWindowViewModel vm &&
            vm.IsWorkspaceInteractionEnabled)
        {
            vm.LoadFolderChildren(node);
        }
    }

    private void OnPhotoNavigationRequested(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(
            () => _libraryGridView?.Focus(),
            DispatcherPriority.Input);
    }

    private async void OnChangeFolderRequested(object? sender, EventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm ||
            !vm.IsWorkspaceInteractionEnabled)
        {
            return;
        }

        var path = await PickBrowseLocationAsync();
        if (path == null)
        {
            return;
        }

        var validation = vm.ValidateBrowseLocation(path);
        if (validation != BrowseLocationValidation.Valid)
        {
            await ShowBrowseLocationErrorAsync(validation);
            return;
        }

        vm.SetRootFolder(path);
        await PersistAppSettingsSafelyAsync(vm);
        FocusFolderTree();
    }

    private async void OnRefreshFolderRequested(object? sender, EventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm ||
            !vm.IsWorkspaceInteractionEnabled)
        {
            return;
        }

        var generation = await vm.RefreshCurrentFolderAsync();
        if (generation != 0)
        {
            QueueRefreshScroll(vm, generation);
        }
    }

    internal void QueueRefreshScroll(
        MainWindowViewModel vm,
        int generation)
    {
        PostRefreshScroll(
            vm,
            generation,
            () => ReferenceEquals(DataContext, vm),
            () => ScrollSelectedIntoView(vm),
            Dispatcher.UIThread.Post);
    }

    internal static void PostRefreshScroll(
        MainWindowViewModel vm,
        int generation,
        Func<bool> isDataContextCurrent,
        Action scroll,
        Action<Action, DispatcherPriority> post) =>
        post(() =>
        {
            if (isDataContextCurrent() &&
                vm.IsLibraryGenerationCurrent(generation))
            {
                scroll();
            }
        }, DispatcherPriority.Background);

    private async Task BrowseForFirstRunLocationAsync(MainWindowViewModel vm)
    {
        var path = await PickBrowseLocationAsync();
        if (path != null && ReferenceEquals(DataContext, vm))
        {
            await vm.CompleteFirstRunFromLocationAsync(path);
        }
    }

    private async Task<string?> PickBrowseLocationAsync()
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(
            new FolderPickerOpenOptions
            {
                Title = "Choose Where Happy Photon Should Browse",
                AllowMultiple = false
            });

        return folders.Count == 0 ? null : folders[0].Path.LocalPath;
    }

    private Task ShowBrowseLocationErrorAsync(BrowseLocationValidation validation)
    {
        var message = validation == BrowseLocationValidation.Catalog
            ? "Choose a folder outside the Happy Photon catalog. It contains application data."
            : "Happy Photon couldn't open that location. Choose another folder and try again.";
        return ConfirmationDialog.ShowMessageAsync(
            this,
            "Choose another location",
            message);
    }

    private async Task PersistFirstRunCompletionAsync(
        MainWindowViewModel vm,
        string path)
    {
        if (_appSettingsService == null)
        {
            throw new InvalidOperationException("Application settings are unavailable.");
        }

        await _appSettingsService.SaveAsync(new AppSettings
        {
            RootFolderPath = path,
            SelectedFolderPath = path,
            FirstRunExperienceVersion =
                MainWindowViewModel.CurrentFirstRunExperienceVersion,
            FileTypeFilter = vm.Library.FileTypeFilter,
            StripLocationData = vm.ExportSettings.StripLocationData,
            OutputSharpening = vm.ExportSettings.OutputSharpening,
            McpServerEnabled = vm.IsAgentServerEnabled,
            McpToken = vm.AgentToken
        });

        if (ReferenceEquals(DataContext, vm))
        {
            vm.SetRootFolder(path);
        }
    }

    private void FocusFolderTree()
    {
        Dispatcher.UIThread.Post(
            () => _folderTreePanel?.FocusTree(),
            DispatcherPriority.Input);
    }
}
