using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using HappyPhoton.ViewModels;

namespace HappyPhoton.Views;

public partial class ExportSettingsPane : UserControl
{
    public ExportSettingsPane() => InitializeComponent();

    private async void OnBrowseClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm ||
            TopLevel.GetTopLevel(this)?.StorageProvider is not { } provider) return;
        var folders = await provider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select Export Folder",
            AllowMultiple = false
        });
        if (folders.Count > 0) vm.ExportSettings.OutputFolder = folders[0].Path.LocalPath;
    }
}

