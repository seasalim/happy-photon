using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using HappyPhoton.ViewModels;

namespace HappyPhoton.Views;

public partial class DevelopViewerPane : UserControl
{
    private MainWindowViewModel? _viewModel;
    private SynchronizedPaneGroup? _paneGroup;
    public DevelopViewerPane()
    {
        InitializeComponent();
    }

    public ZoomPanControl Viewer => ZoomPanControl;
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
