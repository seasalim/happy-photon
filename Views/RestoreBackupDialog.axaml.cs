using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using HappyPhoton.ViewModels;

namespace HappyPhoton.Views;

public partial class RestoreBackupDialog : Window
{
    public RestoreBackupDialog() => InitializeComponent();
    public RestoreBackupDialog(string catalogRoot) : this()
    {
        DataContext = new RestoreBackupViewModel(catalogRoot)
        {
            ChooseFileAsync = async () =>
            {
                var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = "Choose a backup file", AllowMultiple = false,
                    FileTypeFilter = [new FilePickerFileType("Catalog backup") { Patterns = ["*.zip"] }]
                });
                return files.FirstOrDefault()?.TryGetLocalPath();
            },
            ConfirmAsync = message => ConfirmationDialog.ConfirmAsync(this, "Restore catalog", message,
                destructive: true, cancelLabel: "Cancel", confirmLabel: "Restore"),
            Accepted = (path, acknowledged, check) => Close((path, acknowledged, check))
        };
    }
    private void Cancel(object? sender, RoutedEventArgs args) => Close();
}
