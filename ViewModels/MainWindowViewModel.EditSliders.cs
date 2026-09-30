using CommunityToolkit.Mvvm.ComponentModel;
using HappyPhoton.Models;

namespace HappyPhoton.ViewModels;

public partial class MainWindowViewModel
{
    private int _activeSliderEditCount;

    private bool IsSliderEditActive => _activeSliderEditCount > 0 || IsLocalsGestureActive || IsSpotsGestureActive;

    [ObservableProperty]
    private double _exposure;

    [ObservableProperty]
    private int _brightness;

    [ObservableProperty]
    private int _contrast;

    [ObservableProperty]
    private int _saturation;

    [ObservableProperty]
    private int _vibrance;

    [ObservableProperty]
    private int _shadows;

    [ObservableProperty]
    private int _highlights;

    [ObservableProperty] private int _whites;
    [ObservableProperty] private int _blacks;

    [ObservableProperty]
    private int _texture;

    [ObservableProperty]
    private int _clarity;

    [ObservableProperty]
    private int _rotation;

    [ObservableProperty]
    private double _horizonRotation;

    [ObservableProperty]
    private string? _activePresetId;

    partial void OnExposureChanged(double value) => OnEditValueChanged();
    partial void OnBrightnessChanged(int value) => OnEditValueChanged();
    partial void OnContrastChanged(int value) => OnEditValueChanged();
    partial void OnSaturationChanged(int value) => OnEditValueChanged();
    partial void OnVibranceChanged(int value) => OnEditValueChanged();
    partial void OnShadowsChanged(int value) => OnEditValueChanged();
    partial void OnHighlightsChanged(int value) => OnEditValueChanged();
    partial void OnWhitesChanged(int value) => OnEditValueChanged();
    partial void OnBlacksChanged(int value) => OnEditValueChanged();

    partial void OnTextureChanged(int value) => OnEditValueChanged();

    partial void OnClarityChanged(int value) => OnEditValueChanged();

    partial void OnHorizonRotationChanged(double value) => OnHorizonRotationValueChanged();

    public void OnSliderEditStarted() => _activeSliderEditCount++;

    internal event Action? SliderEditsEnding;

    public void OnSliderEditCompleted(string? historyLabel = null, bool completeWheel = false)
    {
        if (_activeSliderEditCount == 0) return;

        _activeSliderEditCount--;
        if (_activeSliderEditCount != 0) return;

        if (completeWheel) CommitWheelEdit(historyLabel);
        else SchedulePreviewUpdate(historyLabel);
    }

    private void CommitWheelEdit(string? historyLabel)
    {
        if (SelectedImage is not { } image) return;

        _previewDebounce?.Cancel();
        SaveSlidersTo(image.EditSettings);
        RefreshDevelopGroupEdits();
        image.HasEdits = image.EditSettings.HasEdits;
        var before = (_lastSavedState ?? image.EditSettings).Clone();
        var previousIntent = _requestedPreviewIntent;
        var generation = RequestEditedRender();

        TrackPreviewDebounce(SaveEditSettingsCoreAsync(
            image, image.EditSettings, historyLabel, before: null, recordHistory: true,
            beforeSave: () => RenderCommittedEditAsync(image, before, generation, previousIntent)));
    }

    private void OnEditValueChanged()
    {
        if (_isLoadingImage || !CanEditSelectedImage)
        {
            return;
        }

        UpdateCanReset();
        SchedulePreviewUpdate();
    }

    private void OnHorizonRotationValueChanged()
    {
        var image = SelectedImage;
        if (_isLoadingImage || !CanEditSelectedImage || image == null) return;

        if (IsCropMode)
        {
            ScheduleCropPreviewUpdate();
            return;
        }

        image.EditSettings.HorizonRotation = HorizonRotation;
        SchedulePreviewUpdate();
    }

    private void SaveSlidersTo(EditSettings target)
    {
        target.Exposure = Exposure;
        SaveWhiteBalanceTo(target);
        SaveHighlightReconstructionTo(target);
        SaveDetailTo(target);
        SaveEffectsTo(target);
        SaveMixerTo(target);
        SaveLensTo(target);
        SaveGeometryTo(target);
        target.Brightness = Brightness;
        target.Contrast = Contrast;
        target.Saturation = Saturation;
        target.Vibrance = Vibrance;
        target.Shadows = Shadows;
        target.Highlights = Highlights;
        target.Whites = Whites;
        target.Blacks = Blacks;
        target.Texture = Texture;
        target.Clarity = Clarity;
        target.Rotation = Rotation;
        if (!IsCropMode)
            target.HorizonRotation = HorizonRotation;
        target.AppliedPresetId = ActivePresetId;
    }

    private void LoadSlidersFrom(EditSettings source)
    {
        Exposure = source.Exposure;
        LoadWhiteBalanceFrom(source);
        LoadHighlightReconstructionFrom(source);
        LoadDetailFrom(source);
        LoadEffectsFrom(source);
        LoadMixerFrom(source);
        LoadLensFrom(source);
        LoadGeometryFrom(source);
        Brightness = source.Brightness;
        Contrast = source.Contrast;
        Saturation = source.Saturation;
        Vibrance = source.Vibrance;
        Shadows = source.Shadows;
        Highlights = source.Highlights;
        Whites = source.Whites;
        Blacks = source.Blacks;
        Texture = source.Texture;
        Clarity = source.Clarity;
        Rotation = source.Rotation;
        HorizonRotation = source.HorizonRotation;
        CurrentCrop = source.Crop?.Clone();
        LoadCurrentCurveFrom(source);
        ActivePresetId = source.AppliedPresetId != null &&
                         PresetService.ContainsId(source.AppliedPresetId)
            ? source.AppliedPresetId
            : null;

        if (ActivePresetId == null && source.AppliedPresetId?.StartsWith("builtin_", StringComparison.Ordinal) == true)
        {
            source.AppliedPresetId = null;
        }
    }
}
