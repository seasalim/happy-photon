using Avalonia;
using Avalonia.Input;
using HappyPhoton.Models;
using HappyPhoton.ViewModels;

namespace HappyPhoton.Views;

public sealed partial class LocalsOverlayControl
{
    private readonly OverlayPointerTracker _hoverTracker;

    private readonly record struct PointerTarget(bool Hue = false, bool Brush = false,
        LocalHandle? Handle = null, LocalAdjustment? Pin = null);

    private PointerTarget _target;

    private static readonly Cursor CrossCursor = new(StandardCursorType.Cross);

    private static readonly Cursor MoveCursor = new(StandardCursorType.SizeAll);

    private static readonly Cursor PinCursor = new(StandardCursorType.Hand);

    private PointerTarget ResolveTarget(Point point, LocalsFrame frame)
    {
        var vm = _owner!;
        if (vm.IsLocalHuePicking) return new(Hue: true);
        if (vm.IsBrushSectionVisible) return new(Brush: true, Pin: HitPin(point, frame));
        if (vm.IsLocalCreationArmed) return new(Handle: LocalHandle.Create);
        if (vm.SelectedLocal is { } selected && HitHandle(point, selected, frame) is { } handle)
            return new(Handle: handle);

        return new(Pin: HitPin(point, frame));
    }

    private Cursor CursorFor(PointerTarget target, Point point, LocalsFrame frame)
    {
        if (target.Hue) return CrossCursor;
        if (target.Brush) return HiddenBrushCursor;
        if (target.Handle is LocalHandle.Create or LocalHandle.Rotation or LocalHandle.Direction) return CrossCursor;
        if (target.Handle == LocalHandle.Center) return MoveCursor;
        if (target.Handle == null) return target.Pin != null ? PinCursor : Cursor.Default;

        var local = _owner!.SelectedLocal!;
        var direction = target.Handle == LocalHandle.FeatherRing ? point - ToCanvas(local, frame, Bounds.Size)
            : Direction(target.Handle is LocalHandle.AxisYPositive or LocalHandle.AxisYNegative
                ? local with { Angle = local.Angle + 90 } : local, frame, Bounds.Size);

        return ResizeCursor.ForAngle(Math.Atan2(direction.Y, direction.X));
    }

    private void UpdateFeedback()
    {
        if (_pointer != null) return;

        UpdateBrushHover(_hoverTracker.Position);
    }

    private void UpdatePointerCursor()
    {
        if (_pointer != null) return;

        if (_brushHover is not { } point || _owner is not { CanEditLocals: true, LocalsFrame: { } frame })
        {
            _target = default;
            Cursor = Cursor.Default;
            return;
        }

        _target = ResolveTarget(point, frame);
        Cursor = CursorFor(_target, point, frame);
    }
}
