using CommunityToolkit.Mvvm.ComponentModel;
using HappyPhoton.Models;

namespace HappyPhoton.ViewModels;

public partial class MainWindowViewModel
{
    public IReadOnlyList<FbddMode> NoiseReductionOptions { get; } =
    [
        FbddMode.Off,
        FbddMode.Light,
        FbddMode.Full
    ];

    [ObservableProperty]
    private int _captureSharpen;

    [ObservableProperty]
    private FbddMode _noiseReduction;

    [ObservableProperty]
    private int _chromaNr;

    public bool IsNoiseReductionEnabled => IsHighlightHandlingEnabled;

    public int CaptureSharpenDefault =>
        DetailSettings.GetCaptureSharpenDefault(IsHighlightHandlingEnabled);

    partial void OnCaptureSharpenChanged(int value) => OnDetailValueChanged();
    partial void OnNoiseReductionChanged(FbddMode value) => OnDetailValueChanged();
    partial void OnChromaNrChanged(int value) => OnDetailValueChanged();

    private void OnDetailValueChanged()
    {
        if (_isLoadingImage || !CanEditSelectedImage)
        {
            return;
        }

        UpdateCanReset();
        SchedulePreviewUpdate();
    }

    private void SaveDetailTo(EditSettings target)
    {
        target.Detail.CaptureSharpen = CaptureSharpen == CaptureSharpenDefault
            ? null
            : CaptureSharpen;
        target.Detail.NoiseReduction = NoiseReduction;
        target.Detail.ChromaNr = ChromaNr;
    }

    private void LoadDetailFrom(EditSettings source)
    {
        CaptureSharpen = source.Detail.ResolveCaptureSharpen(
            IsHighlightHandlingEnabled);
        NoiseReduction = source.Detail.NoiseReduction;
        ChromaNr = source.Detail.ChromaNr;
    }

    internal void ReconcileDetailCapability(bool isRawSource, bool capabilityChanged)
    {
        OnPropertyChanged(nameof(IsNoiseReductionEnabled));
        OnPropertyChanged(nameof(CaptureSharpenDefault));
        // Renders reconcile after every completion; snapping the displayed
        // value is only valid when the RAW capability itself flipped, or a
        // debounced-but-unsaved slider value gets reverted mid-edit.
        if (!capabilityChanged ||
            SelectedImage?.EditSettings.Detail.CaptureSharpen != null)
        {
            return;
        }

        var wasLoading = _isLoadingImage;
        _isLoadingImage = true;
        CaptureSharpen = DetailSettings.GetCaptureSharpenDefault(isRawSource);
        _isLoadingImage = wasLoading;
    }

    private void ResetDetailUi()
    {
        CaptureSharpen = CaptureSharpenDefault;
        NoiseReduction = FbddMode.Off;
        ChromaNr = 0;
    }
}
