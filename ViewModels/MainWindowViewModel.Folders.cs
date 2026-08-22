using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HappyPhoton.Models;
using HappyPhoton.Services;

namespace HappyPhoton.ViewModels;

public partial class MainWindowViewModel
{
    [ObservableProperty]
    private bool _currentFolderHasSubfolders;

    public string? BrowsingFolderName => RootFolders.FirstOrDefault()?.Name;
    public string? ViewingFolderName => SelectedFolder?.Name;

    public string LibraryEmptyHeading
    {
        get
        {
            if (Library.TotalCount > 0)
            {
                return Library.EmptyMessage;
            }

            if (string.IsNullOrWhiteSpace(CurrentFolderPath))
            {
                return "Select a folder to view photographs";
            }

            return CurrentFolderHasSubfolders
                ? $"No photographs directly inside {ViewingFolderName ?? "this folder"}"
                : "No supported photographs in this folder";
        }
    }

    public string LibraryEmptyMessage
    {
        get
        {
            if (Library.TotalCount > 0)
            {
                return string.Empty;
            }

            if (string.IsNullOrWhiteSpace(CurrentFolderPath))
            {
                return "Choose a folder in the folder tree.";
            }

            return CurrentFolderHasSubfolders
                ? "Choose one of its subfolders in the folder tree."
                : "Choose another folder or change where Happy Photon browses.";
        }
    }

    public void ClearFolderTree()
    {
        SelectedFolder = null;
        RootFolders = new ObservableCollection<FolderNode>();
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

        OnPropertyChanged(nameof(ViewingFolderName));
        NotifyLibraryEmptyStateChanged();
    }

    public Task<int> RefreshCurrentFolderAsync() =>
        RefreshCurrentFolderAsync(
            path => Task.Run(() => Directory.Exists(path)),
            LoadFolderAsync);

    internal async Task<int> RefreshCurrentFolderAsync(
        Func<string, Task<bool>> directoryExistsAsync,
        Func<string, Task<int>> loadFolderAsync)
    {
        var folder = SelectedFolder;
        if (folder is null || folder.IsDummy)
        {
            return 0;
        }

        var folderPath = folder.Path;
        var previousPath = SelectedImage?.FilePath;
        var exists = await directoryExistsAsync(folderPath);
        if (!ReferenceEquals(SelectedFolder, folder))
        {
            return 0;
        }

        if (!exists)
        {
            ShowTransientStatus(
                "Refresh skipped — the folder is no longer available.");
            return 0;
        }

        var publishedGeneration = await loadFolderAsync(folderPath);
        if (!IsLibraryGenerationCurrent(publishedGeneration) ||
            !ReferenceEquals(SelectedFolder, folder))
        {
            return 0;
        }

        _folderTreeService.LoadChildren(folder);

        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        SelectedImage = Library.VisibleImages.FirstOrDefault(image =>
            string.Equals(image.FilePath, previousPath, comparison))
            ?? Library.FirstVisible();

        ShowTransientStatus($"Refreshed — {Library.PhotoCountText}.");
        return publishedGeneration;
    }

    internal bool IsLibraryGenerationCurrent(int generation) =>
        generation != 0 &&
        generation == Volatile.Read(ref _libraryGeneration);

    public void LoadFolderChildren(FolderNode node)
    {
        if (node.HasDummyChild)
        {
            _folderTreeService.LoadChildren(node);
        }
    }

    public void SetRootFolder(string folderPath, bool selectRoot = true)
    {
        var node = _folderTreeService.CreateRootNode(folderPath);

        SelectedFolder = null;
        RootFolders = new ObservableCollection<FolderNode> { node };

        node.IsExpanded = true;
        if (selectRoot)
        {
            SelectedFolder = node;
        }
    }

    public async Task InitializeFolderTreeWithRootAsync(
        string rootPath,
        string? navigateToPath = null,
        bool selectFolder = true)
    {
        var result = await Task.Run(() =>
        {
            var root = _folderTreeService.CreateRootNode(rootPath);
            root.IsExpanded = true;
            var selected = CanNavigateTo(rootPath, navigateToPath)
                ? NavigateToFolder(root, navigateToPath!)
                : root;
            return (root, selected);
        });

        SelectedFolder = null;
        RootFolders = new ObservableCollection<FolderNode> { result.root };
        if (selectFolder)
        {
            SelectedFolder = result.selected;
        }
    }

    public string? GetAvailablePicturesPath() =>
        _folderTreeService.GetAvailablePicturesPath();

    public BrowseLocationValidation ValidateBrowseLocation(string? path) =>
        _folderTreeService.ValidateBrowseLocation(path);

    private bool CanNavigateTo(string rootPath, string? targetPath) =>
        _folderTreeService.IsWithinRoot(rootPath, targetPath);

    partial void OnRootFoldersChanged(ObservableCollection<FolderNode> value)
    {
        OnPropertyChanged(nameof(BrowsingFolderName));
    }

    partial void OnCurrentFolderHasSubfoldersChanged(bool value) =>
        NotifyLibraryEmptyStateChanged();

    private void OnLibraryStateChanged(object? sender, EventArgs e)
    {
        SelectedCount = Library.SelectedCount;
        RefreshOnlineOnlyPhotoCount();
        RestartLibrarySelectionSummary();
        NotifyLibraryEmptyStateChanged();
        ReconcileFullScreenSelection();
    }

    private void NotifyLibraryEmptyStateChanged()
    {
        OnPropertyChanged(nameof(LibraryEmptyHeading));
        OnPropertyChanged(nameof(LibraryEmptyMessage));
    }

    // View Mode Methods

    public bool IsDevelopPreviewSurfaceActive =>
        IsDevelopMode && !IsFullScreenMode;

    public bool IsFullScreenPreviewSurfaceActive => IsFullScreenMode;

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

        IsDevelopMode = true;
    }

    [RelayCommand]
    private async Task EnterDevelopModeAsync()
    {
        if (IsFullScreenMode)
        {
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
        if (!value) CancelAdjacentPreviewWarm(true, dropRetained: true);
        // Cancel in-flight resting work but keep the parent: mode round-trips
        // on the same image must stay armed (publication is surface-gated and
        // render-time guards catch real staleness). Only selection changes
        // clear the parent.
        CancelRestingPreview(clearParent: false);
        OnPropertyChanged(nameof(IsDevelopPreviewSurfaceActive));
        UpdateNavigatorPreviewSurfaceActivity();
        OnPropertyChanged(nameof(CanSavePreset));
        NotifyWorkflowTourVisibilityChanged();
        NotifyWhiteBalanceCommandState();
        ToggleColorAssessmentModeCommand.NotifyCanExecuteChanged();
        ToggleBeforeAfterCommand.NotifyCanExecuteChanged();
        UndoCommand.NotifyCanExecuteChanged();
        RedoCommand.NotifyCanExecuteChanged();
        NotifyClippingCommandState();

        // Load preview when entering Develop mode (if we have a selected image)
        if (value && SelectedImage != null)
        {
            var generation = ReserveRenderOutcome();
            ApplySurfaceClearOutcome(SelectedImage, generation);
            _ = LoadPreviewAsync(SelectedImage, generation);
        }
        else if (!value && !IsFullScreenMode)
        {
            var generation = ReserveRenderOutcome();
            ApplySurfaceClearOutcome(SelectedImage, generation);
            LeaveDevelopClippingSurface();
            _previewLoadingCts?.Cancel();
            _previewDebounce?.Cancel();
            if (SelectedImage is { IsRaw: true } image &&
                image.EditSettings.HasEdits)
            {
                _ = TrackDirectThumbnailOperation(
                    RefreshThumbnailAsync(image));
            }
            ImageService.Previews.FlushRenderedPreviewCache();
            ScheduleHistogramUpdate();
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

        if (!HasSelectedImage || IsCropMode)
        {
            return;
        }

        IsFullScreenMode = true;
    }

    partial void OnIsFullScreenModeChanged(bool value)
    {
        if (value) CancelAdjacentPreviewWarm(true, dropRetained: true);
        CancelRestingPreview(clearParent: false);
        OnPropertyChanged(nameof(IsDevelopPreviewSurfaceActive));
        OnPropertyChanged(nameof(IsFullScreenPreviewSurfaceActive));
        UpdateNavigatorPreviewSurfaceActivity();
        if (value)
        {
            ArmFullScreenSelection();
        }
        if (!value)
        {
            ReleaseFullScreenSelection();
        }

        CopyEditSettingsCommand.NotifyCanExecuteChanged();
        PasteEditSettingsCommand.NotifyCanExecuteChanged();
        NotifyWhiteBalanceCommandState();
        ToggleColorAssessmentModeCommand.NotifyCanExecuteChanged();
        ToggleBeforeAfterCommand.NotifyCanExecuteChanged();
        UndoCommand.NotifyCanExecuteChanged();
        RedoCommand.NotifyCanExecuteChanged();
        NotifyClippingCommandState();

        if (value && SelectedImage != null)
        {
            var generation = ReserveRenderOutcome();
            ApplySurfaceClearOutcome(SelectedImage, generation);
            LeaveDevelopClippingSurface();
            _ = LoadPreviewAsync(SelectedImage, generation);
        }
        else if (!value && IsDevelopMode &&
                 IsClippingOverlayLatched && SelectedImage != null)
        {
            RequestClippingOverlayRender();
        }
        else if (!value && !IsDevelopMode)
        {
            var generation = ReserveRenderOutcome();
            ApplySurfaceClearOutcome(SelectedImage, generation);
            _previewLoadingCts?.Cancel();
            _previewDebounce?.Cancel();
            if (SelectedImage is { IsRaw: true } image &&
                image.EditSettings.HasEdits)
            {
                _ = TrackDirectThumbnailOperation(
                    RefreshThumbnailAsync(image));
            }
            ImageService.Previews.FlushRenderedPreviewCache();
            ScheduleHistogramUpdate();
        }
    }

    // Selection Methods
}
