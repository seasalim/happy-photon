using Avalonia.Threading;
using HappyPhoton.ViewModels;

namespace HappyPhoton.Views;

public partial class MainWindow
{
    private void ScheduleCompareFocusHandoff(MainWindowViewModel vm) =>
        Dispatcher.UIThread.Post(
            () =>
            {
                if (!ReferenceEquals(DataContext, vm)) return;
                if (vm.IsCompareMode)
                {
                    _compareView?.Focus();
                }
                else if (vm.IsBrowseGridVisible)
                {
                    // Compare also closes when the workspace leaves Browse;
                    // only restore focus once the grid is actually showing.
                    _browseGridView?.Focus();
                }
            },
            DispatcherPriority.Loaded);

    private void ScheduleLoupeFocusHandoff(MainWindowViewModel vm) =>
        Dispatcher.UIThread.Post(
            () =>
            {
                if (!ReferenceEquals(DataContext, vm)) return;
                if (vm.IsLoupeMode) _loupeView?.Focus();
                else if (vm.IsBrowseGridVisible) _browseGridView?.Focus();
            },
            DispatcherPriority.Loaded);
}
