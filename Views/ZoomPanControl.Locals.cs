using Avalonia;

namespace HappyPhoton.Views;

public partial class ZoomPanControl
{
    public static readonly StyledProperty<bool> IsSpotsModeProperty =
        AvaloniaProperty.Register<ZoomPanControl, bool>(nameof(IsSpotsMode));

    public bool IsSpotsMode
    {
        get => GetValue(IsSpotsModeProperty);
        set => SetValue(IsSpotsModeProperty, value);
    }

    public static readonly StyledProperty<bool> IsLocalsModeProperty =
        AvaloniaProperty.Register<ZoomPanControl, bool>(nameof(IsLocalsMode));
    public bool IsLocalsMode
    {
        get => GetValue(IsLocalsModeProperty);
        set => SetValue(IsLocalsModeProperty, value);
    }
}
