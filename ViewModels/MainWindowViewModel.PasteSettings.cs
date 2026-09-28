using CommunityToolkit.Mvvm.Input;
using HappyPhoton.Models;

namespace HappyPhoton.ViewModels;

public partial class MainWindowViewModel
{
    private Dictionary<string, bool> _pasteGroups = [];

    public Func<PasteSettingsViewModel, Task<bool>>? ShowPasteSettingsAsync { get; set; }

    public string PasteSettingsTooltip =>
        $"Paste settings (Ctrl+Shift+V)\n{string.Join(", ", RememberedPasteGroups.Select(group => group.Name))}\n" +
        "Choose settings: Ctrl+Alt+Shift+V or right-click";

    private EditSettingsGroup[] RememberedPasteGroups =>
        EditSettingsTransfer.Groups.Where(group => group.Kind == EditSettingsGroupKind.Look
            ? _pasteGroups.GetValueOrDefault(group.Name, group.IsDefault)
            : group.IsDefault).ToArray();

    public void RestorePasteGroups(IReadOnlyDictionary<string, bool> groups)
    {
        _pasteGroups = new Dictionary<string, bool>(groups);
        OnPropertyChanged(nameof(PasteSettingsTooltip));
    }

    public Dictionary<string, bool> CapturePasteGroups() => new(_pasteGroups);

    [RelayCommand(CanExecute = nameof(CanPasteEditSettings))]
    private Task ChoosePasteSettingsAsync() => PasteEditSettingsCoreAsync(showDialog: true);

    private async Task<bool> ChoosePasteGroupsAsync(int count, bool currentPhoto)
    {
        if (ShowPasteSettingsAsync == null) return false;

        var dialog = new PasteSettingsViewModel(_copiedSourceName!, count, _pasteGroups, currentPhoto);
        if (!await ShowPasteSettingsAsync(dialog) || !dialog.CanPaste) return false;

        var choice = dialog.CaptureChoice();
        RestorePasteGroups(choice);

        if (PersistAppSettingsAsync != null)
        {
            await PersistAppSettingsAsync();
        }

        return true;
    }
}
