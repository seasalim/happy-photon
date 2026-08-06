using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using HappyPhoton.Models;
using HappyPhoton.Services;

namespace HappyPhoton.ViewModels;

public partial class MainWindowViewModel
{    [RelayCommand]
    private void ZoomIn()
    {
        ZoomLevel = Math.Min(MaxZoom, ZoomLevel * 1.25);
    }

    [RelayCommand]
    private void ZoomOut()
    {
        ZoomLevel = Math.Max(MinZoom, ZoomLevel / 1.25);
    }

    [RelayCommand]
    private void RotateLeft()
    {
        if (SelectedImage == null) return;
        
        // Rotate counter-clockwise (subtract 90, wrap around)
        // Note: Rotation is separate from undo/reset - it's a geometric transform
        Rotation = (Rotation - 90 + 360) % 360;
        SelectedImage.EditSettings.Rotation = Rotation;
        
        SchedulePreviewUpdate(pushUndo: false);
        
        // Refresh thumbnail with new rotation
        RefreshSelectedThumbnail();
    }

    [RelayCommand]
    private void RotateRight()
    {
        if (SelectedImage == null) return;

        // Rotate clockwise (add 90, wrap around)
        // Note: Rotation is separate from undo/reset - it's a geometric transform
        Rotation = (Rotation + 90) % 360;
        SelectedImage.EditSettings.Rotation = Rotation;

        SchedulePreviewUpdate(pushUndo: false);

        // Refresh thumbnail with new rotation
        RefreshSelectedThumbnail();
    }

    /// <summary>
    /// Toggles crop mode on/off. When entering, initializes crop region.
    /// </summary>
    [RelayCommand]
    private void ToggleCropMode()
    {
        if (SelectedImage == null) return;

        if (IsCropMode)
        {
            // Exiting crop mode without applying - restore original
            CancelCrop();
        }
        else
        {
            // Entering crop mode
            EnterCropMode();
        }
    }

    private void EnterCropMode()
    {
        if (SelectedImage == null) return;

        // Save original crop for cancel
        _cropBeforeEdit = SelectedImage.EditSettings.Crop?.Clone();
        _horizonRotationBeforeEdit = SelectedImage.EditSettings.HorizonRotation;

        // Initialize CurrentCrop - use existing or create new full-image region
        CurrentCrop = SelectedImage.EditSettings.Crop?.Clone() ?? new CropRegion();
        ConstrainCropToSafeHorizonBounds();

        IsCropMode = true;

        // Refresh preview to show uncropped image (UpdatePreviewWithCurrentSliders
        // will skip crop when IsCropMode is true)
        SchedulePreviewUpdate(pushUndo: false);
    }

    /// <summary>
    /// Applies the current crop and exits crop mode.
    /// </summary>
    [RelayCommand]
    private async Task ApplyCropAsync()
    {
        if (SelectedImage == null || CurrentCrop == null)
        {
            IsCropMode = false;
            return;
        }

        ConstrainCropToSafeHorizonBounds();

        // Apply crop to settings
        if (CurrentCrop.IsFullImage)
        {
            SelectedImage.EditSettings.Crop = null;
        }
        else
        {
            SelectedImage.EditSettings.Crop = CurrentCrop.Clone();
        }
        SelectedImage.EditSettings.HorizonRotation = HorizonRotation;

        SelectedImage.HasEdits = SelectedImage.EditSettings.HasEdits;

        // Save to catalog
        await SaveEditSettingsAsync(SelectedImage);

        // Exit crop mode first, so preview update shows cropped result
        IsCropMode = false;
        _cropBeforeEdit = null;

        // Update preview and thumbnail (no undo for crop - it's a geometric transform)
        SchedulePreviewUpdate(pushUndo: false);
        RefreshSelectedThumbnail();
    }

    /// <summary>
    /// Resets crop to full image while in crop mode.
    /// </summary>
    [RelayCommand]
    private void ResetCrop()
    {
        // Create new instance to trigger property change (modifying existing object won't update bindings)
        CurrentCrop = new CropRegion();
        HorizonRotation = 0.0;
        ScheduleCropPreviewUpdate();
    }

    /// <summary>
    /// Cancels crop mode and restores original crop.
    /// </summary>
    [RelayCommand]
    private void CancelCrop()
    {
        // Restore original crop
        CurrentCrop = _cropBeforeEdit?.Clone();
        HorizonRotation = _horizonRotationBeforeEdit;

        IsCropMode = false;
        _cropBeforeEdit = null;

        // Refresh preview to show image with original crop applied
        SchedulePreviewUpdate(pushUndo: false);
    }

    private void RefreshSelectedThumbnail()
    {
        var image = SelectedImage;
        if (image == null) return;
        var refresh = ReplaceDebounce(ref _thumbnailDebounce);
        _ = RefreshSelectedThumbnailAsync(image, refresh.Token);
    }

    private async Task RefreshSelectedThumbnailAsync(
        ImageFile image,
        CancellationToken cancellationToken)
    {
        var thumbnail = await ImageService.LoadThumbnailAsync(
            image,
            cancellationToken);
        if (cancellationToken.IsCancellationRequested)
        {
            thumbnail?.Dispose();
            return;
        }

        if (ReferenceEquals(SelectedImage, image) && Library.Contains(image))
        {
            image.ThumbnailLoadFailed = thumbnail == null;
            if (thumbnail != null) Library.ReplaceThumbnail(image, thumbnail);
        }
        else
        {
            thumbnail?.Dispose();
        }
    }

    private void ConstrainCropToSafeHorizonBounds()
    {
        if (CurrentCrop == null) return;

        var safeCrop = GetSafeHorizonCropBounds();
        if (safeCrop == null) return;

        CurrentCrop = CropGeometry.Intersect(CurrentCrop, safeCrop);
    }

    private CropRegion? GetSafeHorizonCropBounds()
    {
        if (SelectedImage == null || HorizonRotation == 0.0) return null;

        var width = SelectedImage.PixelWidth;
        var height = SelectedImage.PixelHeight;
        if ((width <= 0 || height <= 0) && PreviewImage != null)
        {
            width = PreviewImage.PixelSize.Width;
            height = PreviewImage.PixelSize.Height;
        }

        if (width <= 0 || height <= 0) return null;

        if (Rotation is 90 or 270)
        {
            (width, height) = (height, width);
        }

        return CropGeometry.SafeBoundsAfterRotation(width, height, HorizonRotation);
    }

    [RelayCommand]
    private void ZoomActual()
    {
        ZoomLevel = 1.0; // 100% actual pixels
    }

    public void AdjustZoom(double delta)
    {
        if (delta > 0)
            ZoomLevel = Math.Min(MaxZoom, ZoomLevel * 1.1);
        else
            ZoomLevel = Math.Max(MinZoom, ZoomLevel / 1.1);
    }

    // Folder Tree Methods
}
