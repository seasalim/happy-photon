using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;

namespace HappyPhoton.Views;

public partial class CompareView : UserControl
{
    private readonly HashSet<ZoomPanControl> _hookedControls = [];
    private SynchronizedPaneGroup? _paneGroup;

    public CompareView()
    {
        InitializeComponent();
        AddHandler(
            PointerPressedEvent,
            OnPanePointerPressed,
            RoutingStrategies.Bubble,
            handledEventsToo: true);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        _paneGroup?.Dispose();
        _paneGroup = null;
        if (DataContext is MainWindowViewModel vm)
        {
            _paneGroup = new SynchronizedPaneGroup(vm.SynchronizedView)
            {
                RequiredDeviceLongEdgeChanged = PublishRequiredDeviceLongEdge
            };
            foreach (var control in _hookedControls) _paneGroup.Attach(control);
        }
    }

    private void OnPaneAttached(
        object? sender,
        VisualTreeAttachmentEventArgs e)
    {
        if (sender is not ZoomPanControl control ||
            !_hookedControls.Add(control)) return;
        _paneGroup?.Attach(control);
    }

    private void OnPaneDetached(
        object? sender,
        VisualTreeAttachmentEventArgs e)
    {
        if (sender is not ZoomPanControl control ||
            !_hookedControls.Remove(control))
            return;
        _paneGroup?.Detach(control);
    }

    private void OnPanePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var paneBorder = (e.Source as Visual)?.GetVisualAncestors()
            .Prepend(e.Source as Visual)
            .OfType<Border>()
            .FirstOrDefault(border => border.Classes.Contains("compare-pane"));
        if (paneBorder?.DataContext is ComparePaneViewModel pane &&
            DataContext is MainWindowViewModel vm)
        {
            vm.ActivateComparePaneCommand.Execute(pane);
        }
    }

    private void PublishRequiredDeviceLongEdge(
        ZoomPanControl control,
        int longEdge)
    {
        if (control.DataContext is ComparePaneViewModel pane &&
            DataContext is MainWindowViewModel vm)
        {
            vm.PublishCompareRequiredDeviceLongEdge(
                pane,
                longEdge,
                control.IsLoupePeekActive);
        }
    }

    public bool CancelLoupePeek() => _paneGroup?.CancelLoupePeek() == true;
}
