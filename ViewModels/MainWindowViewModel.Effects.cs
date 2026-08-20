using CommunityToolkit.Mvvm.ComponentModel;
using HappyPhoton.Models;

namespace HappyPhoton.ViewModels;

public partial class MainWindowViewModel
{
    public IReadOnlyList<GrainSize> GrainSizeOptions { get; } =
    [
        GrainSize.Fine,
        GrainSize.Medium,
        GrainSize.Coarse
    ];

    [ObservableProperty]
    private int _vignette;

    [ObservableProperty]
    private int _midpoint = 50;

    [ObservableProperty]
    private int _grain;

    [ObservableProperty]
    private GrainSize _grainSize = GrainSize.Medium;

    public bool IsVignetteActive => Vignette != 0;

    partial void OnVignetteChanged(int value)
    {
        OnPropertyChanged(nameof(IsVignetteActive));
        OnEffectsValueChanged(affectsPixels: true);
    }

    partial void OnMidpointChanged(int value) =>
        OnEffectsValueChanged(affectsPixels: Vignette != 0);

    partial void OnGrainChanged(int value) =>
        OnEffectsValueChanged(affectsPixels: true);

    partial void OnGrainSizeChanged(GrainSize value) =>
        OnEffectsValueChanged(affectsPixels: Grain != 0);

    private void OnEffectsValueChanged(bool affectsPixels)
    {
        if (_isLoadingImage || !CanEditSelectedImage || !affectsPixels)
        {
            return;
        }

        UpdateCanReset();
        SchedulePreviewUpdate();
    }

    private void SaveEffectsTo(EditSettings target)
    {
        var effects = new EffectsSettings
        {
            Vignette = Vignette,
            Midpoint = Midpoint,
            Grain = Grain,
            GrainSize = GrainSize
        };
        target.Effects = effects.HasActivePixels ? effects : null;
    }

    private void LoadEffectsFrom(EditSettings source)
    {
        var effects = source.Effects;
        Vignette = effects?.Vignette ?? 0;
        Midpoint = effects?.Midpoint ?? 50;
        Grain = effects?.Grain ?? 0;
        GrainSize = effects?.GrainSize ?? GrainSize.Medium;
    }

    private void ResetEffectsUi()
    {
        Vignette = 0;
        Midpoint = 50;
        Grain = 0;
        GrainSize = GrainSize.Medium;
    }
}
