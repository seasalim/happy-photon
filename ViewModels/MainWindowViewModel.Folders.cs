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

    public string BrowseEmptyHeading
    {
        get
        {
            if (Browse.TotalCount > 0)
            {
                return Browse.EmptyMessage;
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

    public string BrowseEmptyMessage
    {
        get
        {
            if (Browse.TotalCount > 0)
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
        NotifyBrowseEmptyStateChanged();
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
        if (!IsBrowseGenerationCurrent(publishedGeneration) ||
            !ReferenceEquals(SelectedFolder, folder))
        {
            return 0;
        }

        _folderTreeService.LoadChildren(folder);

        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        SelectedImage = Browse.VisibleImages.FirstOrDefault(image =>
            string.Equals(image.FilePath, previousPath, comparison))
            ?? Browse.FirstVisible();

        ShowTransientStatus($"Refreshed — {Browse.PhotoCountText}.");
        return publishedGeneration;
    }

    internal bool IsBrowseGenerationCurrent(int generation) =>
        generation != 0 &&
        generation == Volatile.Read(ref _browseGeneration);

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
        NotifyBrowseEmptyStateChanged();

    private void OnBrowseStateChanged(object? sender, EventArgs e)
    {
        SelectedCount = Browse.SelectedCount;
        RefreshOnlineOnlyPhotoCount();
        RestartBrowseSelectionSummary();
        NotifyBrowseEmptyStateChanged();
        ReconcileFullScreenSelection();
        NotifyImageNavigationCommandState();
    }

    private void NotifyBrowseEmptyStateChanged()
    {
        OnPropertyChanged(nameof(BrowseEmptyHeading));
        OnPropertyChanged(nameof(BrowseEmptyMessage));
    }

    // View Mode Methods

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDevelopMode))]
    [NotifyPropertyChangedFor(nameof(IsBrowseMode))]
    [NotifyPropertyChangedFor(nameof(IsExportMode))]
    [NotifyPropertyChangedFor(nameof(IsBrowseOrDevelopMode))]
    // IsBrowseGridVisible also reads IsCompareMode, whose own notification fires
    // while leaving Browse — only this one covers the return trip.
    [NotifyPropertyChangedFor(nameof(IsBrowseGridVisible))]
    [NotifyPropertyChangedFor(nameof(IsExportProofCaptionVisible))]
    private WorkspaceMode _workspaceMode;
    private WorkspaceMode _workspaceModeBeforeExport = WorkspaceMode.Browse;

    public bool IsDevelopMode
    {
        get => WorkspaceMode == WorkspaceMode.Develop;
        set => WorkspaceMode = value
            ? WorkspaceMode.Develop
            : WorkspaceMode.Browse;
    }

    public bool IsBrowseMode => WorkspaceMode == WorkspaceMode.Browse;

    public bool IsExportMode =>
        WorkspaceMode == WorkspaceMode.Export;

    public bool IsBrowseOrDevelopMode =>
        WorkspaceMode is WorkspaceMode.Browse or WorkspaceMode.Develop;

    public bool IsDevelopPreviewSurfaceActive =>
        IsDevelopMode && !IsFullScreenMode;

    public bool IsWorkspacePreviewSurfaceActive =>
        (IsDevelopMode || IsExportMode) && !IsFullScreenMode;

    public bool IsFullScreenPreviewSurfaceActive => IsFullScreenMode;

    [RelayCommand]
    private void ToggleViewMode()
    {
        if (IsFullScreenMode || IsExportMode)
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
    private void SwitchToBrowse()
    {
        if (IsFullScreenMode)
        {
            return;
        }

        // Compare is a Browse sub-state: the assignment below is a no-op here.
        CloseCompare();
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
    private void SwitchToExport()
    {
        if (IsFullScreenMode)
        {
            return;
        }

        if (IsCropMode)
        {
            CancelCrop();
        }
        _workspaceModeBeforeExport = WorkspaceMode;
        WorkspaceMode = WorkspaceMode.Export;
    }

    [RelayCommand]
    private async Task EnterDevelopModeAsync()
    {
        if (IsFullScreenMode)
        {
            return;
        }

        if (IsExportMode)
        {
            await RunExportAsync();
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

    partial void OnWorkspaceModeChanged(WorkspaceMode value)
    {
        if (value != WorkspaceMode.Browse) CloseCompare();
        // The compare gate reads the workspace too, so it needs the same re-notify.
        NotifyCompareGateChanged();
        if (value != WorkspaceMode.Export) SetProofDisplayed(false);
        var isDevelopMode = value == WorkspaceMode.Develop;
        var isPreviewWorkspace = value is WorkspaceMode.Develop or
            WorkspaceMode.Export;
        UpdateThumbnailPumpAdmission();
        if (!isDevelopMode)
        {
            CancelAdjacentPreviewWarm(true, dropRetained: true);
            ClearAlignmentGrid();
        }
        // Cancel in-flight resting work but keep the parent: mode round-trips
        // on the same image must stay armed (publication is surface-gated and
        // render-time guards catch real staleness). Only selection changes
        // clear the parent.
        CancelRestingPreview(clearParent: false);
        OnPropertyChanged(nameof(IsDevelopPreviewSurfaceActive));
        OnPropertyChanged(nameof(IsWorkspacePreviewSurfaceActive));
        UpdateNavigatorPreviewSurfaceActivity();
        OnPropertyChanged(nameof(CanSavePreset));
        NotifyWorkflowTourVisibilityChanged();
        NotifyWhiteBalanceCommandState();
        ToggleColorAssessmentModeCommand.NotifyCanExecuteChanged();
        ToggleBeforeAfterCommand.NotifyCanExecuteChanged();
        UndoCommand.NotifyCanExecuteChanged();
        RedoCommand.NotifyCanExecuteChanged();
        NotifyClippingCommandState();
        NotifyExportRunCommandState();
        if (value == WorkspaceMode.Export)
        {
            PrepareExportWorkspace();
        }
        if (isPreviewWorkspace && SelectedImage != null)
        {
            var generation = ReserveRenderOutcome();
            ApplySurfaceClearOutcome(SelectedImage, generation);
            if (!isDevelopMode && !IsFullScreenMode)
            {
                LeaveDevelopClippingSurface();
            }
            _ = LoadPreviewAsync(SelectedImage, generation);
        }
        else if (!isPreviewWorkspace && !IsFullScreenMode)
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
        else if (!isDevelopMode && !IsFullScreenMode)
        {
            LeaveDevelopClippingSurface();
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

        if (IsExportMode || IsCompareMode || !HasSelectedImage || IsCropMode)
        {
            return;
        }

        IsFullScreenMode = true;
    }

    partial void OnIsFullScreenModeChanged(bool value)
    {
        UpdateThumbnailPumpAdmission();
        if (value) CancelAdjacentPreviewWarm(true, dropRetained: true);
        CancelRestingPreview(clearParent: false);
        OnPropertyChanged(nameof(IsDevelopPreviewSurfaceActive));
        OnPropertyChanged(nameof(IsWorkspacePreviewSurfaceActive));
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
        else if (!value && IsBrowseMode)
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

}
