using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Input;
using HappyPhoton.ViewModels;

namespace HappyPhoton.Views;

public partial class DevelopViewerPane : UserControl
{
    private MainWindowViewModel? _viewModel;
    private SynchronizedPaneGroup? _paneGroup;

    private double _fullBarWidth;

    private double _noSliderBarWidth;

    public DevelopViewerPane()
    {
        InitializeComponent();
        DevelopBarLayout.PrepareLayout = UpdateControlBarTier;
        DevelopImageAssessment.AddHandler(Button.ClickEvent, (_, _) => Focus());
        DevelopViewActionsButton.Flyout!.Opened += (_, _) => ConfigureViewMenuKeys();
        DevelopViewActionsButton.Flyout.Closed += (_, _) =>
        {
            if (IsEffectivelyVisible) Focus();
            else (TopLevel.GetTopLevel(this) as MainWindow)?
                .FindControl<ZoomPanControl>("FullScreenZoomPanControl")?.Focus();
        };
    }

    public ZoomPanControl Viewer => ZoomPanControl;

    private void ConfigureViewMenuKeys()
    {
        var menu = (MenuFlyout)DevelopViewActionsButton.Flyout!;
        var presenter = menu.Items.Cast<MenuItem>().First().GetVisualAncestors()
            .OfType<MenuFlyoutPresenter>().Single();
        if (presenter.KeyBindings.Count != 0) return;

        // Local bindings run before the window's bindings; route through native menu handling.
        foreach (var key in new[] { Key.Enter, Key.Escape })
        {
            presenter.KeyBindings.Add(new KeyBinding
            {
                Gesture = new KeyGesture(key),
                Command = new RelayCommand(() =>
                {
                    var target = TopLevel.GetTopLevel(presenter)?.FocusManager?.GetFocusedElement()
                        as InputElement ?? presenter;
                    target.RaiseEvent(new KeyEventArgs { RoutedEvent = KeyDownEvent, Key = key });
                })
            });
        }
    }

    private void UpdateControlBarTier(double width)
    {
        if (_fullBarWidth == 0)
        {
            // Read once with every original control visible, even on a narrow first layout.
            DevelopImageActionsPanel.Measure(Size.Infinity);
            DevelopViewStatePanel.Measure(Size.Infinity);
            _noSliderBarWidth = DevelopImageActionsPanel.DesiredSize.Width +
                DevelopViewStatePanel.DesiredSize.Width - DevelopZoomSlider.DesiredSize.Width -
                DevelopViewStatePanel.Spacing;
            _fullBarWidth = ControlBarLayout.FullClusterWidth + 2 * Math.Max(
                DevelopImageActionsPanel.DesiredSize.Width, DevelopViewStatePanel.DesiredSize.Width);
        }

        var overflow = width < _noSliderBarWidth;

        foreach (var control in DevelopViewStatePanel.Children)
        {
            control.IsVisible = control == DevelopViewActionsButton ? overflow
                : control == DevelopZoomSlider ? width >= _fullBarWidth : !overflow;
        }
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_viewModel != null) _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _viewModel = DataContext as MainWindowViewModel;
        if (_viewModel != null) _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        SetSplit(_viewModel?.IsBeforeAfterSplit == true);
    }
    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.IsBeforeAfterSplit))
            SetSplit(_viewModel?.IsBeforeAfterSplit == true);
        else if (e.PropertyName == nameof(MainWindowViewModel.BeforeAfterPreviewImage))
            Avalonia.Threading.Dispatcher.UIThread.Post(
                () => _paneGroup?.RefreshFromLeader());
    }
    private void SetSplit(bool active)
    {
        _paneGroup?.Dispose();
        _paneGroup = null;
        ViewerGrid.ColumnDefinitions[0].Width = active
            ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        if (!active || _viewModel is not { } vm) return;
        vm.BeforeAfterSynchronizedView.SetViewport(
            ZoomPanControl.CaptureNormalizedViewport());
        _paneGroup = new SynchronizedPaneGroup(vm.BeforeAfterSynchronizedView)
        {
            Leader = ZoomPanControl,
            ZoomRequested = (control, delta) =>
            {
                if (ReferenceEquals(control, BeforeZoomPanControl))
                    vm.AdjustZoom(delta);
            },
            RequiredDeviceLongEdgeChanged = (control, edge) =>
            {
                if (ReferenceEquals(control, BeforeZoomPanControl))
                    vm.PublishBeforeAfterRequiredDeviceLongEdge(edge);
            }
        };
        _paneGroup.Attach(BeforeZoomPanControl);
        _paneGroup.Attach(ZoomPanControl);
    }
}
