using CommunityToolkit.Mvvm.ComponentModel;
using HappyPhoton.Models;

namespace HappyPhoton.ViewModels;

public partial class MainWindowViewModel
{
    [ObservableProperty]
    private HlReconstructionMode _hlReconstruction =
        HlReconstructionMode.Clip;

    partial void OnHlReconstructionChanged(HlReconstructionMode value)
    {
        if (_isLoadingImage || SelectedImage == null) return;
        UpdateCanReset();
        SchedulePreviewUpdate();
    }

    private void SaveHighlightReconstructionTo(EditSettings target) =>
        target.HlReconstruction = HlReconstruction;

    private void LoadHighlightReconstructionFrom(EditSettings source) =>
        HlReconstruction = source.HlReconstruction;

    internal void ReconcileHighlightReconstructionCapability(
        ImageFile imageFile,
        bool isRawSource)
    {
        if (imageFile.IsRaw && !isRawSource)
        {
            ShowTransientStatus(
                "Decoded via fallback — RAW controls unavailable");
        }
    }

    private void ResetHighlightReconstructionUi() =>
        HlReconstruction = HlReconstructionMode.Clip;
}
