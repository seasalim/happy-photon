using Avalonia.Threading;
using HappyPhoton.Models;
using HappyPhoton.Services;

namespace HappyPhoton.ViewModels;

public partial class MainWindowViewModel
{
    private long _latestPreviewOutcomeGeneration;
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
        if (!ReferenceEquals(SelectedImage, outcome.ImageFile) ||
            outcome.Generation < Volatile.Read(
                ref _latestPreviewOutcomeGeneration))
        {
            return;
        }

        Volatile.Write(
            ref _latestPreviewOutcomeGeneration,
            outcome.Generation);
        if (outcome.Succeeded)
        {
            outcome.ImageFile.RawDecodeFailed = false;
            _previewSourceFailure = false;
        }
        else
        {
            ClearPreviewClippingArtifacts();
            _previewSourceFailure =
                outcome.Failure == BaseImageLoadFailure.SourceUnavailable;
            if (outcome.ImageFile.IsRaw &&
                outcome.Failure is BaseImageLoadFailure.UnsupportedRaw or
                    BaseImageLoadFailure.DecodeFailed)
            {
                outcome.ImageFile.RawDecodeFailed = true;
            }
        }

        OnPropertyChanged(nameof(StatusMessage));
    }

    private void OnPreviewFailureSelectionChanged()
    {
        Volatile.Write(ref _latestPreviewOutcomeGeneration, 0);
        _previewSourceFailure = false;
        OnPropertyChanged(nameof(StatusMessage));
    }
}
