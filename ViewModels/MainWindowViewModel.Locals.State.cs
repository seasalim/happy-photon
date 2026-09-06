using System.ComponentModel;
using HappyPhoton.Models;

namespace HappyPhoton.ViewModels;

public partial class MainWindowViewModel
{
    partial void OnWorkspaceModeChanging(WorkspaceMode value)
    {
        if (value != WorkspaceMode.Develop) CloseLocals();
    }

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName is nameof(PreviewImage) or nameof(IsFullScreenMode) or
            nameof(IsBeforeAfterSplit) or nameof(CanEditSelectedImage) or nameof(IsColorEditingEnabled) or
            nameof(IsWhiteBalancePicking) or nameof(IsShowingOriginal) or nameof(CurrentCrop) or nameof(HorizonRotation))
            NotifyLocalsState();
        if (e.PropertyName == nameof(IsLocalMaskVisible))
            OnPropertyChanged(nameof(VisibleClippingOverlaySides));
    }
}
