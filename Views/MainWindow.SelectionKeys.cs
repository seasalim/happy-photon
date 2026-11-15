using Avalonia.Input;
using HappyPhoton.ViewModels;

namespace HappyPhoton.Views;

public partial class MainWindow
{
    // Shift+navigation keys extend the Browse grid selection from the anchor
    // that plain clicks and plain navigation keys leave behind.
    private bool TryHandleSelectionExtendKey(KeyEventArgs e, MainWindowViewModel vm)
    {
        if (e.KeyModifiers != KeyModifiers.Shift || !vm.IsBrowseGridVisible ||
            _browseGridView == null ||
            e.Key is not (Key.Left or Key.Right or Key.Up or Key.Down or
                Key.PageUp or Key.PageDown or Key.Home or Key.End))
        {
            return false;
        }

        var itemsPerRow = _browseGridView.GetItemsPerRow();
        var pageItems = itemsPerRow * _browseGridView.GetRowsPerPage();
        var anchor = _browseGridView.SelectionAnchor;

        _browseGridView.SelectionAnchor = e.Key switch
        {
            Key.Left => vm.ExtendSelection(anchor, -1),
            Key.Right => vm.ExtendSelection(anchor, 1),
            Key.Up => vm.ExtendSelection(anchor, -itemsPerRow),
            Key.Down => vm.ExtendSelection(anchor, itemsPerRow),
            Key.PageUp => vm.ExtendSelection(anchor, -pageItems),
            Key.PageDown => vm.ExtendSelection(anchor, pageItems),
            Key.Home => vm.ExtendSelectionToEdge(anchor, last: false),
            _ => vm.ExtendSelectionToEdge(anchor, last: true)
        };

        ScrollSelectedIntoView(vm);
        e.Handled = true;
        return true;
    }

    // Left/Right step everywhere. Up/Down also step between compare pane rows,
    // where the grid is hidden. Paging and Home/End stay grid-only: compare has
    // no scroll extent.
    private bool TryHandleNavigationKey(KeyEventArgs e, MainWindowViewModel vm)
    {
        var grid = vm.IsBrowseGridVisible;
        var rows = grid || vm.IsCompareMode;
        Action? navigate = e.Key switch
        {
            Key.Left => () => vm.SelectPreviousImageCommand.Execute(null),
            Key.Right => () => vm.SelectNextImageCommand.Execute(null),
            Key.Up when rows => () => vm.SelectImageUp(ItemsPerRow()),
            Key.Down when rows => () => vm.SelectImageDown(ItemsPerRow()),
            Key.PageUp when grid => () => vm.SelectImageUp(ItemsPerRow() * RowsPerPage()),
            Key.PageDown when grid => () => vm.SelectImageDown(ItemsPerRow() * RowsPerPage()),
            Key.Home when grid => vm.SelectFirstImage,
            Key.End when grid => vm.SelectLastImage,
            _ => null
        };

        if (navigate == null) return false;

        navigate();
        if (vm.IsBrowseGridVisible) FollowKeyboardFocus(vm);
        e.Handled = true;

        return true;
    }

    private int ItemsPerRow() => _browseGridView?.GetItemsPerRow() ?? 1;

    private int RowsPerPage() => _browseGridView?.GetRowsPerPage() ?? 1;

    internal void FollowBrowseNavigation()
    {
        if (DataContext is MainWindowViewModel { IsBrowseGridVisible: true } vm)
        {
            FollowKeyboardFocus(vm);
        }
    }

    // Plain navigation moves re-anchor at the new focus, as a plain click does.
    private void FollowKeyboardFocus(MainWindowViewModel vm)
    {
        ScrollSelectedIntoView(vm);
        if (_browseGridView != null) _browseGridView.SelectionAnchor = vm.SelectedImage;
    }
}
