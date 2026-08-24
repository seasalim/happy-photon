using CommunityToolkit.Mvvm.Input;

namespace HappyPhoton.ViewModels;

public sealed record FileOperationFailure(string Path, string Reason);

public partial class MainWindowViewModel
{
    [RelayCommand]
    private async Task CopyImagePathsAsync()
    {
        var targets = ResolveActionTargets().Targets;
        if (targets.Count == 0 || CopyToClipboardAsync == null) return;

        try
        {
            await CopyToClipboardAsync(string.Join(
                Environment.NewLine,
                targets.Select(image => image.FilePath)));
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine(
                $"Copy paths failed: {exception.Message}");
            ShowTransientStatus("Unable to copy file paths");
        }
    }

    [RelayCommand]
    private async Task RevealImageAsync()
    {
        if (IsFullScreenMode || SelectedImage == null) return;

        if (!await _fileOperationService.RevealFileAsync(SelectedImage.FilePath))
            ShowTransientStatus("Unable to reveal the selected file");
    }

    public async Task RevealFolderAsync(string path)
    {
        if (!await _fileOperationService.OpenFolderAsync(path))
            ShowTransientStatus("Unable to reveal the selected folder");
    }
}
