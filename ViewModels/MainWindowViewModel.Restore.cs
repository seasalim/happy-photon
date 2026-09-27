using CommunityToolkit.Mvvm.Input;

namespace HappyPhoton.ViewModels;

public partial class MainWindowViewModel
{
    public Func<Task>? RequestRestoreBackupAsync { get; set; }
    public Func<Task>? PresentRestoreNoticeAsync { get; set; }

    [RelayCommand]
    private Task RestoreBackup() => IsStartupError ? GuardGateActionAsync(() =>
        RequestRestoreBackupAsync?.Invoke() ?? Task.CompletedTask) : Task.CompletedTask;
}
