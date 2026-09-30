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

    // Plain keyboard moves re-anchor at the new focus, as a plain click does.
    private void FollowKeyboardFocus(MainWindowViewModel vm)
    {
        ScrollSelectedIntoView(vm);
        if (_browseGridView != null) _browseGridView.SelectionAnchor = vm.SelectedImage;
    }
}
