using Avalonia;
using HappyPhoton.Models;

namespace HappyPhoton.ViewModels;

public enum LocalHandle { Create, Center, Direction, Feather, AxisXPositive, AxisXNegative,
    AxisYPositive, AxisYNegative, Rotation, FeatherRing }

public partial class MainWindowViewModel
{
    private EditSettings? _localsGestureBefore;
    private ImageFile? _localsGestureImage;
    private LocalAdjustment? _localsGestureLocal;
    private LocalHandle _localsHandle;
    private Point _localsStart;
    private LocalsFrame _localsGestureFrame;
    public bool IsLocalsGestureActive => _localsGestureBefore != null;

    public bool BeginLocalsGesture(LocalHandle handle, Point normalizedPoint)
    {
        if (!double.IsFinite(normalizedPoint.X) || !double.IsFinite(normalizedPoint.Y)) return false;
        if (!CanEditLocals || LocalsFrame is not { } frame ||
            handle == LocalHandle.Create && (!IsLocalCreationArmed || !CanAddLocal) ||
            handle != LocalHandle.Create && SelectedLocal == null) return false;
        _previewDebounce?.Cancel();
        _localsGestureBefore = CaptureLiveEditState();
        _localsGestureImage = SelectedImage;
        _localsGestureLocal = handle == LocalHandle.Create ? NewLocal() : SelectedLocal! with { };
        _localsGestureFrame = frame;
        _localsHandle = handle;
        _localsStart = normalizedPoint;
        UndoCommand.NotifyCanExecuteChanged();
        return true;
    }

    public void MoveLocalsGesture(Point point, double screenDistance)
    {
        if (!double.IsFinite(point.X) || !double.IsFinite(point.Y) || !double.IsFinite(screenDistance)) return;
        if (!IsLocalsGestureActive || !ReferenceEquals(SelectedImage, _localsGestureImage) ||
            _localsGestureLocal is not { } start) return;
        var frame = _localsGestureFrame;
        var dx = (point.X - _localsStart.X) * frame.Width / frame.LongEdge;
        var dy = (point.Y - _localsStart.Y) * frame.Height / frame.LongEdge;
        var local = Locals.FirstOrDefault(item => item.Id == start.Id);
        if (_localsHandle == LocalHandle.Create)
        {
            var feather = Math.Sqrt(dx * dx + dy * dy);
            if (screenDistance < 8 || feather < .001 || start.IsRadial && (Math.Abs(dx) < .001 || Math.Abs(dy) < .001))
            {
                SelectedImage!.EditSettings.Locals?.RemoveAll(item => item.Id == start.Id);
                NotifyLocalsState();
                return;
            }
            if (local == null)
            {
                local = start with { };
                (SelectedImage!.EditSettings.Locals ??= []).Add(local);
                _selectedLocalId = local.Id;
            }
            local.Cu = (_localsStart.X + point.X) / 2;
            local.Cv = (_localsStart.Y + point.Y) / 2;
            local.Angle = Math.Atan2(dy, dx) * 180 / Math.PI;
            local.Feather = feather;
            if (local.IsRadial)
            {
                local.Cu = _localsStart.X;
                local.Cv = _localsStart.Y;
                local.Rx = Math.Abs(dx);
                local.Ry = Math.Abs(dy);
                local.Angle = 0;
                local.Feather = .5;
            }
        }
        else if (local != null)
        {
            var x = (point.X - start.Cu) * frame.Width / frame.LongEdge;
            var y = (point.Y - start.Cv) * frame.Height / frame.LongEdge;
            var angle = start.Angle * Math.PI / 180;
            var along = x * Math.Cos(angle) + y * Math.Sin(angle);
            var across = -x * Math.Sin(angle) + y * Math.Cos(angle);
            switch (_localsHandle)
            {
                case LocalHandle.Center:
                    local.Cu = start.Cu + point.X - _localsStart.X;
                    local.Cv = start.Cv + point.Y - _localsStart.Y;
                    break;
                case LocalHandle.Direction:
                case LocalHandle.Rotation:
                    local.Angle = Math.Atan2(y, x) * 180 / Math.PI;
                    break;
                case LocalHandle.Feather:
                    local.Feather = 2 * Math.Abs(along);
                    break;
                case LocalHandle.AxisXPositive:
                case LocalHandle.AxisXNegative: local.Rx = Math.Abs(along); break;
                case LocalHandle.AxisYPositive:
                case LocalHandle.AxisYNegative: local.Ry = Math.Abs(across); break;
                case LocalHandle.FeatherRing:
                    local.Feather = 1 - Math.Sqrt(Math.Pow(along / start.Rx, 2) + Math.Pow(across / start.Ry, 2));
                    break;
            }
        }
        if (local == null) return;
        local.Cu = Math.Clamp(local.Cu, -1, 2);
        local.Cv = Math.Clamp(local.Cv, -1, 2);
        local.Angle = (local.Angle % 360 + 360) % 360;
        local.Feather = Math.Clamp(local.Feather, local.IsRadial ? 0 : .001, local.IsRadial ? 1 : 2);
        local.Rx = Math.Clamp(local.Rx, .001, 1);
        local.Ry = Math.Clamp(local.Ry, .001, 1);
        NotifyLocalsState();
        ScheduleCropPreviewUpdate();
    }

    public async Task CompleteLocalsGestureAsync()
    {
        if (_localsGestureBefore is not { } before) return;
        _previewDebounce?.Cancel();
        var label = _localsHandle == LocalHandle.Create ?
            (_localsGestureLocal!.IsRadial ? "Add Radial" : "Add Linear") : "Local geometry";
        _localsGestureBefore = null;
        _localsGestureImage = null;
        IsLocalCreationArmed = false;
        await CommitLocalAsync(before, label);
        RebindLocalSelection();
    }

    public bool DiscardLocalsGesture()
    {
        var before = _localsGestureBefore;
        var image = _localsGestureImage;
        _localsGestureBefore = null;
        _localsGestureImage = null;
        IsLocalCreationArmed = false;
        if (before == null || image == null) return false;
        _previewDebounce?.Cancel();
        image.EditSettings = before.Clone();
        image.HasEdits = before.HasEdits;
        if (ReferenceEquals(image, SelectedImage))
        {
            RebindLocalSelection();
            ScheduleCropPreviewUpdate();
        }
        return true;
    }

    public bool EscapeLocals()
    {
        if (!IsLocalsMode) return false;
        if (IsLocalsGestureActive || IsLocalCreationArmed) DiscardLocalsGesture();
        else CloseLocals();
        return true;
    }
}
