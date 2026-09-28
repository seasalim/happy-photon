using CommunityToolkit.Mvvm.Input;
using HappyPhoton.Models;
using HappyPhoton.Services;

namespace HappyPhoton.ViewModels;

public partial class MainWindowViewModel
{
    public bool IsSpotHeal => (SelectedSpot?.Type ?? _brushPreferences.SpotMode) == "heal";

    public bool IsSpotClone => !IsSpotHeal;

    public int SpotModeIndex
    {
        get => IsSpotHeal ? 0 : 1;
        set
        {
            if (value != SpotModeIndex) SetSpotModeCommand.Execute(value == 0 ? "heal" : "clone");
        }
    }

    public double SpotSize
    {
        get => SelectedSpot?.Radius * 100 ?? _brushPreferences.SpotSize;
        set => SetSpotValue(value, 0);
    }

    public double SpotFeather
    {
        get => SelectedSpot?.Feather * 100 ?? _brushPreferences.SpotFeather;
        set => SetSpotValue(value, 1);
    }

    public double SpotOpacity
    {
        get => SelectedSpot?.Opacity * 100 ?? _brushPreferences.SpotOpacity;
        set => SetSpotValue(value, 2);
    }

    [RelayCommand]
    private Task SetSpotModeAsync(string mode)
    {
        if (mode is not ("heal" or "clone") || !CanEditSpots) return Task.CompletedTask;
        if (SelectedSpot != null)
            return ChangeSpotsAsync(EditHistoryLabel.SpotMode, () => SelectedSpot!.Type = mode);
        _brushPreferences.SpotMode = mode;
        BrushPreferenceChanged();
        NotifySpotsState();
        return Task.CompletedTask;
    }

    private void SetSpotValue(double value, int field)
    {
        if (!CanEditSpots || (IsSpotsGestureActive && _spotsSliderLabel == null) || !double.IsFinite(value)) return;
        value = Math.Clamp(value, field == 0 ? .2 : field == 1 ? 0 : 5, field == 0 ? 10 : 100);
        if (SelectedSpot is { } spot)
        {
            if (field == 0) spot.Radius = LimitSpotRadius(value / 100, spot.Id);
            else if (field == 1) spot.Feather = value / 100;
            else spot.Opacity = value / 100;
            ClampSpot(spot);
            SchedulePreviewUpdate(field == 0 ? EditHistoryLabel.SpotSize :
                field == 1 ? EditHistoryLabel.SpotFeather : EditHistoryLabel.SpotOpacity);
            UpdateCanReset();
        }
        else
        {
            if (field == 0) _brushPreferences.SpotSize = value;
            else if (field == 1) _brushPreferences.SpotFeather = value;
            else _brushPreferences.SpotOpacity = value;
            BrushPreferenceChanged();
        }
        NotifySpotsState();
    }

    private void RestoreSpotPreferences(AppSettings settings)
    {
        _brushPreferences.SpotMode = settings.SpotMode == "clone" ? "clone" : "heal";
        _brushPreferences.SpotSize = FinitePreference(settings.SpotSize, .2, 10, 3);
        _brushPreferences.SpotFeather = FinitePreference(settings.SpotFeather, 0, 100, 50);
        _brushPreferences.SpotOpacity = FinitePreference(settings.SpotOpacity, 5, 100, 100);
        NotifySpotsState();
    }

    private void CaptureSpotPreferences(AppSettings settings)
    {
        settings.SpotMode = _brushPreferences.SpotMode;
        settings.SpotSize = _brushPreferences.SpotSize;
        settings.SpotFeather = _brushPreferences.SpotFeather;
        settings.SpotOpacity = _brushPreferences.SpotOpacity;
    }
}
