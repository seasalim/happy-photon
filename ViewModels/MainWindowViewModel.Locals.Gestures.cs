using Avalonia;
using HappyPhoton.Models;

namespace HappyPhoton.ViewModels;

public enum LocalHandle { Create, Center, Direction, Feather }

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
        _localsGestureLocal = handle == LocalHandle.Create ? NewLinear() : SelectedLocal! with { };
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
            if (screenDistance < 8 || feather < .001)
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
        }
        else if (local != null)
        {
            var x = (point.X - start.Cu) * frame.Width / frame.LongEdge;
            var y = (point.Y - start.Cv) * frame.Height / frame.LongEdge;
            switch (_localsHandle)
            {
                case LocalHandle.Center:
                    local.Cu = start.Cu + point.X - _localsStart.X;
                    local.Cv = start.Cv + point.Y - _localsStart.Y;
                    break;
                case LocalHandle.Direction:
                    local.Angle = Math.Atan2(y, x) * 180 / Math.PI;
                    break;
                case LocalHandle.Feather:
                    var angle = start.Angle * Math.PI / 180;
                    local.Feather = 2 * Math.Abs(x * Math.Cos(angle) + y * Math.Sin(angle));
                    break;
            }
        }
        if (local == null) return;
        local.Cu = Math.Clamp(local.Cu, -1, 2);
        local.Cv = Math.Clamp(local.Cv, -1, 2);
        local.Angle = (local.Angle % 360 + 360) % 360;
        local.Feather = Math.Clamp(local.Feather, .001, 2);
        NotifyLocalsState();
        ScheduleCropPreviewUpdate();
    }

    public async Task CompleteLocalsGestureAsync()
    {
        if (_localsGestureBefore is not { } before) return;
        _previewDebounce?.Cancel();
        var label = _localsHandle == LocalHandle.Create ? "Add Linear" : "Local geometry";
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
