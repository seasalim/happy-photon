using Avalonia.Controls;
using Avalonia.Platform.Storage;
using HappyPhoton.ViewModels;

namespace HappyPhoton.Views;

public partial class RawProfilePicker : UserControl
{
    private bool _restoringSelection;

    public RawProfilePicker() => InitializeComponent();

    private async void OnDropDownOpened(object? sender, EventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            await viewModel.OpenRawProfilePickerCommand.ExecuteAsync(null);
        }
    }

    private async void OnSelectionChanged(
        object? sender,
        SelectionChangedEventArgs e)
    {
        if (_restoringSelection ||
            DataContext is not MainWindowViewModel viewModel ||
            sender is not ComboBox comboBox ||
            comboBox.SelectedItem is not RawProfileOptionViewModel option)
        {
            return;
        }
        if (option.IsChooseFile)
        {
            RestoreSelection(comboBox, viewModel);
            await BrowseForRawProfileAsync(viewModel);
            return;
        }
        if (!option.IsProfile)
        {
            RestoreSelection(comboBox, viewModel);
            return;
        }
        await viewModel.SelectRawProfileAsync(option);
    }

    private void RestoreSelection(
        ComboBox comboBox,
        MainWindowViewModel viewModel)
    {
        _restoringSelection = true;
        comboBox.SelectedItem = viewModel.SelectedRawProfileOption;
        _restoringSelection = false;
    }

    private async Task BrowseForRawProfileAsync(MainWindowViewModel viewModel)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.StorageProvider == null)
        {
            return;
        }
        var files = await topLevel.StorageProvider.OpenFilePickerAsync(
            new FilePickerOpenOptions
            {
                Title = "Select camera profile",
                AllowMultiple = false,
                FileTypeFilter =
                [
                    new FilePickerFileType("DNG camera profiles")
                    {
                        Patterns = ["*.dcp"]
                    }
                ]
            });
        if (files.Count == 1 && files[0].TryGetLocalPath() is { } path)
        {
            await viewModel.AddRawProfileFileAsync(path);
        }
    }
}
