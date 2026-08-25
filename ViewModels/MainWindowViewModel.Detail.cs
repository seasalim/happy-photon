using CommunityToolkit.Mvvm.ComponentModel;
using HappyPhoton.Models;

namespace HappyPhoton.ViewModels;

public partial class MainWindowViewModel
{
    [ObservableProperty]
    private int _captureSharpen;

    [ObservableProperty]
    private int _luminanceNr;

    [ObservableProperty]
    private int _chromaNr;

    public int CaptureSharpenDefault =>
        DetailSettings.GetCaptureSharpenDefault(IsHighlightHandlingEnabled);

    partial void OnCaptureSharpenChanged(int value) => OnDetailValueChanged();
    partial void OnLuminanceNrChanged(int value) => OnDetailValueChanged();
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
        target.Detail.LuminanceNr = LuminanceNr;
        target.Detail.ChromaNr = ChromaNr;
    }

    private void LoadDetailFrom(EditSettings source)
    {
        CaptureSharpen = source.Detail.ResolveCaptureSharpen(
            IsHighlightHandlingEnabled);
        LuminanceNr = source.Detail.LuminanceNr;
        ChromaNr = source.Detail.ChromaNr;
    }

    internal void ReconcileDetailCapability(bool isRawSource, bool capabilityChanged)
    {
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
        LuminanceNr = 0;
        ChromaNr = 0;
    }
}
