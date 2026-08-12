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
    private LibraryGridView? _libraryGridView;

    public MainWindow()
    {
        InitializeComponent();
        InitializeWindowChrome();
        AddHandler(
            KeyDownEvent,
            OnWorkspaceKeyDown,
            RoutingStrategies.Tunnel);
        AddHandler(
            KeyUpEvent,
            OnWorkspaceKeyUp,
            RoutingStrategies.Tunnel);

        _zoomPanControl = this.FindControl<ZoomPanControl>("ZoomPanControl");
        if (_zoomPanControl != null)
        {
            _zoomPanControl.ZoomChanged += OnZoomChanged;
            _zoomPanControl.AutoFitRequested += OnAutoFitRequested;
            _zoomPanControl.WhiteBalancePickRequested += OnWhiteBalancePickRequested;
        }

        _fullScreenZoomPanControl = this.FindControl<ZoomPanControl>("FullScreenZoomPanControl");
        if (_fullScreenZoomPanControl != null)
        {
            _fullScreenZoomPanControl.ZoomChanged += OnZoomChanged;
            _fullScreenZoomPanControl.AutoFitRequested += OnAutoFitRequested;
        }

        _folderTreePanel = this.FindControl<FolderTreePanel>("FolderTreePanel");
        if (_folderTreePanel != null)
        {
            _folderTreePanel.FolderExpanding += OnFolderExpanding;
            _folderTreePanel.ChangeFolderRequested += OnChangeFolderRequested;
            _folderTreePanel.RefreshFolderRequested += OnRefreshFolderRequested;
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

    private void OnBatchExportRequested(object? sender, EventArgs e) =>
        WithVm(vm => vm.ShowExportDialogCommand.Execute(null));

    private void OnDeleteRejectedRequested(object? sender, EventArgs e) =>
        WithVm(vm => vm.DeleteRejectedImagesCommand.Execute(null));

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
        WithVm(vm => vm.ZoomLevel = fitZoom);
    }

    private async void OnWhiteBalancePickRequested(
        object? sender,
        (double X, double Y) position) =>
        await WithVmAsync(vm =>
            vm.ApplyWhiteBalancePickAsync(position.X, position.Y));

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

    private void OnWorkspaceKeyDown(object? sender, KeyEventArgs e)
    {
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
        if (_libraryGridView == null || vm.SelectedImage == null) return;
        var index = vm.Library.VisibleImages.IndexOf(vm.SelectedImage);
        _libraryGridView.ScrollItemIntoView(index);
    }

}
