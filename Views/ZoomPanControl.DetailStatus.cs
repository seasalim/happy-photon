using Avalonia;

namespace HappyPhoton.Views;

public partial class ZoomPanControl
{
    public static readonly StyledProperty<string> OneToOneStatusProperty =
        AvaloniaProperty.Register<ZoomPanControl, string>(nameof(OneToOneStatus), "1:1");

    public static readonly DirectProperty<ZoomPanControl, bool> IsOneToOneStatusVisibleProperty =
        AvaloniaProperty.RegisterDirect<ZoomPanControl, bool>(nameof(IsOneToOneStatusVisible),
            control => control.IsOneToOneStatusVisible);

    public string OneToOneStatus
    {
        get => GetValue(OneToOneStatusProperty);
        set => SetValue(OneToOneStatusProperty, value);
    }

    private bool _isOneToOneStatusVisible;

    public bool IsOneToOneStatusVisible => _isOneToOneStatusVisible;

    private void NotifyDetailStatus() => SetAndRaise(IsOneToOneStatusVisibleProperty,
        ref _isOneToOneStatusVisible, IsLoupePeekActive || (!AutoFit && Math.Abs(ZoomLevel - 1) < .0001));
}
