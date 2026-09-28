using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HappyPhoton.Models;
using HappyPhoton.Services;

namespace HappyPhoton.ViewModels;

public partial class MainWindowViewModel
{
    [ObservableProperty] private bool _isSpotsMode;

    private string? _selectedSpotId;

    public IReadOnlyList<Repair> Spots => SelectedImage?.EditSettings.Repairs ?? [];

    public bool HasSpots => Spots.Count > 0;

    public RepairDisplayMap? SpotDisplayMap => PreviewImage == null ? null : ImageService.Previews.GetRepairDisplayMap(PreviewImage);

    public double NewSpotRadius => _brushPreferences.SpotSize / 100;

    public bool CanEditSpots => !_renderOutcomeChannelClosed && IsSpotsMode && CanEditSelectedImage && IsDevelopMode &&
        !IsFullScreenMode && !IsBeforeAfterSplit && !_isBeforeAfterSplitTransitioning &&
        _requestedPreviewIntent != PreviewSurfaceIntent.Original && !_isHoveringPreset &&
        _hoveredHistoryEntry == null && !IsWhiteBalancePicking && SpotDisplayMap != null;

    public Repair? SelectedSpot
    {
        get => Spots.FirstOrDefault(spot => spot.Id == _selectedSpotId);
        set
        {
            if (value?.Id == _selectedSpotId) return;
            DiscardSpotsGesture();
            _selectedSpotId = value?.Id;
            NotifySpotsState();
        }
    }

    public string SpotCount => $"{Spots.Count} of {Repair.MaximumCount} spots" +
        (RepairArea.Sum(Spots) / Repair.MaximumArea is > .75 and var fraction ? $" · area {fraction:P0} used" : "");

    [RelayCommand]
    private async Task ToggleSpotsModeAsync()
    {
        if (IsSpotsMode) { CloseSpots(); return; }
        if (!IsDevelopMode || !CanEditSelectedImage || IsFullScreenMode) return;
        if (IsCropMode) await CancelCropCoreAsync();
        if (IsCropMode) return;
        CloseLocals();
        IsWhiteBalancePicking = false;
        IsSpotsMode = true;
    }

    [RelayCommand]
    private void CloseSpots()
    {
        DiscardSpotsGesture();
        _selectedSpotId = null;
        IsSpotsMode = false;
    }

    [RelayCommand]
    private Task DeleteSpotAsync() => ChangeSpotsAsync(EditHistoryLabel.DeleteSpot, () =>
    {
        if (SelectedSpot is { } spot)
        {
            SelectedImage!.EditSettings.Repairs!.Remove(spot);
        }
        _selectedSpotId = null;
    });

    [RelayCommand]
    private Task ClearSpotsAsync() => ChangeSpotsAsync(EditHistoryLabel.ClearSpots, () =>
    {
        SelectedImage!.EditSettings.Repairs = null;
        _selectedSpotId = null;
    });

    private async Task ChangeSpotsAsync(string label, Action change)
    {
        DiscardSpotsGesture();
        if (!CanEditSpots) return;
        var before = CaptureLiveEditState();
        change();
        NotifySpotsState();
        await CommitLocalAsync(before, label, "Unable to save spot");
    }

    public bool EscapeSpots()
    {
        if (!IsSpotsMode) return false;
        if (IsSpotsGestureActive) DiscardSpotsGesture();
        else if (SelectedSpot != null) SelectedSpot = null;
        else CloseSpots();
        return true;
    }

    private double LimitSpotRadius(double radius, string? except = null) => Math.Min(radius,
        Math.Sqrt(Math.Max(0, Repair.MaximumArea - RepairArea.Sum(Spots.Where(spot => spot.Id != except))) / Math.PI));

    private void ClampSpot(Repair spot)
    {
        spot.U = QuantizeSpot(spot.U);
        spot.V = QuantizeSpot(spot.V);
        spot.Su = QuantizeSpot(spot.Su);
        spot.Sv = QuantizeSpot(spot.Sv);
        if (SpotDisplayMap is { } map)
            (spot.Su, spot.Sv) = RepairGeometry.ClampSource(spot, map.BaseWidth, map.BaseHeight);
    }

    private static double QuantizeSpot(double value) =>
        Math.Round(Math.Clamp(value, 0, 1) * Repair.CoordinateScale) / Repair.CoordinateScale;

    private void NotifySpotsState()
    {
        foreach (var name in new[] { nameof(Spots), nameof(HasSpots), nameof(SelectedSpot), nameof(SpotCount),
            nameof(CanEditSpots), nameof(SpotDisplayMap), nameof(SpotSize), nameof(SpotFeather), nameof(SpotOpacity),
            nameof(IsSpotHeal), nameof(IsSpotClone), nameof(SpotModeIndex), nameof(IsSpotsGestureActive) }) OnPropertyChanged(name);
        UndoCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsSpotsModeChanged(bool value) => NotifySpotsState();
}
