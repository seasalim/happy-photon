using System.ComponentModel;
using HappyPhoton.Models;

namespace HappyPhoton.ViewModels;

public partial class MainWindowViewModel
{
    partial void OnWorkspaceModeChanging(WorkspaceMode value)
    {
        if (value != WorkspaceMode.Develop) CloseLocals();
    }

    public bool IsToolActive => IsCropMode || IsLocalsMode;

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        // Selection and history rebinds refresh both committed-content indicators.
        if (e.PropertyName == nameof(HasLocals))
            OnPropertyChanged(nameof(HasCommittedCrop));
        if (e.PropertyName is nameof(IsCropMode) or nameof(IsLocalsMode))
            OnPropertyChanged(nameof(IsToolActive));
        if (e.PropertyName is nameof(PreviewImage) or nameof(IsFullScreenMode) or
            nameof(IsBeforeAfterSplit) or nameof(CanEditSelectedImage) or nameof(IsColorEditingEnabled) or
            nameof(IsWhiteBalancePicking) or nameof(IsShowingOriginal) or nameof(CurrentCrop) or nameof(HorizonRotation))
            NotifyLocalsState();
        if (e.PropertyName == nameof(IsLocalMaskVisible))
            OnPropertyChanged(nameof(VisibleClippingOverlaySides));
    }
}
