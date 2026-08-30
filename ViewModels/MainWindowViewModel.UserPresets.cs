using HappyPhoton.Models;

namespace HappyPhoton.ViewModels;

public partial class MainWindowViewModel
{
    public async Task SaveCurrentAsPresetAsync(string name, string? overwriteId = null)
    {
        if (!CanSavePreset || SelectedImage == null)
        {
            return;
        }

        var image = SelectedImage;
        var source = image.EditSettings.Clone();
        SaveSlidersTo(source);

        var preset = await PresetService.SaveUserPresetAsync(name, source, overwriteId);
        if (!ReferenceEquals(image, SelectedImage)) return;
        ActivePresetId = preset.Id;
        SaveSlidersTo(image.EditSettings);
        image.HasEdits = image.EditSettings.HasEdits;
        await SaveEditSettingsAsync(image, image.EditSettings, recordHistory: false);
        if (!ReferenceEquals(image, SelectedImage)) return;
        _lastSavedState = image.EditSettings.Clone();
        UpdateCanReset();
    }

    public Task RenameUserPresetAsync(string id, string newName)
    {
        return PresetService.RenameUserPresetAsync(id, newName);
    }

    public async Task DeleteUserPresetAsync(string id)
    {
        await PresetService.DeleteUserPresetAsync(id);
        if (ActivePresetId == id)
        {
            ActivePresetId = null;
            UpdateCanReset();
        }
    }
}
