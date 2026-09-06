using Avalonia;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HappyPhoton.Models;

namespace HappyPhoton.ViewModels;

public partial class MainWindowViewModel
{
    [ObservableProperty] private bool _isLocalGeometryExpanded;
    public bool CanEditLocalGeometry => CanEditLocals && HasSelectedLocal;

    public double LocalX
    {
        get => (SelectedLocal?.Cu ?? .5) * 100;
        set => SetLocalGeometry(value, LocalHandle.Center, horizontal: true);
    }
    public double LocalY
    {
        get => (SelectedLocal?.Cv ?? .5) * 100;
        set => SetLocalGeometry(value, LocalHandle.Center);
    }
    public double LocalAngle
    {
        get => SelectedLocal?.Angle ?? 90;
        set => SetLocalGeometry(value, LocalHandle.Direction);
    }
    public double LocalWidth
    {
        get => (SelectedLocal?.Feather ?? .25) * 100;
        set => SetLocalGeometry(value, LocalHandle.Feather);
    }

    private void SetLocalGeometry(double value, LocalHandle handle, bool horizontal = false)
    {
        if (!CanEditLocalGeometry || SelectedLocal is not { } local || !double.IsFinite(value)) return;
        var before = local with { };
        switch (handle)
        {
            case LocalHandle.Center:
                value = Math.Clamp(value, -100, 200) / 100;
                if (horizontal) local.Cu = value;
                else local.Cv = value;
                break;
            case LocalHandle.Direction: local.Angle = (value % 360 + 360) % 360; break;
            case LocalHandle.Feather: local.Feather = Math.Clamp(value, .1, 200) / 100; break;
        }
        if (before == local) return;
        NotifyLocalsState();
        UpdateCanReset();
        SchedulePreviewUpdate("Local geometry");
    }

    [RelayCommand(CanExecute = nameof(CanEditLocalGeometry))]
    private Task CenterLocalInViewAsync() => ChangeLocalAsync("Center in view", () =>
    {
        if (SelectedLocal is not { } local || LocalsFrame is not { } frame) return;
        var center = NavigatorVisibleRegion?.Center ?? new Point(.5, .5);
        local.Cu = Math.Clamp(frame.CropX + center.X * frame.CropWidth, -1, 2);
        local.Cv = Math.Clamp(frame.CropY + center.Y * frame.CropHeight, -1, 2);
    });
}
