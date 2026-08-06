using HappyPhoton.ViewModels;

namespace HappyPhoton.Views;

public partial class MainWindow
{
    private void OnThumbnailViewportRangeChanged(
        object? sender,
        (int StartIndex, int Count) range)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            vm.RequestThumbnailRange(range.StartIndex, range.Count);
        }
    }
}
