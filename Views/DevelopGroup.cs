using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace HappyPhoton.Views;

public sealed class DevelopGroup : Expander
{
    public static readonly StyledProperty<ICommand?> SoloCommandProperty =
        AvaloniaProperty.Register<DevelopGroup, ICommand?>(nameof(SoloCommand));

    public ICommand? SoloCommand
    {
        get => GetValue(SoloCommandProperty);
        set => SetValue(SoloCommandProperty, value);
    }

    protected override Type StyleKeyOverride => typeof(Expander);

    public DevelopGroup()
    {
        Classes.Add("develop-group");
        Classes.Add("compact-chevron");
        IsExpanded = true;
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        var header = e.NameScope.Find<ToggleButton>("ExpanderHeader")!;
        header.AddHandler(PointerPressedEvent, OnHeaderPressed, RoutingStrategies.Tunnel);
    }

    internal static bool ToggleFocusedHeader(object? focused)
    {
        if (focused is not ToggleButton { Name: "ExpanderHeader", IsEffectivelyEnabled: true } header ||
            header.TemplatedParent is not DevelopGroup group)
        {
            return false;
        }

        group.SetCurrentValue(IsExpandedProperty, !group.IsExpanded);

        return true;
    }

    private void OnHeaderPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!IsEffectivelyEnabled || !e.KeyModifiers.HasFlag(KeyModifiers.Alt) ||
            !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed ||
            SoloCommand?.CanExecute(Tag) != true)
        {
            return;
        }

        e.Handled = true;
        var header = (ToggleButton)sender!;
        var scroll = this.GetVisualAncestors().OfType<ScrollViewer>()
            .FirstOrDefault(viewer => viewer.Name == "DevelopControlsScrollViewer");
        var before = header.TranslatePoint(default, scroll ?? (Visual)this)?.Y;
        SoloCommand.Execute(Tag);

        if (scroll is null || before is null) return;

        Dispatcher.UIThread.Post(() =>
        {
            scroll.UpdateLayout();
            if (header.TranslatePoint(default, scroll) is not { } after) return;

            var offset = scroll.Offset.Y + after.Y - before.Value;
            scroll.Offset = new Vector(scroll.Offset.X,
                Math.Clamp(offset, 0, Math.Max(0, scroll.Extent.Height - scroll.Viewport.Height)));
        }, DispatcherPriority.Render);
    }
}
