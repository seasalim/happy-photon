using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace HappyPhoton.Views;

public partial class CropEditSection : UserControl
{
    public CropEditSection()
    {
        InitializeComponent();
        // Horizon belongs to the crop draft, not the panel's slider transaction.
        AddHandler(CompactSlider.DragStartedEvent, (_, e) => e.Handled = true);
        AddHandler(CompactSlider.DragCompletedEvent, (_, e) => e.Handled = true);
        PropertyChanged += (_, e) =>
        {
            if (e.Property == IsVisibleProperty && IsVisible)
                Dispatcher.UIThread.Post(() =>
                {
                    if (IsVisible && this.FindAncestorOfType<ScrollViewer>() is { } scroll)
                        scroll.Offset = default;
                }, DispatcherPriority.Background);
        };
    }
}
