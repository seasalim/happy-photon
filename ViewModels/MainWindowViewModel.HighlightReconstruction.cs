using CommunityToolkit.Mvvm.ComponentModel;
using HappyPhoton.Models;

namespace HappyPhoton.ViewModels;

public partial class MainWindowViewModel
{
    private const string RawFallbackStatus =
        "Decoded via fallback — RAW controls unavailable";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsHighlightHandlingEnabled))]
    [NotifyPropertyChangedFor(nameof(IsNoiseReductionEnabled))]
    [NotifyPropertyChangedFor(nameof(CaptureSharpenDefault))]
    private bool _isBrightnessEnabled = true;

    // Rides the brightness capability because crossing-on ⟺ RAW today; a
    // future display-referred crossing toggle must split these two gates.
    public bool IsHighlightHandlingEnabled => !IsBrightnessEnabled;

    public IReadOnlyList<HlReconstructionMode> HighlightHandlingOptions { get; } =
    [
        HlReconstructionMode.Clip,
        HlReconstructionMode.Blend
    ];

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
        var capabilityChanged = IsBrightnessEnabled != !isRawSource;
        IsBrightnessEnabled = !isRawSource;
        ReconcileDetailCapability(isRawSource, capabilityChanged);
        if (capabilityChanged && imageFile.IsRaw && !isRawSource)
        {
            ShowTransientStatus(RawFallbackStatus);
        }
    }

    private void ResetHighlightReconstructionUi() =>
        HlReconstruction = HlReconstructionMode.Clip;
}
