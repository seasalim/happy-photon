using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace HappyPhoton.ViewModels;

public partial class MainWindowViewModel
{
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UpgradeWithoutBackupCommand))]
    private bool _isUpgradeBackupFailure;

    internal void ShowUpgradeBackupFailure(string message)
    {
        IsSchemaMismatch = false;
        ShowStartupFailure(message);
        IsUpgradeBackupFailure = true;
    }

    [RelayCommand(CanExecute = nameof(IsUpgradeBackupFailure))]
    private Task UpgradeWithoutBackup()
    {
        _catalogService.UpgradeWithoutBackup = true;

        return RetryStartupAsync?.Invoke() ?? Task.CompletedTask;
    }
}
