using CommunityToolkit.Mvvm.ComponentModel;

namespace HappyPhoton.ViewModels;

public partial class MainWindowViewModel
{
    private readonly LoadingMessageGrace _startupProgress;

    private bool _firstFramePainted;

    [ObservableProperty]
    private bool _isStartupProgressVisible;

    internal void MarkFirstFramePainted()
    {
        if (_firstFramePainted) return;

        _firstFramePainted = true;
        _startupProgress.Update(IsStartupInitializing);
    }
}
