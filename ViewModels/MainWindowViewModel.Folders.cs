using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using HappyPhoton.Models;
using HappyPhoton.Services;

namespace HappyPhoton.ViewModels;

public partial class MainWindowViewModel
{
    public async Task InitializeFolderTreeAsync(string? navigateToPath = null)
    {
        var result = await Task.Run(() =>
        {
            var root = _folderTreeService.GetPicturesFolderNode();
            root.IsExpanded = true;
            var selected = CanNavigateTo(root.Path, navigateToPath)
                ? NavigateToFolder(root, navigateToPath!)
                : root;
            return (root, selected);
        });

        RootFolders = new ObservableCollection<FolderNode> { result.root };
        SelectedFolder = result.selected;
    }

    private FolderNode NavigateToFolder(FolderNode rootNode, string targetPath)
    {
        // Get relative path segments from root to target
        var relativePath = Path.GetRelativePath(rootNode.Path, targetPath);
        var segments = relativePath.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);

        var currentNode = rootNode;
        foreach (var segment in segments)
        {
            // Load children if needed (lazy loading)
            if (currentNode.HasDummyChild)
            {
                _folderTreeService.LoadChildren(currentNode);
            }

            // Find matching child
            var childNode = currentNode.Children.FirstOrDefault(c =>
                c.Name.Equals(segment, StringComparison.OrdinalIgnoreCase));

            if (childNode == null)
                break;

            // Expand this node to show the path
            currentNode.IsExpanded = true;
            currentNode = childNode;
        }

        return currentNode;
    }

    partial void OnSelectedFolderChanged(FolderNode? oldValue, FolderNode? newValue)
    {
        if (oldValue != null)
        {
            oldValue.IsSelected = false;
        }

        if (newValue != null && !newValue.IsDummy)
        {
            newValue.IsSelected = true;
            _ = LoadFolderAsync(newValue.Path);
        }
    }

    public void LoadFolderChildren(FolderNode node)
    {
        if (node.HasDummyChild)
        {
            _folderTreeService.LoadChildren(node);
        }
    }

    public void SetRootFolder(string folderPath)
    {
        var node = new Models.FolderNode(folderPath);
        _folderTreeService.LoadChildren(node);

        // Assign new collection to trigger property change and re-subscribe to events
        RootFolders = new ObservableCollection<FolderNode> { node };

        // Expand and select the new root
        node.IsExpanded = true;
        SelectedFolder = node;
    }

    public async Task InitializeFolderTreeWithRootAsync(
        string rootPath,
        string? navigateToPath = null)
    {
        var result = await Task.Run(() =>
        {
            var root = new FolderNode(rootPath);
            _folderTreeService.LoadChildren(root);
            root.IsExpanded = true;
            var selected = CanNavigateTo(rootPath, navigateToPath)
                ? NavigateToFolder(root, navigateToPath!)
                : root;
            return (root, selected);
        });

        RootFolders = new ObservableCollection<FolderNode> { result.root };
        SelectedFolder = result.selected;
    }

    private static bool CanNavigateTo(string rootPath, string? targetPath) =>
        !string.IsNullOrEmpty(targetPath) &&
        Directory.Exists(targetPath) &&
        targetPath.StartsWith(rootPath, StringComparison.OrdinalIgnoreCase);

    // View Mode Methods

    [RelayCommand]
    private void ToggleViewMode()
    {
        if (IsFullScreenMode)
        {
            return;
        }

        if (IsCropMode)
        {
            CancelCrop();
            return;
        }
        IsDevelopMode = !IsDevelopMode;
    }

    [RelayCommand]
    private void SwitchToLibrary()
    {
        if (IsFullScreenMode)
        {
            return;
        }

        IsDevelopMode = false;
    }

    [RelayCommand]
    private void SwitchToDevelop()
    {
        if (IsFullScreenMode)
        {
            return;
        }

        if (HasSelectedImage)
        {
            IsDevelopMode = true;
        }
    }

    [RelayCommand]
    private async Task EnterDevelopModeAsync()
    {
        if (IsFullScreenMode)
        {
            return;
        }

        if (IsExportPanelVisible)
        {
            RequestExport?.Invoke();
            return;
        }
        if (IsCropMode)
        {
            await ApplyCropAsync();
            return;
        }
        if (IsDevelopMode)
        {
            IsDevelopMode = false;
        }
        else if (HasSelectedImage)
        {
            IsDevelopMode = true;
        }
    }

    partial void OnIsDevelopModeChanged(bool value)
    {
        OnPropertyChanged(nameof(CanSavePreset));

        // Load preview when entering Develop mode (if we have a selected image)
        if (value && SelectedImage != null)
        {
            _ = LoadPreviewAsync(SelectedImage);
        }
    }

    [RelayCommand]
    private void ToggleFullScreen()
    {
        if (IsFullScreenMode)
        {
            IsFullScreenMode = false;
            return;
        }

        if (!HasSelectedImage || IsExportPanelVisible || IsCropMode)
        {
            return;
        }

        IsFullScreenMode = true;
    }

    partial void OnIsFullScreenModeChanged(bool value)
    {
        CopyEditSettingsCommand.NotifyCanExecuteChanged();
        PasteEditSettingsCommand.NotifyCanExecuteChanged();

        if (value && SelectedImage != null)
        {
            _ = LoadPreviewAsync(SelectedImage);
        }
    }

    // Selection Methods
}
