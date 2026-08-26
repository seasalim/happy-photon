using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;

namespace HappyPhoton.Views;

public partial class MainWindow : Window
{
    private ZoomPanControl? _zoomPanControl;
    private ZoomPanControl? _fullScreenZoomPanControl;
    private FolderTreePanel? _folderTreePanel;
    private PresetsPanel? _presetsPanel;
    private BrowseGridView? _browseGridView;
    private CompareView? _compareView;
    private bool _isDevelopViewportPublicationSuppressed;

    public MainWindow()
    {
        InitializeComponent();
        InitializeWindowChrome();
        HookFullScreenExitReveal();
        AddHandler(
            KeyDownEvent,
            OnWorkspaceKeyDown,
            RoutingStrategies.Tunnel);
        AddHandler(
            KeyUpEvent,
            OnWorkspaceKeyUp,
            RoutingStrategies.Tunnel);

        var developViewerPane =
            this.FindControl<DevelopViewerPane>("DevelopViewerPane");
        _zoomPanControl = developViewerPane?.Viewer;
        if (_zoomPanControl != null)
        {
            _zoomPanControl.ZoomChanged += OnZoomChanged;
            _zoomPanControl.AutoFitRequested += OnAutoFitRequested;
            _zoomPanControl.WhiteBalancePickRequested += OnWhiteBalancePickRequested;
            _zoomPanControl.VisibleRegionChanged += OnVisibleRegionChanged;
            _zoomPanControl.RequiredDeviceLongEdgeChanged +=
                OnRequiredDeviceLongEdgeChanged;
        }

        _fullScreenZoomPanControl = this.FindControl<ZoomPanControl>("FullScreenZoomPanControl");
        if (_fullScreenZoomPanControl != null)
        {
            _fullScreenZoomPanControl.ZoomChanged += OnZoomChanged;
            _fullScreenZoomPanControl.AutoFitRequested += OnAutoFitRequested;
            _fullScreenZoomPanControl.RequiredDeviceLongEdgeChanged +=
                OnRequiredDeviceLongEdgeChanged;
        }

        _folderTreePanel = this.FindControl<FolderTreePanel>("FolderTreePanel");
        if (_folderTreePanel != null)
        {
            _folderTreePanel.FolderExpanding += OnFolderExpanding;
            _folderTreePanel.ChangeFolderRequested += OnChangeFolderRequested;
            _folderTreePanel.RefreshFolderRequested += OnRefreshFolderRequested;
            _folderTreePanel.ImportCatalogRequested += OnImportCatalogRequested;
            _folderTreePanel.PhotoNavigationRequested += OnPhotoNavigationRequested;
            _folderTreePanel.RevealFolderRequested += OnRevealFolderRequested;
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

        _browseGridView = this.FindControl<BrowseGridView>("BrowseGridView");
        if (_browseGridView != null)
        {
            _browseGridView.DevelopModeRequested += OnDevelopModeRequested;
            _browseGridView.SelectAllRequested += OnSelectAllRequested;
            _browseGridView.DeselectAllRequested += OnDeselectAllRequested;
            _browseGridView.DeleteRejectedRequested += OnDeleteRejectedRequested;
            _browseGridView.CopyImagePathsRequested += OnCopyImagePathsRequested;
            _browseGridView.RevealImageRequested += OnRevealImageRequested;
            _browseGridView.DeleteImagesRequested += OnDeleteImagesRequested;
            _browseGridView.SelectionChanged += OnSelectionChanged;
            _browseGridView.ImageSelectionToggled += OnImageSelectionToggled;
            _browseGridView.RangeSelectionRequested += OnRangeSelectionRequested;
            _browseGridView.ViewportRangeChanged += OnThumbnailViewportRangeChanged;
        }

        _compareView = _browseGridView?.FindControl<CompareView>("CompareView");
    }

    private void WithVm(Action<MainWindowViewModel> action)
    {
        if (DataContext is MainWindowViewModel vm) action(vm);
    }

    private async Task WithVmAsync(Func<MainWindowViewModel, Task> action)
    {
        if (DataContext is MainWindowViewModel vm) await action(vm);
    }

    private void OnDevelopModeRequested(object? sender, EventArgs e) =>
        WithVm(vm => vm.EnterDevelopModeCommand.Execute(null));

    private void OnSelectAllRequested(object? sender, EventArgs e) =>
        WithVm(vm => vm.SelectAllCommand.Execute(null));

    private void OnDeselectAllRequested(object? sender, EventArgs e) =>
        WithVm(vm => vm.DeselectAllCommand.Execute(null));

    private void OnDeleteRejectedRequested(object? sender, EventArgs e) =>
        WithVm(vm => vm.DeleteRejectedImagesCommand.Execute(null));

    private void OnCopyImagePathsRequested(object? sender, EventArgs e) =>
        WithVm(vm => vm.CopyImagePathsCommand.Execute(null));

    private void OnRevealImageRequested(object? sender, EventArgs e) =>
        WithVm(vm => vm.RevealImageCommand.Execute(null));

    private void OnDeleteImagesRequested(object? sender, EventArgs e) =>
        WithVm(vm => vm.DeleteImageCommand.Execute(null));

    private void OnSelectionChanged(object? sender, EventArgs e) =>
        WithVm(vm => vm.RefreshSelectedCount());

    private void OnImageSelectionToggled(object? sender, Models.ImageFile image) =>
        WithVm(vm => vm.ToggleImageSelection(image));

    private void OnRangeSelectionRequested(
        object? sender,
        (Models.ImageFile from, Models.ImageFile to) range) =>
        WithVm(vm => vm.SelectRange(range.from, range.to));

    private async void OnPresetClicked(object? sender, string presetId) =>
        await WithVmAsync(vm => vm.ApplyPresetAsync(presetId));

    private async void OnPresetHoverEnter(object? sender, string presetId) =>
        await WithVmAsync(vm => vm.PreviewPresetHoverAsync(presetId));

    private async void OnPresetHoverLeave(object? sender, string presetId) =>
        await WithVmAsync(vm => vm.RestoreFromHoverAsync());

    private void OnZoomChanged(object? sender, double delta)
    {
        if (!ReferenceEquals(sender, GetActiveZoomPanControl())) return;
        WithVm(vm => vm.AdjustZoom(delta));
    }

    private void OnAutoFitRequested(object? sender, double fitZoom)
    {
        if (!ReferenceEquals(sender, GetActiveZoomPanControl())) return;
        WithVm(vm => vm.ApplyFitZoom(fitZoom));
    }

    private void OnRequiredDeviceLongEdgeChanged(object? sender, int longEdge)
    {
        if (!ReferenceEquals(sender, GetActiveZoomPanControl())) return;
        WithVm(vm => vm.PublishRequiredDeviceLongEdge(longEdge));
    }

    private void OnVisibleRegionChanged(object? sender, Rect? region)
    {
        if (!ReferenceEquals(sender, _zoomPanControl) ||
            _isDevelopViewportPublicationSuppressed ||
            DataContext is not MainWindowViewModel vm ||
            !vm.IsDevelopPreviewSurfaceActive)
        {
            return;
        }

        vm.PublishNavigatorVisibleRegion(region);
    }

    private async void OnWhiteBalancePickRequested(
        object? sender,
        (double X, double Y) position) =>
        await WithVmAsync(vm =>
            vm.ApplyWhiteBalancePickAsync(position.X, position.Y));

    private Task<bool> ConfirmMoveToTrashAsync(int count, string? fileName)
    {
        var message = count == 1
            ? $"Move \"{fileName}\" to Trash?"
            : $"Move {count} images to Trash?";
        return ConfirmationDialog.ConfirmAsync(
            this,
            "Move to Trash",
            message,
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

    private Task<bool> ConfirmExportOverwriteAsync(
        int count,
        IReadOnlyList<string> paths)
    {
        var message = count == 1
            ? $"The file \"{Path.GetFileName(paths[0])}\" already exists. Overwrite?"
            : $"{count} export files already exist. Overwrite them?";
        return ConfirmationDialog.ConfirmAsync(
            this,
            "Confirm Overwrite",
            message);
    }

    private Task<bool> ConfirmExportHydrationAsync(ExportHydrationScope scope)
    {
        var noun = scope.FileCount == 1 ? "original" : "originals";
        return ConfirmationDialog.ConfirmAsync(
            this,
            "Download originals for export?",
            $"Exporting will download {scope.FileCount} online-only {noun} " +
            $"(approximately {FormatLogicalSize(scope.LogicalBytes)}).",
            cancelLabel: "Cancel",
            confirmLabel: "Download / Export");
    }

    private static string FormatLogicalSize(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var value = (double)Math.Max(0, bytes);
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return $"{value:0.#} {units[unit]}";
    }

    private Task ShowFileOperationFailuresAsync(
        IReadOnlyList<FileOperationFailure> failures)
    {
        var heading = failures.Count == 1
            ? "1 file was not fully moved to Trash:"
            : $"{failures.Count} files were not fully moved to Trash:";
        var details = string.Join(
            Environment.NewLine,
            failures.Select(failure =>
                $"{Path.GetFileName(failure.Path)} — {failure.Reason}"));

        return ConfirmationDialog.ShowMessageAsync(
            this,
            "Move to Trash",
            $"{heading}{Environment.NewLine}{Environment.NewLine}{details}");
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            if (!vm.IsWorkspaceInteractionEnabled)
            {
                base.OnKeyDown(e);
                return;
            }

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
                if (vm.IsBrowseGridVisible) ScrollSelectedIntoView(vm);
                e.Handled = true;
                return;
            }
            else if (e.Key == Key.Right)
            {
                vm.SelectNextImageCommand.Execute(null);
                if (vm.IsBrowseGridVisible) ScrollSelectedIntoView(vm);
                e.Handled = true;
                return;
            }

            // Up/Down: row navigation in the grid, and between pane rows in
            // compare, where the grid is hidden but its 2x2 still has rows.
            if (vm.IsBrowseGridVisible || vm.IsCompareMode)
            {
                if (e.Key == Key.Up)
                {
                    var itemsPerRow = _browseGridView?.GetItemsPerRow() ?? 1;
                    vm.SelectImageUp(itemsPerRow);
                    if (vm.IsBrowseGridVisible) ScrollSelectedIntoView(vm);
                    e.Handled = true;
                    return;
                }
                else if (e.Key == Key.Down)
                {
                    var itemsPerRow = _browseGridView?.GetItemsPerRow() ?? 1;
                    vm.SelectImageDown(itemsPerRow);
                    if (vm.IsBrowseGridVisible) ScrollSelectedIntoView(vm);
                    e.Handled = true;
                    return;
                }
            }

            // Paging and Home/End stay grid-only: compare has no scroll extent.
            if (vm.IsBrowseGridVisible)
            {
                if (e.Key == Key.PageUp)
                {
                    var itemsPerRow = _browseGridView?.GetItemsPerRow() ?? 1;
                    var rowsPerPage = _browseGridView?.GetRowsPerPage() ?? 1;
                    vm.SelectImageUp(itemsPerRow * rowsPerPage);
                    ScrollSelectedIntoView(vm);
                    e.Handled = true;
                    return;
                }
                else if (e.Key == Key.PageDown)
                {
                    var itemsPerRow = _browseGridView?.GetItemsPerRow() ?? 1;
                    var rowsPerPage = _browseGridView?.GetRowsPerPage() ?? 1;
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

    private void OnWorkspaceKeyDown(object? sender, KeyEventArgs e)
    {
        // Escape is not handled here. The window's Escape KeyBinding runs ahead of
        // this tunnel handler through the real input pipeline, so anything ranked
        // here would be dead in the app while still passing a test that raises
        // KeyDown directly. The whole ladder, loupe included, lives in
        // MainWindowViewModel.HandleEscape.
        if (e.Key == Key.Space &&
            e.KeyModifiers == KeyModifiers.None &&
            WorkspaceKeyRouting.TryHandleSpace(
                DataContext as MainWindowViewModel,
                toggleSelection: true))
        {
            e.Handled = true;
        }
    }

    private void OnWorkspaceKeyUp(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space &&
            e.KeyModifiers == KeyModifiers.None &&
            WorkspaceKeyRouting.TryHandleSpace(
                DataContext as MainWindowViewModel,
                toggleSelection: false))
        {
            e.Handled = true;
        }
    }

    private void ScrollSelectedIntoView(MainWindowViewModel vm)
    {
        if (_browseGridView == null || vm.SelectedImage == null) return;
        var index = vm.Browse.VisibleImages.IndexOf(vm.SelectedImage);
        _browseGridView.ScrollItemIntoView(index);
    }

}
