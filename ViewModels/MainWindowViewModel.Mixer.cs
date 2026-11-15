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

    /// <summary>Raised when the active band or any band's touched state may have changed.</summary>
    public event EventHandler? MixerBandsChanged;

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

    private void LoadActiveMixerBand()
    {
        var band = _liveMixer.GetBand(ActiveMixerBand);
        _isSwitchingMixerBand = true;
        MixerHue = band.Hue;
        MixerSaturation = band.Saturation;
        MixerLuminance = band.Luminance;
        _isSwitchingMixerBand = false;
    }

    public bool IsMixerBandTouched(ColorMixerBand band) =>
        _liveMixer.GetBand(band).HasActivePixels;

    private void NotifyMixerStateChanged() => MixerBandsChanged?.Invoke(this, EventArgs.Empty);
}
