using HappyPhoton.Models;
using HappyPhoton.Services;

namespace HappyPhoton.ViewModels;

public partial class MainWindowViewModel
{
    private Dictionary<string, bool> _presetGroups = [];

    public IReadOnlyDictionary<string, bool> PresetGroups => _presetGroups;

    public void RestorePresetGroups(IReadOnlyDictionary<string, bool> groups)
    {
        _presetGroups = new Dictionary<string, bool>(groups);
        OnPropertyChanged(nameof(PresetGroups));
    }

    public async Task SetPresetGroupExpandedAsync(string group, bool expanded)
    {
        _presetGroups[group] = expanded;

        await PersistBrowsePreferenceAsync("Preset-group");
    }

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
        if (PresetService.GetById(id)?.IsBuiltIn == true) return;

        await PresetService.DeleteUserPresetAsync(id);
        if (ActivePresetId == id)
        {
            ActivePresetId = null;
            UpdateCanReset();
        }
    }

    /// <summary>
    /// Applies a preset to the current image, or untoggles if the same preset is already active.
    /// </summary>
    public async Task ApplyPresetAsync(string presetId)
    {
        DiscardSpotsGesture();
        DiscardLocalsGesture();
        if (!CanEditSelectedImage || SelectedImage == null) return;
        var image = SelectedImage;

        // Clear hover state - we're committing to this preset
        _isHoveringPreset = false;
        _preHoverSettings = null;
        _hoverPreviewCts?.Cancel();

        var preset = PresetService.GetById(presetId);
        if (preset == null) return;

        var removingLook = preset.IsBuiltIn && ActivePresetId == presetId;

        // Personal presets retain their whole-look reset behavior.
        if (!preset.IsBuiltIn && ActivePresetId == presetId)
        {
            await UntogglePresetAsync();
            return;
        }

        var previousSettings = CaptureLiveEditState();
        var previousIntent = _requestedPreviewIntent;
        var generation = RequestEditedRender();

        if (preset.IsBuiltIn)
        {
            var settings = previousSettings.Clone();

            if (removingLook) EditSettingsLook.Reset(settings);
            else EditSettingsLook.Apply(preset.Settings, settings);

            settings.AppliedPresetId = removingLook ? null : presetId;
            InstallDevelopDocument(image, settings, preserveCropDraft: true);
        }
        else
        {
            var currentCrop = CurrentCrop?.Clone();
            _isLoadingImage = true;
            LoadSlidersFrom(preset.Settings);
            LensProfileOverride = previousSettings.Lens.ProfileOverride;
            ActivePresetId = presetId;
            Rotation = SelectedImage.EditSettings.Rotation;
            HorizonRotation = SelectedImage.EditSettings.HorizonRotation;
            LoadGeometryFrom(previousSettings);
            CurrentCrop = currentCrop;
            _isLoadingImage = false;

            SelectedImage.EditSettings = previousSettings.Clone();
            EditSettingsTransfer.ApplyGroups(preset.Settings, SelectedImage.EditSettings, EditSettingsTransfer.LookGroups);
            SelectedImage.EditSettings.AppliedPresetId = presetId;
            LoadCurrentCurveFrom(SelectedImage.EditSettings);
            SelectedImage.HasEdits = true;
        }

        // Save to catalog
        try
        {
            await SaveEditSettingsAsync(
                SelectedImage, removingLook ? "Preset: None" : $"Preset: {preset.Name}", previousSettings);
        }
        catch
        {
            RollbackEditReservation(
                image,
                previousSettings,
                generation,
                previousIntent);
            throw;
        }

        _lastSavedState = SelectedImage.EditSettings.Clone();

        // Update preview and UI
        await UpdatePreviewWithCurrentSliders(generation: generation);
        UpdateCanReset();

        // Refresh thumbnail to reflect preset
        RefreshSelectedThumbnail();
    }
}
