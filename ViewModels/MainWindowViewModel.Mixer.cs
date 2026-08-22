using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HappyPhoton.Models;

namespace HappyPhoton.ViewModels;

public partial class MainWindowViewModel
{
    private ColorMixerSettings _liveMixer = new();
    private bool _isSwitchingMixerBand;

    [ObservableProperty]
    private ColorMixerBand _activeMixerBand;

    [ObservableProperty]
    private int _mixerHue;

    [ObservableProperty]
    private int _mixerSaturation;

    [ObservableProperty]
    private int _mixerLuminance;

    public bool IsRedMixerActive => ActiveMixerBand == ColorMixerBand.Red;
    public bool IsOrangeMixerActive => ActiveMixerBand == ColorMixerBand.Orange;
    public bool IsYellowMixerActive => ActiveMixerBand == ColorMixerBand.Yellow;
    public bool IsGreenMixerActive => ActiveMixerBand == ColorMixerBand.Green;
    public bool IsAquaMixerActive => ActiveMixerBand == ColorMixerBand.Aqua;
    public bool IsBlueMixerActive => ActiveMixerBand == ColorMixerBand.Blue;
    public bool IsPurpleMixerActive => ActiveMixerBand == ColorMixerBand.Purple;
    public bool IsMagentaMixerActive => ActiveMixerBand == ColorMixerBand.Magenta;

    public bool IsRedMixerTouched => IsMixerBandTouched(ColorMixerBand.Red);
    public bool IsOrangeMixerTouched => IsMixerBandTouched(ColorMixerBand.Orange);
    public bool IsYellowMixerTouched => IsMixerBandTouched(ColorMixerBand.Yellow);
    public bool IsGreenMixerTouched => IsMixerBandTouched(ColorMixerBand.Green);
    public bool IsAquaMixerTouched => IsMixerBandTouched(ColorMixerBand.Aqua);
    public bool IsBlueMixerTouched => IsMixerBandTouched(ColorMixerBand.Blue);
    public bool IsPurpleMixerTouched => IsMixerBandTouched(ColorMixerBand.Purple);
    public bool IsMagentaMixerTouched => IsMixerBandTouched(ColorMixerBand.Magenta);

    partial void OnActiveMixerBandChanged(ColorMixerBand value)
    {
        LoadActiveMixerBand();
        NotifyMixerStateChanged();
    }

    partial void OnMixerHueChanged(int value) => OnMixerValueChanged();
    partial void OnMixerSaturationChanged(int value) => OnMixerValueChanged();
    partial void OnMixerLuminanceChanged(int value) => OnMixerValueChanged();

    [RelayCommand]
    private void SelectMixerBand(ColorMixerBand band) => ActiveMixerBand = band;

    private void OnMixerValueChanged()
    {
        if (_isSwitchingMixerBand)
        {
            return;
        }

        var band = _liveMixer.GetBand(ActiveMixerBand);
        band.Hue = MixerHue;
        band.Saturation = MixerSaturation;
        band.Luminance = MixerLuminance;
        NotifyMixerStateChanged();
        if (_isLoadingImage || !CanEditSelectedImage)
        {
            return;
        }

        UpdateCanReset();
        SchedulePreviewUpdate();
    }

    private void SaveMixerTo(EditSettings target)
    {
        var mixer = _liveMixer.Clone();
        target.Mixer = mixer.HasActivePixels ? mixer : null;
    }

    private void LoadMixerFrom(EditSettings source)
    {
        _liveMixer = source.Mixer?.Clone() ?? new ColorMixerSettings();
        LoadActiveMixerBand();
        NotifyMixerStateChanged();
    }

    private void ResetMixerUi()
    {
        _liveMixer = new ColorMixerSettings();
        LoadActiveMixerBand();
        NotifyMixerStateChanged();
    }

    private void LoadActiveMixerBand()
    {
        var band = _liveMixer.GetBand(ActiveMixerBand);
        _isSwitchingMixerBand = true;
        MixerHue = band.Hue;
        MixerSaturation = band.Saturation;
        MixerLuminance = band.Luminance;
        _isSwitchingMixerBand = false;
    }

    private bool IsMixerBandTouched(ColorMixerBand band) =>
        _liveMixer.GetBand(band).HasActivePixels;

    private void NotifyMixerStateChanged()
    {
        OnPropertyChanged(nameof(IsRedMixerActive));
        OnPropertyChanged(nameof(IsOrangeMixerActive));
        OnPropertyChanged(nameof(IsYellowMixerActive));
        OnPropertyChanged(nameof(IsGreenMixerActive));
        OnPropertyChanged(nameof(IsAquaMixerActive));
        OnPropertyChanged(nameof(IsBlueMixerActive));
        OnPropertyChanged(nameof(IsPurpleMixerActive));
        OnPropertyChanged(nameof(IsMagentaMixerActive));
        OnPropertyChanged(nameof(IsRedMixerTouched));
        OnPropertyChanged(nameof(IsOrangeMixerTouched));
        OnPropertyChanged(nameof(IsYellowMixerTouched));
        OnPropertyChanged(nameof(IsGreenMixerTouched));
        OnPropertyChanged(nameof(IsAquaMixerTouched));
        OnPropertyChanged(nameof(IsBlueMixerTouched));
        OnPropertyChanged(nameof(IsPurpleMixerTouched));
        OnPropertyChanged(nameof(IsMagentaMixerTouched));
    }
}
