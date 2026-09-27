using Avalonia.Controls;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;

namespace HappyPhoton.Views;

public partial class StoragePanel : UserControl
{
    public StoragePanel()
    {
        InitializeComponent();
        Loaded += async (_, _) =>
        {
            if (DataContext is not StorageSettingsViewModel vm) return;
            vm.RequestRestoreAsync = () => new RestoreBackupDialog(vm.CatalogRoot, staged: true)
                .ShowDialog<(string Path, bool Acknowledged, CheckedCatalogBackup Check)?>(
                    (Window)TopLevel.GetTopLevel(this)!);
            await vm.RefreshPendingAsync();
        };
    }
}
