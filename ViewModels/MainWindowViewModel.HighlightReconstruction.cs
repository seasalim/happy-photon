using CommunityToolkit.Mvvm.ComponentModel;
using HappyPhoton.Models;

namespace HappyPhoton.ViewModels;

public partial class MainWindowViewModel
{
    private const string RawFallbackStatus =
        "Decoded via fallback — RAW controls unavailable";
    private const string MonochromeRawStatus =
        "Monochrome RAW — color controls unavailable";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsColorEditingEnabled))]
    [NotifyPropertyChangedFor(nameof(AreColorCurveChannelsEnabled))]
    private bool _isMonochromeSource;

    public bool IsColorEditingEnabled => !IsMonochromeSource;
    public bool AreColorCurveChannelsEnabled => !IsMonochromeSource;
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
        OnPropertyChanged(nameof(IsOpticsEnabled));
        if (capabilityChanged && imageFile.IsRaw && !isRawSource)
        {
            ShowTransientStatus(RawFallbackStatus);
        }
    }

    internal void ReconcileMonochromeCapability(
        ImageFile imageFile,
        bool isMonochrome)
    {
        if (IsMonochromeSource == isMonochrome)
        {
            return;
        }
        if (isMonochrome && ActiveCurveChannel != ToneCurveChannel.Composite)
        {
            ActiveCurveChannel = ToneCurveChannel.Composite;
        }
        IsMonochromeSource = isMonochrome;
        NotifyWhiteBalanceCommandState();
        if (isMonochrome && imageFile.IsRaw)
        {
            ShowTransientStatus(MonochromeRawStatus);
        }
    }

    private void ResetHighlightReconstructionUi() =>
        HlReconstruction = HlReconstructionMode.Clip;
}
