using Avalonia;

namespace HappyPhoton.Views;

public partial class ZoomPanControl
{
    public static readonly StyledProperty<bool> IsLocalsModeProperty =
        AvaloniaProperty.Register<ZoomPanControl, bool>(nameof(IsLocalsMode));
    public bool IsLocalsMode
    {
        get => GetValue(IsLocalsModeProperty);
        set => SetValue(IsLocalsModeProperty, value);
    }
}
