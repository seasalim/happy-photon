using HappyPhoton.Models;

namespace HappyPhoton.ViewModels;

public partial class MainWindowViewModel
{
    private void RollbackCropReservation(
        ImageFile image,
        EditSettings previousSettings,
        long generation,
        PreviewSurfaceIntent previousIntent)
    {
        if (generation != Volatile.Read(ref _latestPreviewOutcomeGeneration) ||
            !ReferenceEquals(SelectedImage, image))
        {
            return;
        }

        image.EditSettings = previousSettings.Clone();
        image.HasEdits = image.EditSettings.HasEdits;
        _lastSavedState = image.EditSettings.Clone();
        ApplyRollbackOutcome(image, generation, previousIntent);
    }

    private bool RollbackEditReservation(
        ImageFile image,
        EditSettings previousSettings,
        long generation,
        PreviewSurfaceIntent previousIntent)
    {
        if (generation != Volatile.Read(ref _latestPreviewOutcomeGeneration) ||
            !ReferenceEquals(SelectedImage, image))
        {
            return false;
        }

        image.EditSettings = previousSettings.Clone();
        image.HasEdits = image.EditSettings.HasEdits;
        _isLoadingImage = true;
        try
        {
            LoadSlidersFrom(image.EditSettings);
            SyncRawProfilePickerSelection(image.EditSettings.RawProfile);
        }
        finally
        {
            _isLoadingImage = false;
        }
        _lastSavedState = image.EditSettings.Clone();
        UpdateCanReset();
        ApplyRollbackOutcome(image, generation, previousIntent);
        return true;
    }

    private void ApplyRollbackOutcome(
        ImageFile image,
        long generation,
        PreviewSurfaceIntent previousIntent)
    {
        ApplyRenderOutcome(new RenderOutcome
        {
            Image = image,
            Generation = generation,
            Class = RenderOutcomeClass.Rollback,
            Intent = _appliedPreviewIntent,
            RollbackRequestedIntent = previousIntent
        });
    }
}
