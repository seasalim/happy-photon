using Avalonia.Threading;
using HappyPhoton.Models;
using HappyPhoton.Services;

namespace HappyPhoton.ViewModels;

public partial class MainWindowViewModel
{
    private bool _previewSourceFailure;

    internal string? PreviewSourceFailureStatus =>
        SelectedImage?.SourceRequiresHydration == true
            ? "The original is online-only. Download and open it to develop this photo."
            : _previewSourceFailure
                ? "The original is unavailable. Make it available locally, then retry."
                : null;

    internal string? SelectedRawDecodeFailureStatus =>
        SelectedImage?.RawDecodeFailed == true
            ? "This RAW file could not be decoded. It may use an unsupported encoding such as Nikon HE."
            : null;

    internal string? GlobalRawRuntimeFailureStatus =>
        IsRawRuntimeDegraded
            ? "RAW support is unavailable. Reinstall Happy Photon to repair the native RAW runtime."
            : null;

    private void OnPreviewLoadCompleted(
        object? sender,
        PreviewLoadOutcome outcome) =>
        Dispatcher.UIThread.Post(() => ApplyPreviewLoadOutcome(outcome));

    internal void ApplyPreviewLoadOutcome(PreviewLoadOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        ApplyRenderOutcome(new RenderOutcome
        {
            Image = outcome.ImageFile,
            Generation = outcome.Generation,
            Class = RenderOutcomeClass.Failure,
            Intent = _requestedPreviewIntent,
            Succeeded = outcome.Succeeded,
            Failure = outcome.Failure
        });
    }

    private void ApplyPreviewFailure(RenderOutcome outcome)
    {
        if (outcome.Class != RenderOutcomeClass.Failure)
        {
            return;
        }
        if (outcome.Succeeded)
        {
            outcome.Image!.RawDecodeFailed = false;
            _previewSourceFailure = false;
        }
        else
        {
            ApplyPreviewClipping(null, null);
            _previewSourceFailure =
                outcome.Failure == BaseImageLoadFailure.SourceUnavailable;
            if (outcome.Image!.IsRaw &&
                outcome.Failure is BaseImageLoadFailure.UnsupportedRaw or
                    BaseImageLoadFailure.DecodeFailed)
            {
                outcome.Image.RawDecodeFailed = true;
            }
        }
        OnPropertyChanged(nameof(StatusMessage));
    }

    private void OnPreviewFailureSelectionChanged()
    {
        _previewSourceFailure = false;
        if (TransientStatus == RawFallbackStatus)
        {
            _transientStatusCts?.Cancel();
            TransientStatus = null;
        }
        OnPropertyChanged(nameof(StatusMessage));
    }
}
