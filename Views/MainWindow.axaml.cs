using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;

namespace HappyPhoton.Views;

public partial class MainWindow : Window
{
    private ZoomPanControl? _zoomPanControl;
    private ZoomPanControl? _fullScreenZoomPanControl;
    private CurveView? _curveView;
    private BatchExportPanel? _batchExportPanel;
    private FolderTreePanel? _folderTreePanel;
    private PresetsPanel? _presetsPanel;
    private LibraryGridView? _libraryGridView;
    private CancellationTokenSource? _exportCts;

    public MainWindow()
    {
        InitializeComponent();
        InitializeWindowChrome();

        _zoomPanControl = this.FindControl<ZoomPanControl>("ZoomPanControl");
        if (_zoomPanControl != null)
        {
            _zoomPanControl.ZoomChanged += OnZoomChanged;
            _zoomPanControl.AutoFitRequested += OnAutoFitRequested;
        }

        _fullScreenZoomPanControl = this.FindControl<ZoomPanControl>("FullScreenZoomPanControl");
        if (_fullScreenZoomPanControl != null)
        {
            _fullScreenZoomPanControl.ZoomChanged += OnZoomChanged;
            _fullScreenZoomPanControl.AutoFitRequested += OnAutoFitRequested;
        }

        _curveView = this.FindControl<CurveView>("CurveView");
        if (_curveView != null)
        {
            _curveView.CurveChanged += OnCurveChanged;
        }

        _batchExportPanel = this.FindControl<BatchExportPanel>("BatchExportPanel");
        if (_batchExportPanel != null)
        {
            _batchExportPanel.CloseRequested += OnExportPanelCloseRequested;
            _batchExportPanel.ExportRequested += OnExportRequested;
            _batchExportPanel.CancelRequested += OnExportCancelRequested;
        }

        _folderTreePanel = this.FindControl<FolderTreePanel>("FolderTreePanel");
        if (_folderTreePanel != null)
        {
            _folderTreePanel.FolderExpanding += OnFolderExpanding;
            _folderTreePanel.ChangeFolderRequested += OnChangeFolderRequested;
            _folderTreePanel.PhotoNavigationRequested += OnPhotoNavigationRequested;
        }

        _presetsPanel = this.FindControl<PresetsPanel>("PresetsPanel");
        if (_presetsPanel != null)
        {
            _presetsPanel.PresetClicked += OnPresetClicked;
            _presetsPanel.PresetHoverEnter += OnPresetHoverEnter;
            _presetsPanel.PresetHoverLeave += OnPresetHoverLeave;
            _presetsPanel.SavePresetRequested += OnSavePresetRequested;
            _presetsPanel.RenamePresetRequested += OnRenamePresetRequested;
            _presetsPanel.DeletePresetRequested += OnDeletePresetRequested;
        }

        _libraryGridView = this.FindControl<LibraryGridView>("LibraryGridView");
        if (_libraryGridView != null)
        {
            _libraryGridView.DevelopModeRequested += OnDevelopModeRequested;
            _libraryGridView.SelectAllRequested += OnSelectAllRequested;
            _libraryGridView.DeselectAllRequested += OnDeselectAllRequested;
            _libraryGridView.BatchExportRequested += OnBatchExportRequested;
            _libraryGridView.DeleteRejectedRequested += OnDeleteRejectedRequested;
            _libraryGridView.SelectionChanged += OnSelectionChanged;
            _libraryGridView.ImageSelectionToggled += OnImageSelectionToggled;
            _libraryGridView.RangeSelectionRequested += OnRangeSelectionRequested;
            _libraryGridView.ViewportRangeChanged += OnThumbnailViewportRangeChanged;
        }
    }

    private void OnFolderExpanding(object? sender, Models.FolderNode node)
    {
        if (DataContext is MainWindowViewModel vm)
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
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select Root Folder",
            AllowMultiple = false
        });

        if (folders.Count > 0 && DataContext is MainWindowViewModel vm)
        {
            var path = folders[0].Path.LocalPath;
            vm.SetRootFolder(path);
            await PersistAppSettingsSafelyAsync(vm);
        }
    }

    private void OnDevelopModeRequested(object? sender, EventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            vm.EnterDevelopModeCommand.Execute(null);
        }
    }

    private void OnSelectAllRequested(object? sender, EventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            vm.SelectAllCommand.Execute(null);
        }
    }

    private void OnDeselectAllRequested(object? sender, EventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            vm.DeselectAllCommand.Execute(null);
        }
    }

    private void OnBatchExportRequested(object? sender, EventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            vm.ShowExportPanelCommand.Execute(null);
        }
    }

    private void OnDeleteRejectedRequested(object? sender, EventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            vm.DeleteRejectedImagesCommand.Execute(null);
        }
    }

    private void OnSelectionChanged(object? sender, EventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            vm.RefreshSelectedCount();
        }
    }

    private void OnImageSelectionToggled(object? sender, Models.ImageFile image)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            vm.ToggleImageSelection(image);
        }
    }

    private void OnRangeSelectionRequested(object? sender, (Models.ImageFile from, Models.ImageFile to) range)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            vm.SelectRange(range.from, range.to);
        }
    }

    private async void OnCurveChanged(object? sender, EventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            await vm.OnCurveChangedAsync();
        }
    }

    private async void OnPresetClicked(object? sender, string presetId)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            await vm.ApplyPresetAsync(presetId);
        }
    }

    private async void OnPresetHoverEnter(object? sender, string presetId)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            await vm.PreviewPresetHoverAsync(presetId);
        }
    }

    private async void OnPresetHoverLeave(object? sender, string presetId)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            await vm.RestoreFromHoverAsync();
        }
    }

    private void OnZoomChanged(object? sender, double delta)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            vm.AdjustZoom(delta);
        }
    }

    private void OnAutoFitRequested(object? sender, double fitZoom)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            vm.ZoomLevel = fitZoom;
        }
    }

    private void UpdateExportPanel(MainWindowViewModel vm)
    {
        if (_batchExportPanel == null) return;

        _batchExportPanel.SetImageCount(vm.SelectedCount);

        if (!string.IsNullOrEmpty(vm.CurrentFolderPath))
        {
            _batchExportPanel.SetDefaultOutputFolder(vm.CurrentFolderPath);
        }
    }

    private void OnExportPanelCloseRequested(object? sender, EventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            vm.HideExportPanelCommand.Execute(null);
        }
    }

    private void OnExportCancelRequested(object? sender, EventArgs e)
    {
        _exportCts?.Cancel();
    }

    private async void OnExportRequested(object? sender, EventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm || _batchExportPanel == null)
            return;

        // Don't trigger if already exporting
        if (_exportCts != null)
            return;

        // Copy settings from panel to ViewModel
        vm.ExportSettings.OutputFolder = _batchExportPanel.Settings.OutputFolder;
        vm.ExportSettings.Quality = _batchExportPanel.Settings.Quality;
        vm.ExportSettings.Format = _batchExportPanel.Settings.Format;
        vm.ExportSettings.ExportHiRes = _batchExportPanel.Settings.ExportHiRes;
        vm.ExportSettings.ExportWeb = _batchExportPanel.Settings.ExportWeb;
        vm.ExportSettings.ExportSmall = _batchExportPanel.Settings.ExportSmall;
        vm.ExportSettings.WebMaxSize = _batchExportPanel.Settings.WebMaxSize;
        vm.ExportSettings.SmallMaxSize = _batchExportPanel.Settings.SmallMaxSize;
        vm.ExportSettings.NamingPattern = _batchExportPanel.Settings.NamingPattern;

        var imagesToExport = vm.GetSelectedImages().ToList();
        var variants = vm.ExportSettings.GetActiveVariants();
        var useSubfolders = variants.Count > 1;
        var targetPaths = imagesToExport
            .SelectMany(image => variants.Select(variant =>
                vm.ExportSettings.GetOutputPath(image.FileName, variant, useSubfolders)))
            .ToList();
        var originalPaths = ExportSafety.BuildOriginalPathSet(
            vm.Library.AllImages.Select(image => image.FilePath));
        var blockedCount = targetPaths.Count(path => ExportSafety.IsOriginalPath(path, originalPaths));
        if (blockedCount > 0)
        {
            var noun = blockedCount == 1 ? "file" : "files";
            await ConfirmationDialog.ShowMessageAsync(this, "Export blocked",
                $"{blockedCount} export {noun} would overwrite original images. " +
                "Choose a different output folder or naming pattern.");
            return;
        }

        var existingFiles = targetPaths.Where(File.Exists).ToList();

        if (existingFiles.Count > 0)
        {
            var message = existingFiles.Count == 1
                ? $"The file \"{Path.GetFileName(existingFiles[0])}\" already exists. Overwrite?"
                : $"{existingFiles.Count} files already exist in the output folder. Overwrite?";

            var shouldOverwrite = await ConfirmationDialog.ConfirmAsync(
                this,
                "Confirm Overwrite",
                message);

            if (!shouldOverwrite)
                return;
        }

        _batchExportPanel.ShowProgress(true);
        _exportCts = new CancellationTokenSource();

        var progress = new Progress<(int current, int total, string fileName)>(p =>
        {
            _batchExportPanel.UpdateProgress(p.current, p.total, p.fileName);
        });

        try
        {
            var count = await vm.ExportBatchAsync(imagesToExport, progress, _exportCts.Token);
            _batchExportPanel.ClearProgress();
            vm.HideExportPanelCommand.Execute(null);
        }
        catch (OperationCanceledException)
        {
            _batchExportPanel.ClearProgress();
        }
        catch (Exception)
        {
            _batchExportPanel.ClearProgress();
        }
        finally
        {
            _exportCts?.Dispose();
            _exportCts = null;
        }
    }

    private Task<bool> ConfirmMoveToTrashAsync(string fileName)
    {
        return ConfirmationDialog.ConfirmAsync(
            this,
            "Move to Trash",
            $"Move \"{fileName}\" to Trash?",
            destructive: true);
    }

    private Task<bool> ConfirmDeleteRejectedAsync(int rejectedCount, string? folderPath)
    {
        var folder = string.IsNullOrWhiteSpace(folderPath) ? "the current folder" : folderPath;
        var message = rejectedCount == 1
            ? $"Move 1 rejected image from \"{folder}\" to Trash?"
            : $"Move {rejectedCount} rejected images from \"{folder}\" to Trash?";

        return ConfirmationDialog.ConfirmAsync(
            this,
            "Delete Rejected Images",
            message,
            destructive: true);
    }

    private Task<bool> ConfirmBatchApplyAsync(int count)
    {
        var message = count == 1
            ? "Apply copied edit settings to 1 image? This cannot be undone."
            : $"Apply copied edit settings to {count} images? This cannot be undone.";

        return ConfirmationDialog.ConfirmAsync(
            this,
            "Apply Edit Settings",
            message);
    }

    private Task ShowDeleteRejectedFailuresAsync(int failureCount)
    {
        var message = failureCount == 1
            ? "1 rejected image could not be moved to Trash."
            : $"{failureCount} rejected images could not be moved to Trash.";

        return ConfirmationDialog.ShowMessageAsync(
            this,
            "Delete Rejected Images",
            message);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            // C key: Toggle crop mode (in Develop mode)
            if (e.Key == Key.C && e.KeyModifiers == KeyModifiers.None &&
                vm.IsDevelopMode && !vm.IsFullScreenMode && vm.HasSelectedImage)
            {
                vm.ToggleCropModeCommand.Execute(null);
                e.Handled = true;
                return;
            }

            // Arrow keys: Navigation
            if (e.Key == Key.Left)
            {
                vm.SelectPreviousImageCommand.Execute(null);
                if (!vm.IsDevelopMode) ScrollSelectedIntoView(vm);
                e.Handled = true;
                return;
            }
            else if (e.Key == Key.Right)
            {
                vm.SelectNextImageCommand.Execute(null);
                if (!vm.IsDevelopMode) ScrollSelectedIntoView(vm);
                e.Handled = true;
                return;
            }

            // Up/Down/PageUp/PageDown: Row navigation (Library mode only)
            if (!vm.IsDevelopMode)
            {
                if (e.Key == Key.Up)
                {
                    var itemsPerRow = _libraryGridView?.GetItemsPerRow() ?? 1;
                    vm.SelectImageUp(itemsPerRow);
                    ScrollSelectedIntoView(vm);
                    e.Handled = true;
                    return;
                }
                else if (e.Key == Key.Down)
                {
                    var itemsPerRow = _libraryGridView?.GetItemsPerRow() ?? 1;
                    vm.SelectImageDown(itemsPerRow);
                    ScrollSelectedIntoView(vm);
                    e.Handled = true;
                    return;
                }
                else if (e.Key == Key.PageUp)
                {
                    var itemsPerRow = _libraryGridView?.GetItemsPerRow() ?? 1;
                    var rowsPerPage = _libraryGridView?.GetRowsPerPage() ?? 1;
                    vm.SelectImageUp(itemsPerRow * rowsPerPage);
                    ScrollSelectedIntoView(vm);
                    e.Handled = true;
                    return;
                }
                else if (e.Key == Key.PageDown)
                {
                    var itemsPerRow = _libraryGridView?.GetItemsPerRow() ?? 1;
                    var rowsPerPage = _libraryGridView?.GetRowsPerPage() ?? 1;
                    vm.SelectImageDown(itemsPerRow * rowsPerPage);
                    ScrollSelectedIntoView(vm);
                    e.Handled = true;
                    return;
                }
                else if (e.Key == Key.Home)
                {
                    vm.SelectFirstImage();
                    ScrollSelectedIntoView(vm);
                    e.Handled = true;
                    return;
                }
                else if (e.Key == Key.End)
                {
                    vm.SelectLastImage();
                    ScrollSelectedIntoView(vm);
                    e.Handled = true;
                    return;
                }
            }
        }

        base.OnKeyDown(e);
    }

    private void ScrollSelectedIntoView(MainWindowViewModel vm)
    {
        if (_libraryGridView == null || vm.SelectedImage == null) return;
        var index = vm.Library.VisibleImages.IndexOf(vm.SelectedImage);
        _libraryGridView.ScrollItemIntoView(index);
    }

}
