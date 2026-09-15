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

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedLensName))]
    private string? _lensProfileOverride;

    public IReadOnlyList<string> LensChoices { get; private set; } = ["Automatic"];
    public bool HasLensChoices => LensPrescription?.Camera != null;
    public string SelectedLensName
    {
        get => LensProfileOverride ?? "Automatic";
        set
        {
            if (value != null) LensProfileOverride = value == "Automatic" ? null : value;
        }
    }

    partial void OnLensProfileOverrideChanged(string? value) => OnLensValueChanged();

    public bool IsOpticsEnabled => IsHighlightHandlingEnabled;
    public bool HasLensDistortion => LensPrescription?.HasDistortion == true;
    public bool HasLensChromaticAberration =>
        LensPrescription?.HasChromaticAberration == true;
    public bool HasLensVignetting => LensPrescription?.HasVignetting == true;

    public string LensSourceText => LensPrescription?.HasAny == true
        ? $"{LensPrescription.LensName ?? "EMBEDDED LENS"} · {LensPrescription.Source}" +
            (LensPrescription.IsManual ? " · MANUAL" : string.Empty)
        : LensPrescription?.IsManual == true
            ? $"{LensPrescription.LensName} · NO CORRECTION DATA · MANUAL"
            : "NO CORRECTION DATA FOR THIS LENS";

    partial void OnLensDistortionChanged(bool value) => OnLensValueChanged();
    partial void OnLensChromaticAberrationChanged(bool value) => OnLensValueChanged();
    partial void OnLensVignettingChanged(bool value) => OnLensValueChanged();

    partial void OnLensPrescriptionChanged(LensPrescriptionSummary? value)
    {
        if (!ReferenceEquals(value?.CompatibleLenses, _lensChoicesSource))
        {
            _lensChoicesSource = value?.CompatibleLenses;
            LensChoices = ["Automatic", .. value?.CompatibleLenses ?? []];
            OnPropertyChanged(nameof(LensChoices));
            OnPropertyChanged(nameof(SelectedLensName));
        }
        OnPropertyChanged(nameof(HasLensChoices));
        OnPropertyChanged(nameof(HasLensDistortion));
        OnPropertyChanged(nameof(HasLensChromaticAberration));
        OnPropertyChanged(nameof(HasLensVignetting));
    }

    private IReadOnlyList<string>? _lensChoicesSource;

    private void OnLensValueChanged()
    {
        if (_isLoadingImage || !CanEditSelectedImage) return;
        UpdateCanReset();
        SchedulePreviewUpdate();
    }

    private void SaveLensTo(EditSettings target)
    {
        target.Lens.ProfileOverride = LensProfileOverride;
        target.Lens.Distortion = LensDistortion;
        target.Lens.ChromaticAberration = LensChromaticAberration;
        target.Lens.Vignetting = LensVignetting;
    }

    private void LoadLensFrom(EditSettings source)
    {
        LensProfileOverride = source.Lens.ProfileOverride;
        LensDistortion = source.Lens.Distortion;
        LensChromaticAberration = source.Lens.ChromaticAberration;
        LensVignetting = source.Lens.Vignetting;
    }

    private void ResetLensUi()
    {
        LensProfileOverride = null;
        LensDistortion = LensSettings.DefaultDistortion;
        LensChromaticAberration = LensSettings.DefaultChromaticAberration;
        LensVignetting = LensSettings.DefaultVignetting;
    }

    internal void ApplyLensPrescription(
        bool isRawSource,
        LensPrescriptionSummary? prescription)
    {
        LensPrescription = isRawSource ? prescription : null;
        OnPropertyChanged(nameof(IsOpticsEnabled));
    }
}
