using HappyPhoton.Models;
using HappyPhoton.Services;

namespace HappyPhoton.ViewModels;

public partial class MainWindowViewModel
{
    // The caller owns this document and its persistence. Paste preserves the live
    // crop draft and pending lens choice separately from committed values when requested.
    private void InstallDevelopDocument(
        ImageFile image, EditSettings settings, bool preserveCropDraft, bool preserveLensDraft = false)
    {
        var rotation = Rotation;
        var horizon = HorizonRotation;
        var crop = CurrentCrop?.Clone();
        var lensProfileOverride = LensProfileOverride;
        var previousProfile = image.EditSettings.RawProfile;
        _isLoadingImage = true;

        try
        {
            if (!preserveCropDraft)
            {
                IsCropMode = false;
                _cropBeforeEdit = null;
                _horizonRotationBeforeEdit = 0;
            }

            if (preserveLensDraft) settings.Lens.ProfileOverride = image.EditSettings.Lens.ProfileOverride;
            LoadSlidersFrom(settings);
            if (preserveLensDraft) LensProfileOverride = lensProfileOverride;

            if (preserveCropDraft)
            {
                // The proposal contains live controls; persist only the committed frame and lens choice.
                settings.Rotation = image.EditSettings.Rotation;
                settings.HorizonRotation = image.EditSettings.HorizonRotation;
                settings.Lens.ProfileOverride = image.EditSettings.Lens.ProfileOverride;

                Rotation = rotation;
                HorizonRotation = horizon;
                CurrentCrop = crop;
                LensProfileOverride = lensProfileOverride;
            }
        }
        finally
        {
            _isLoadingImage = false;
        }

        image.EditSettings = settings;
        RebindLocalSelection();

        if (!RawProfilePickerProjector.ProfilesEqual(previousProfile, settings.RawProfile))
        {
            SupersedeRawProfileDiscovery();
        }

        _rawProfileTransientError = null;
        PublishRawProfilePickerState();
        LoadCurrentCurveFrom(settings);
        image.HasEdits = settings.HasEdits;
    }
}
