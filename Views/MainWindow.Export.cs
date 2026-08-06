using HappyPhoton.ViewModels;

namespace HappyPhoton.Views;

public partial class MainWindow
{
    private bool _isExportDialogOpen;

    private async Task ShowExportDialogAsync(ExportDialogMode mode)
    {
        if (_isExportDialogOpen || DataContext is not MainWindowViewModel vm)
        {
            return;
        }

        _isExportDialogOpen = true;
        vm.ExportSettings.PropertyChanged += OnExportSettingsPropertyChanged;
        try
        {
            var images = vm.GetSelectedImages().ToList();
            var dialog = new BatchExportDialog(vm, images, mode);
            await dialog.ShowDialog<bool>(this);
        }
        finally
        {
            vm.ExportSettings.PropertyChanged -= OnExportSettingsPropertyChanged;
            _isExportDialogOpen = false;
        }
    }
}
