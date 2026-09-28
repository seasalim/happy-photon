using Avalonia.Media.Imaging;
using Avalonia.Threading;
using HappyPhoton.Models;
using HappyPhoton.Services;

namespace HappyPhoton.ViewModels;

public partial class MainWindowViewModel
{
    private DevelopFullBase? _fullBase;

    private ImageFile? _fullBaseImage;

    private string? _fullDecodeKey;

    private string? _fullRequestedDecodeKey;

    private CancellationTokenSource? _fullRenderCts;

    private Bitmap? _fullBitmap;

    private Bitmap? _fullRestingBitmap;

    private Task _fullWork = Task.CompletedTask;

    private Task _fullRelease = Task.CompletedTask;

    private int _fullThreshold;

    private long _fullGeneration;

    private int _interactiveRenders;

    private string _oneToOneStatus = "1:1";

    public string OneToOneStatus
    {
        get => _oneToOneStatus;
        private set => SetProperty(ref _oneToOneStatus, value);
    }

    internal int HeldFullBaseCount => _fullBase?.HeldCount ?? 0;

    internal Task FullResolutionWork => _fullWork;

    internal int RefinementWorkerBudget => IsSliderEditActive || _curveGestureStartState != null ||
        Volatile.Read(ref _interactiveRenders) > 0 || _previewDebounceTask is { IsCompleted: false }
            ? 2 : Environment.ProcessorCount;

    internal void RefreshFullResolution()
    {
        CancelFullRefinement();
        EvaluateFullResolutionDemand();
    }

    private void EvaluateFullResolutionDemand()
    {
        var image = SelectedImage;
        var threshold = _restingAchievableLongEdge > 0 ? _restingAchievableLongEdge : _fullThreshold;

        if (!IsDevelopMode || IsCropMode || IsShowingOriginal || _isHoveringPreset ||
            _hoveredHistoryEntry != null || image == null || _renderOutcomeChannelClosed ||
            (threshold > 0 && !ExceedsRestingBound(threshold)))
        {
            ReleaseFullResolution();
            return;
        }

        if (!ImageService.Previews.CanRefine(image))
        {
            ReleaseFullResolution();
            OneToOneStatus = "1:1 · preview detail";
            return;
        }

        if (_restingAchievableLongEdge <= 0) return;
        var parent = _restingParent;
        var settings = _restingSettings;
        if (parent == null || settings == null) return;

        if (_fullBase != null && (!ReferenceEquals(_fullBaseImage, image) || _fullDecodeKey != parent.DecodeKey))
            ReleaseFullResolution();

        // The full frame supersedes any armed or in-flight large-base render.
        CancelRestingTimerOnly();
        _fullBase ??= ImageService.Previews.CreateFullBase(image, settings.Clone());
        _fullThreshold = _restingAchievableLongEdge;
        _fullBaseImage = image;
        _fullDecodeKey = parent.DecodeKey;
        _fullRequestedDecodeKey = BaseDecodeSettings.From(settings).CacheKey;
        if (_fullRenderCts != null && _fullGeneration == parent.Generation) return;

        CancelFullRefinement();
        _fullGeneration = parent.Generation;
        var cancellation = _fullRenderCts = new CancellationTokenSource();
        OneToOneStatus = "1:1 · refining";
        var run = RefineFullResolutionAsync(_fullBase, settings.Clone(), parent,
            _restingSurfaceGeneration, cancellation);
        _fullWork = Task.WhenAll(_fullWork, run);
    }

    private async Task RefineFullResolutionAsync(DevelopFullBase holder, EditSettings settings,
        PreviewRenderIdentity parent, long surface, CancellationTokenSource cancellation)
    {
        Bitmap? bitmap = null;
        var completed = false;
        var token = cancellation.Token;

        try
        {
            await _fullRelease;
            token.ThrowIfCancellationRequested();
            bitmap = await ImageService.Previews.RenderFullResolutionAsync(holder, settings, parent,
                () => RefinementWorkerBudget, token);
            completed = bitmap != null;

            if (cancellation.IsCancellationRequested || !ReferenceEquals(_fullBase, holder) ||
                _restingParent?.Generation != parent.Generation ||
                surface != Volatile.Read(ref _latestPreviewOutcomeGeneration) ||
                !ReferenceEquals(SelectedImage, parent.ImageFile) || !IsDevelopMode)
                return;

            if (bitmap == null)
            {
                OneToOneStatus = "1:1 · preview detail";
                return;
            }

            var previous = _fullBitmap;
            _fullRestingBitmap ??= PreviewImage;
            _fullBitmap = bitmap;
            PreviewImage = bitmap;
            bitmap = null;
            OneToOneStatus = "1:1";
            ImageService.Previews.FullResolutionTrace?.Invoke("installed");
            ImageServiceHelpers.LogDisplayTrace($"paint source=full-base bitmap={PreviewImage.PixelSize} " +
                $"decode={parent.DecodeKey} settings={parent.SettingsHash}");
            if (previous != null) await RetireFullBitmapAsync(previous);
        }
        catch (OperationCanceledException) { }
        catch (Exception exception)
        {
            ImageServiceHelpers.LogDisplayTrace($"full-base failed={exception.Message}");
            if (ReferenceEquals(_fullRenderCts, cancellation)) OneToOneStatus = "1:1 · preview detail";
        }
        finally
        {
            bitmap?.Dispose();
            bitmap = null;

            // Cancelled slider work is reclaimed by the next completion or release, never per tick.
            if (completed)
            {
                ImageService.Previews.FullResolutionTrace?.Invoke("gc-start");
                await DevelopFullBase.ReclaimAsync();
                ImageService.Previews.FullResolutionTrace?.Invoke("gc-end");
            }
        }
    }

    private static async Task RetireFullBitmapAsync(Bitmap bitmap)
    {
        await Dispatcher.UIThread.InvokeAsync(bitmap.Dispose, DispatcherPriority.Loaded);
    }

    private void RestoreRestingBitmap()
    {
        var bitmap = _fullBitmap;
        if (bitmap == null) return;

        if (ReferenceEquals(PreviewImage, bitmap)) PreviewImage = _fullRestingBitmap;
        _fullBitmap = null;
        _fullRestingBitmap = null;
        _fullWork = Task.WhenAll(_fullWork, RetireFullBitmapAsync(bitmap));
    }

    private void InvalidateFullRefinementForEdit()
    {
        CancelFullRefinement();

        if (_fullBase != null && SelectedImage != null &&
            _fullRequestedDecodeKey != BaseDecodeSettings.From(CaptureRestingSettings()).CacheKey)
            ReleaseFullResolution();
    }

    private void CancelFullRefinement()
    {
        var cancellation = _fullRenderCts;
        _fullRenderCts = null;
        cancellation?.Cancel();
        cancellation?.Dispose();
    }

    private void ReleaseFullResolution()
    {
        CancelFullRefinement();
        RestoreRestingBitmap();
        var holder = _fullBase;
        _fullBase = null;
        _fullBaseImage = null;
        _fullThreshold = 0;
        _fullDecodeKey = null;
        _fullRequestedDecodeKey = null;
        OneToOneStatus = "1:1";
        if (holder == null) return;

        _fullWork = _fullRelease = ReleaseFullResolutionAsync(holder, _fullWork);
    }

    private static async Task ReleaseFullResolutionAsync(DevelopFullBase holder, Task pending)
    {
        await holder.DisposeAsync();
        await pending;
        await DevelopFullBase.ReclaimAsync();
    }
}
