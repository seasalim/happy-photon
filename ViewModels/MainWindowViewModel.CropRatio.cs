using Avalonia;
using CommunityToolkit.Mvvm.Input;
using HappyPhoton.Models;
using HappyPhoton.Services;

namespace HappyPhoton.ViewModels;

public partial class MainWindowViewModel
{
    public IReadOnlyList<string> CropRatios => CropGeometry.RatioNames;

    public PixelSize CropFrameSize { get; private set; }

    private void RefreshCropFrameSize()
    {
        var identity = PreviewImage == null ? null : ImageService.Previews.TryGetPreviewRenderIdentity(PreviewImage);
        var settings = new EditSettings { Rotation = Rotation, HorizonRotation = HorizonRotation };
        SaveGeometryTo(settings);
        CropFrameSize = identity == null || !ReferenceEquals(identity.ImageFile, SelectedImage) ? default :
            RenderGeometry.CalculateOriginalViewSize(identity.OriginalImageSize.Width,
                identity.OriginalImageSize.Height, settings);

        OnPropertyChanged(nameof(CropFrameSize));
    }

    private double CropFrameRatio => CropFrameSize.Width / (double)CropFrameSize.Height;

    public bool CanChooseCropRatio => IsCropMode && CanEditSelectedImage && CropFrameSize is { Width: > 0, Height: > 0 };

    public string CropRatio => CanChooseCropRatio && CurrentCrop is { } crop
        ? CropGeometry.DeriveRatio(crop, CropFrameRatio) : "Custom";

    public bool CanSwapCropRatio => CanChooseCropRatio && CropRatio is not ("Custom" or "1:1") &&
        CurrentCrop is { } crop &&
        CropGeometry.CanFit(CropGeometry.SwappedRatio(crop, CropFrameRatio));

    public void ChooseCropRatio(string name)
    {
        if (!CanChooseCropRatio || CurrentCrop == null || name == "Custom" || !CropRatios.Contains(name)) return;

        var ratio = CropGeometry.TargetRatio(CurrentCrop, name, CropFrameRatio);
        if (!CropGeometry.CanFit(ratio)) return;

        IsCropAspectLocked = true;
        CurrentCrop = CropGeometry.Fit(CurrentCrop, ratio);
    }

    [RelayCommand(CanExecute = nameof(CanSwapCropRatio))]
    private void SwapCropRatio()
    {
        if (!CanSwapCropRatio || CurrentCrop == null) return;

        CurrentCrop = CropGeometry.Swap(CurrentCrop, CropFrameRatio);
    }

    [RelayCommand]
    private void HandleX()
    {
        if (IsCropMode) SwapCropRatio();
        else ToggleRejectedImageCommand.Execute(null);
    }

    private void NotifyCropRatio()
    {
        OnPropertyChanged(nameof(CanChooseCropRatio));
        OnPropertyChanged(nameof(CropRatio));
        OnPropertyChanged(nameof(CanSwapCropRatio));
        SwapCropRatioCommand.NotifyCanExecuteChanged();
    }
}
