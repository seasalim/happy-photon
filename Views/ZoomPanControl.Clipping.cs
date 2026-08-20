using Avalonia;
using Avalonia.Controls;
using HappyPhoton.Services;

namespace HappyPhoton.Views;

public partial class ZoomPanControl
{
    public static readonly StyledProperty<ClippingMask?> ClippingMaskProperty =
        AvaloniaProperty.Register<ZoomPanControl, ClippingMask?>(
            nameof(ClippingMask));

    public static readonly StyledProperty<ClippingOverlaySide>
        VisibleClippingSidesProperty =
        AvaloniaProperty.Register<ZoomPanControl, ClippingOverlaySide>(
            nameof(VisibleClippingSides));

    public static readonly StyledProperty<bool> IsClippingOverlayLatchedProperty =
        AvaloniaProperty.Register<ZoomPanControl, bool>(
            nameof(IsClippingOverlayLatched));

    private ClippingOverlayControl? _clippingOverlay;

    public ClippingMask? ClippingMask
    {
        get => GetValue(ClippingMaskProperty);
        set => SetValue(ClippingMaskProperty, value);
    }

    public ClippingOverlaySide VisibleClippingSides
    {
        get => GetValue(VisibleClippingSidesProperty);
        set => SetValue(VisibleClippingSidesProperty, value);
    }

    public bool IsClippingOverlayLatched
    {
        get => GetValue(IsClippingOverlayLatchedProperty);
        set => SetValue(IsClippingOverlayLatchedProperty, value);
    }

    private void InitializeClippingOverlay() =>
        _clippingOverlay =
            this.FindControl<ClippingOverlayControl>("ClippingOverlay");

    private static bool IsClippingProperty(AvaloniaProperty property) =>
        property == ClippingMaskProperty ||
        property == VisibleClippingSidesProperty ||
        property == IsClippingOverlayLatchedProperty;

    private void UpdateClippingOverlaySize()
    {
        if (_clippingOverlay == null || _imageControl == null || Source == null)
        {
            return;
        }

        _clippingOverlay.Width = _imageControl.Width;
        _clippingOverlay.Height = _imageControl.Height;
    }
}
