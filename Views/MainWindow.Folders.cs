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

    private async void OnImportCatalogRequested(object? sender, EventArgs e)
    {
        if (DataContext is MainWindowViewModel vm &&
            vm.IsWorkspaceInteractionEnabled)
        {
            await ShowImportCatalogAsync();
        }
    }

    private async Task<bool> ShowImportCatalogAsync(
        string? catalogPath = null,
        bool returnAppliedOnClose = false)
    {
        if (DataContext is not MainWindowViewModel vm) return false;

        if (string.IsNullOrWhiteSpace(catalogPath))
        {
            catalogPath = await PickLightroomCatalogPathAsync();
            if (catalogPath == null || !ReferenceEquals(DataContext, vm)) return false;
        }

        return await new ImportCatalogDialog(
            vm,
            catalogPath,
            returnAppliedOnClose).ShowDialog<bool>(this);
    }

    private async Task<string?> PickLightroomCatalogPathAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(
            new FilePickerOpenOptions
            {
                Title = "Choose a Lightroom Classic Catalog",
                AllowMultiple = false,
                FileTypeFilter =
                [
                    new FilePickerFileType("Lightroom Classic catalog")
                    {
                        Patterns = ["*.lrcat"]
                    }
                ]
            });
        return files.Count == 0 ? null : files[0].Path.LocalPath;
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
        var suggestedStartPath = string.IsNullOrWhiteSpace(vm.FirstRunDefaultPath)
            ? Environment.GetFolderPath(Environment.SpecialFolder.MyPictures)
            : vm.FirstRunDefaultPath;
        var path = await PickBrowseLocationAsync(suggestedStartPath);
        if (path != null && ReferenceEquals(DataContext, vm))
        {
            await vm.CompleteFirstRunFromLocationAsync(path);
        }
    }

    private async Task<string?> PickBrowseLocationAsync(
        string? suggestedStartPath = null)
    {
        var suggestedStart = await TryGetSuggestedStartFolderAsync(suggestedStartPath);
        var folders = await StorageProvider.OpenFolderPickerAsync(
            new FolderPickerOpenOptions
            {
                Title = "Choose Where Happy Photon Should Browse",
                AllowMultiple = false,
                SuggestedStartLocation = suggestedStart
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
            OutputSharpening = vm.ExportSettings.OutputSharpening
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
