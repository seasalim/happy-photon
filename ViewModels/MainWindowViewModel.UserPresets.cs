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

        var source = SelectedImage.EditSettings.Clone();
        SaveSlidersTo(source);
        source.Curve = CurrentCurve?.Clone() ?? new CurveData();

        var preset = await PresetService.SaveUserPresetAsync(name, source, overwriteId);
        ActivePresetId = preset.Id;
        SaveSlidersTo(SelectedImage.EditSettings);
        SelectedImage.HasEdits = SelectedImage.EditSettings.HasEdits;
        await SaveEditSettingsAsync(SelectedImage);
        _lastSavedState = SelectedImage.EditSettings.Clone();
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
