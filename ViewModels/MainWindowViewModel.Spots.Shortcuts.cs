using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HappyPhoton.Services;

namespace HappyPhoton.ViewModels;

public partial class MainWindowViewModel
{
    [ObservableProperty] private bool _visualizeSpots;

    [ObservableProperty] private bool _hideSpotCircles;

    public bool CanUseSpotShortcuts => CanEditSpots && !IsLocalHuePicking && !IsSpotsGestureActive;

    [RelayCommand]
    private Task NextSpotSourceAsync()
    {
        if (!CanUseSpotShortcuts || SelectedSpot is not { } spot) return Task.CompletedTask;

        using var lease = ImageService.Previews.AcquireLocalRangeBase(SelectedImage!,
            SelectedImage!.EditSettings, BaseImage.InteractivePreviewMaxDimension);
        if (lease == null) return Task.CompletedTask;

        var ranked = AutomaticRepairSource.Rank(lease.Base, spot)
            .Select(candidate => PersistedSource(candidate.U, candidate.V)).Distinct().ToList();
        if (ranked.Count == 0) return Task.CompletedTask;

        var next = ranked[(ranked.IndexOf(PersistedSource(spot.Su, spot.Sv)) + 1) % ranked.Count];

        return ChangeSpotsAsync(EditHistoryLabel.NewSpotSource, () => (spot.Su, spot.Sv) = next);

        (double Su, double Sv) PersistedSource(double u, double v)
        {
            var trial = spot with { Su = u, Sv = v };
            ClampSpot(trial);

            // Edge clamping can leave the coordinate grid; use the value that round-trips.
            return (QuantizeSpot(trial.Su), QuantizeSpot(trial.Sv));
        }
    }

    [RelayCommand]
    private async Task StepSpotPreferenceAsync(string step)
    {
        if (!CanUseSpotShortcuts) return;

        var feather = step.StartsWith("feather");
        var increase = step.EndsWith('+');
        var selected = SelectedSpot != null;
        if (selected && !BeginSpotSliderEdit(feather ? EditHistoryLabel.SpotFeather : EditHistoryLabel.SpotSize)) return;

        if (feather) SpotFeather += increase ? 10 : -10;
        else SpotSize *= increase ? 1.25 : .8;
        if (selected) await CompleteSpotSliderEditAsync();
    }
}
