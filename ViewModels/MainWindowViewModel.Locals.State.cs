using System.ComponentModel;
using HappyPhoton.Models;

namespace HappyPhoton.ViewModels;

public partial class MainWindowViewModel
{
    partial void OnWorkspaceModeChanging(WorkspaceMode value)
    {
        if (value != WorkspaceMode.Develop)
        {
            CloseSpots();
            CloseLocals();
        }
    }

    public bool IsToolActive => IsCropMode || IsLocalsMode || IsSpotsMode;

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(IsLocalHuePicking))
            OnPropertyChanged(nameof(IsLocalMaskVisible));

        if (e.PropertyName == nameof(IsLocalMaskVisible))
        {
            if (IsLocalMaskVisible) RefreshLocalMaskPresentation();
            else InvalidateLocalMask();
        }

        if (e.PropertyName is nameof(PreviewImage) or nameof(SelectedImage) or nameof(HorizonRotation) or nameof(Rotation) or
            nameof(GeometryVertical) or nameof(GeometryHorizontal) or nameof(GeometryAspect) or nameof(GeometryDistortion))
        {
            RefreshCropFrameSize();
        }

        base.OnPropertyChanged(e);

        if (e.PropertyName is nameof(CurrentCrop) or nameof(CropFrameSize) or nameof(IsCropMode) or nameof(CanEditSelectedImage))
        {
            NotifyCropRatio();
        }

        // Selection and history rebinds refresh both committed-content indicators.
        if (e.PropertyName == nameof(HasLocals))
            OnPropertyChanged(nameof(HasCommittedCrop));
        if (e.PropertyName is nameof(IsCropMode) or nameof(IsLocalsMode) or nameof(IsSpotsMode))
            OnPropertyChanged(nameof(IsToolActive));
        if (e.PropertyName is nameof(PreviewImage) or nameof(IsFullScreenMode) or
            nameof(IsBeforeAfterSplit) or nameof(CanEditSelectedImage) or nameof(IsColorEditingEnabled) or
            nameof(IsWhiteBalancePicking) or nameof(IsShowingOriginal) or nameof(CurrentCrop) or nameof(HorizonRotation))
            NotifyLocalsState();
        if (e.PropertyName == nameof(IsLocalMaskVisible))
            OnPropertyChanged(nameof(VisibleClippingOverlaySides));
    }
}
