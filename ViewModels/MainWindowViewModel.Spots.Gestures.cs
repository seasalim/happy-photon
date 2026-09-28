using Avalonia;
using HappyPhoton.Models;
using HappyPhoton.Services;

namespace HappyPhoton.ViewModels;

public enum SpotHandle { Create, Destination, Source, Edge }

public partial class MainWindowViewModel
{
    private EditSettings? _spotsGestureBefore;

    private ImageFile? _spotsGestureImage;

    private Repair? _spotsGestureStart;

    private Point _spotsPress;

    private SpotHandle _spotsHandle;

    private bool _spotsManualSource;

    private bool _spotsResizeMoved;

    private string? _spotsSliderLabel;

    public bool IsSpotsGestureActive => _spotsGestureBefore != null;

    public bool BeginSpotSliderEdit(string label)
    {
        if (SelectedSpot is not { } spot || !BeginSpotsGesture(SpotHandle.Destination, new(spot.U, spot.V)))
            return false;

        _spotsSliderLabel = label;
        return true;
    }

    public Task CompleteSpotSliderEditAsync() => _spotsSliderLabel == null
        ? Task.CompletedTask : CompleteSpotsGestureAsync();

    public bool BeginSpotsGesture(SpotHandle handle, Point point)
    {
        if (!CanEditSpots || IsSpotsGestureActive || !FiniteSpotPoint(point)) return false;
        if (handle != SpotHandle.Create && SelectedSpot == null) return false;
        var radius = LimitSpotRadius(_brushPreferences.SpotSize / 100);
        if (handle == SpotHandle.Create && (Spots.Count >= Repair.MaximumCount || radius < Repair.MinimumRadius))
        {
            ShowTransientStatus(Spots.Count >= Repair.MaximumCount ? "64 of 64 spots" : "Spot area limit reached");
            return false;
        }
        _previewDebounce?.Cancel();
        _spotsGestureBefore = CaptureLiveEditState();
        _spotsGestureImage = SelectedImage;
        _spotsHandle = handle;
        _spotsPress = point;
        _spotsManualSource = false;
        _spotsResizeMoved = false;
        if (handle == SpotHandle.Create)
        {
            var spot = new Repair { U = point.X, V = point.Y, Su = point.X, Sv = point.Y, Radius = radius,
                Type = _brushPreferences.SpotMode, Feather = _brushPreferences.SpotFeather / 100,
                Opacity = _brushPreferences.SpotOpacity / 100 };
            ClampSpot(spot);
            (SelectedImage!.EditSettings.Repairs ??= []).Add(spot);
            _selectedSpotId = spot.Id;
        }
        _spotsGestureStart = SelectedSpot! with { };
        if (handle == SpotHandle.Source && SpotDisplayMap is { } map)
            (_spotsGestureStart.Su, _spotsGestureStart.Sv) =
                RepairGeometry.ClampSource(_spotsGestureStart, map.BaseWidth, map.BaseHeight);
        NotifySpotsState();
        return true;
    }

    public void MoveSpotsGesture(Point point, double screenDistance)
    {
        if (!FiniteSpotPoint(point) || !double.IsFinite(screenDistance) || !IsSpotsGestureActive ||
            !ReferenceEquals(SelectedImage, _spotsGestureImage) || SelectedSpot is not { } spot ||
            _spotsGestureStart is not { } start || SpotDisplayMap is not { } map) return;
        var delta = point - _spotsPress;
        switch (_spotsHandle)
        {
            case SpotHandle.Create:
                if (screenDistance < 4 && !_spotsManualSource) return;
                _spotsManualSource = true;
                spot.Su = point.X;
                spot.Sv = point.Y;
                break;
            case SpotHandle.Destination:
                spot.U = start.U + delta.X;
                spot.V = start.V + delta.Y;
                break;
            case SpotHandle.Source:
                spot.Su = start.Su + delta.X;
                spot.Sv = start.Sv + delta.Y;
                break;
            case SpotHandle.Edge:
                if (screenDistance < 4 && !_spotsResizeMoved) return;
                var pressDx = (_spotsPress.X - start.U) * map.BaseWidth;
                var pressDy = (_spotsPress.Y - start.V) * map.BaseHeight;
                var pressDistance = Math.Sqrt(pressDx * pressDx + pressDy * pressDy);
                if (pressDistance <= 0) return;
                _spotsResizeMoved = true;
                var dx = (point.X - start.U) * map.BaseWidth;
                var dy = (point.Y - start.V) * map.BaseHeight;
                spot.Radius = LimitSpotRadius(Math.Clamp(start.Radius * Math.Sqrt(dx * dx + dy * dy) /
                    pressDistance, Repair.MinimumRadius, Repair.MaximumRadius), spot.Id);
                break;
        }
        ClampSpot(spot);
        NotifySpotsState();
        ScheduleCropPreviewUpdate();
    }

    public async Task CompleteSpotsGestureAsync()
    {
        if (_spotsGestureBefore is not { } before || SelectedSpot is not { } spot) return;
        if (_spotsHandle == SpotHandle.Create && !_spotsManualSource)
        {
            using var lease = ImageService.Previews.AcquireLocalRangeBase(SelectedImage!, before,
                BaseImage.InteractivePreviewMaxDimension);
            var ranked = lease == null ? [] : AutomaticRepairSource.Rank(lease.Base, spot);
            if (ranked.Count == 0)
            {
                DiscardSpotsGesture();
                ShowTransientStatus("No source available for this spot");
                return;
            }
            spot.Su = ranked[0].U;
            spot.Sv = ranked[0].V;
            ClampSpot(spot);
        }
        var label = _spotsSliderLabel ?? (_spotsHandle switch
        {
            SpotHandle.Create => spot.Type == "heal" ? EditHistoryLabel.AddHealSpot : EditHistoryLabel.AddCloneSpot,
            SpotHandle.Source => EditHistoryLabel.MoveSpotSource,
            SpotHandle.Edge => EditHistoryLabel.ResizeSpot,
            _ => EditHistoryLabel.MoveSpot
        });
        _previewDebounce?.Cancel();
        _spotsGestureBefore = null;
        _spotsGestureImage = null;
        _spotsSliderLabel = null;
        NotifySpotsState();
        await CommitLocalAsync(before, label, "Unable to save spot");
    }

    public bool DiscardSpotsGesture()
    {
        var before = _spotsGestureBefore;
        var image = _spotsGestureImage;
        _spotsGestureBefore = null;
        _spotsGestureImage = null;
        _spotsSliderLabel = null;
        if (before == null || image == null) return false;
        _previewDebounce?.Cancel();
        image.EditSettings = before;
        image.HasEdits = before.HasEdits;
        NotifySpotsState();
        if (ReferenceEquals(image, SelectedImage)) ScheduleCropPreviewUpdate();
        return true;
    }

    private static bool FiniteSpotPoint(Point point) => double.IsFinite(point.X) && double.IsFinite(point.Y);
}
