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
        PasteSettingsViewModel.AvailableGroups.Where(group =>
            _pasteGroups.GetValueOrDefault(group.Name, group.IsDefault)).ToArray();

    public void RestorePasteGroups(IReadOnlyDictionary<string, bool> groups)
    {
        _pasteGroups = new Dictionary<string, bool>(groups);
        OnPropertyChanged(nameof(PasteSettingsTooltip));
    }

    public Dictionary<string, bool> CapturePasteGroups() => new(_pasteGroups);

    [RelayCommand(CanExecute = nameof(CanPasteEditSettings))]
    private Task ChoosePasteSettingsAsync() => PasteEditSettingsCoreAsync(showDialog: true);

    private async Task<bool> ChoosePasteGroupsAsync(IReadOnlyList<ImageFile> targets, bool currentPhoto)
    {
        if (ShowPasteSettingsAsync == null) return false;

        var dialog = new PasteSettingsViewModel(_copiedSourceName!, targets.Count, _pasteGroups, currentPhoto,
            targets.Select(target => target.EditSettings).ToArray(),
            reframeCount: optics => CachedReframeCount(targets, optics));
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
