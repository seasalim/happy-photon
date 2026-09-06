using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace HappyPhoton.Views;

public partial class LocalsEditSection : UserControl
{
    public LocalsEditSection()
    {
        InitializeComponent();
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

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key is Key.Left or Key.Right && e.Source is Avalonia.Visual source &&
            (ReferenceEquals(source, LocalList) || source.GetVisualAncestors().Contains(LocalList)))
        {
            LocalList.SelectedIndex = Math.Clamp(LocalList.SelectedIndex + (e.Key == Key.Left ? -1 : 1),
                0, Math.Max(0, LocalList.ItemCount - 1));
            e.Handled = true;
        }
        base.OnKeyDown(e);
    }
}
