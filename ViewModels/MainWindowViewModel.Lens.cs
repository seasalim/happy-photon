using CommunityToolkit.Mvvm.ComponentModel;
using HappyPhoton.Models;
using HappyPhoton.Services;

namespace HappyPhoton.ViewModels;

public partial class MainWindowViewModel
{
    [ObservableProperty]
    private bool _lensDistortion = true;

    [ObservableProperty]
    private bool _lensChromaticAberration = true;

    [ObservableProperty]
    private bool _lensVignetting;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LensSourceText))]
    private LensPrescriptionSummary? _lensPrescription;

    public bool IsOpticsEnabled => IsHighlightHandlingEnabled;
    public bool HasLensDistortion => LensPrescription?.HasDistortion == true;
    public bool HasLensChromaticAberration =>
        LensPrescription?.HasChromaticAberration == true;
    public bool HasLensVignetting => LensPrescription?.HasVignetting == true;

    public string LensSourceText => LensPrescription?.HasAny == true
        ? $"{LensPrescription.LensName ?? "EMBEDDED LENS"} · {LensPrescription.Source}"
        : "NO CORRECTION DATA FOR THIS LENS";

    partial void OnLensDistortionChanged(bool value) => OnLensValueChanged();
    partial void OnLensChromaticAberrationChanged(bool value) => OnLensValueChanged();
    partial void OnLensVignettingChanged(bool value) => OnLensValueChanged();

    partial void OnLensPrescriptionChanged(LensPrescriptionSummary? value)
    {
        OnPropertyChanged(nameof(HasLensDistortion));
        OnPropertyChanged(nameof(HasLensChromaticAberration));
        OnPropertyChanged(nameof(HasLensVignetting));
    }

    private void OnLensValueChanged()
    {
        if (_isLoadingImage || !CanEditSelectedImage) return;
        UpdateCanReset();
        SchedulePreviewUpdate();
    }

    private void SaveLensTo(EditSettings target)
    {
        target.Lens.Distortion = LensDistortion;
        target.Lens.ChromaticAberration = LensChromaticAberration;
        target.Lens.Vignetting = LensVignetting;
    }

    private void LoadLensFrom(EditSettings source)
    {
        LensDistortion = source.Lens.Distortion;
        LensChromaticAberration = source.Lens.ChromaticAberration;
        LensVignetting = source.Lens.Vignetting;
    }

    private void ResetLensUi()
    {
        var lens = SelectedImage?.EditSettings.Lens ?? new LensSettings();
        LensDistortion = lens.BaselineDistortion;
        LensChromaticAberration = lens.BaselineChromaticAberration;
        LensVignetting = lens.BaselineVignetting;
    }

    internal void ApplyLensPrescription(
        bool isRawSource,
        LensPrescriptionSummary? prescription)
    {
        LensPrescription = isRawSource ? prescription : null;
        OnPropertyChanged(nameof(IsOpticsEnabled));
    }
}
