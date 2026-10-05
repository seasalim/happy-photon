using HappyPhoton.ViewModels;

namespace HappyPhoton.Views;

public partial class MainWindow
{
    private bool _firstFramePainted;

    internal event Action? FirstFramePainted;

    private void InitializeStartupProgress()
    {
        Opened += (_, _) => RequestAnimationFrame(_ =>
        {
            _firstFramePainted = true;
            FirstFramePainted?.Invoke();
            (DataContext as MainWindowViewModel)?.MarkFirstFramePainted();
        });

        DataContextChanged += (_, _) =>
        {
            if (_firstFramePainted)
                (DataContext as MainWindowViewModel)?.MarkFirstFramePainted();
        };
    }
}
