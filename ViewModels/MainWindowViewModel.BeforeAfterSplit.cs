using Avalonia;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HappyPhoton.Models;
using HappyPhoton.Services;

namespace HappyPhoton.ViewModels;

public partial class MainWindowViewModel
{
    private CancellationTokenSource? _beforeAfterRenderCts;
    private string? _beforeAfterRenderedHash;
    private (string? Hash, int MaxDimension) _beforeAfterRequest;
    [ObservableProperty] private bool _isBeforeAfterSplit;
    [ObservableProperty] private Bitmap? _beforeAfterPreviewImage;
    [ObservableProperty] private PixelSize _beforeAfterOriginalViewPixelSize;
    public SynchronizedViewService BeforeAfterSynchronizedView { get; } = new();
    [RelayCommand(CanExecute = nameof(CanToggleBeforeAfterSplit))]
    private async Task ToggleBeforeAfterSplitAsync()
    {
        if (IsBeforeAfterSplit)
        {
            CloseBeforeAfterSplit();
            return;
        }
        if (!CanToggleBeforeAfterSplit() || SelectedImage == null) return;
        if (_requestedPreviewIntent == PreviewSurfaceIntent.Original)
        {
            var generation = RequestEditedRender();
            if (!await UpdatePreviewWithCurrentSliders(generation: generation)) return;
        }
        IsBeforeAfterSplit = true;
        RequestBeforeAfterRender(CaptureRestingSettings());
    }
    private bool CanToggleBeforeAfterSplit() => IsBeforeAfterSplit ||
        IsDevelopMode && !IsFullScreenMode && !IsCropMode && CanEditSelectedImage;
    partial void OnIsBeforeAfterSplitChanged(bool value) =>
        ToggleBeforeAfterCommand.NotifyCanExecuteChanged();
    internal void PublishBeforeAfterRequiredDeviceLongEdge(int longEdge)
    {
        var rendered = BeforeAfterPreviewImage;
        if (IsBeforeAfterSplit && SelectedImage != null && rendered != null &&
            longEdge > Math.Max(rendered.PixelSize.Width, rendered.PixelSize.Height))
            RequestBeforeAfterRender(CaptureRestingSettings(), longEdge);
    }
    private void RequestBeforeAfterRender(
        EditSettings edited,
        int maxDimension = BaseImage.InteractivePreviewMaxDimension)
    {
        if (!IsBeforeAfterSplit || SelectedImage is not { } image) return;
        var settings = BuildOriginalRenderSettings(edited);
        var hash = RenderSettingsHash.Compute(settings);
        var renderedEdge = BeforeAfterPreviewImage is { } bitmap
            ? Math.Max(bitmap.PixelSize.Width, bitmap.PixelSize.Height) : 0;
        if (hash == _beforeAfterRequest.Hash &&
            maxDimension <= _beforeAfterRequest.MaxDimension ||
            hash == _beforeAfterRenderedHash && maxDimension <= renderedEdge) return;
        var cts = new CancellationTokenSource();
        var previous = Interlocked.Exchange(ref _beforeAfterRenderCts, cts);
        previous?.Cancel();
        previous?.Dispose();
        _beforeAfterRequest = (hash, maxDimension);
        _ = RenderBeforeAfterAsync(image, settings, hash, maxDimension, cts);
    }
    private async Task RenderBeforeAfterAsync(
        ImageFile image, EditSettings settings, string hash, int maxDimension,
        CancellationTokenSource cts)
    {
        try
        {
            var result = await ImageService.Previews.RenderCurrentBaseSideSurfaceAsync(
                image, settings, maxDimension, cts.Token);
            if (result.Bitmap == null || cts.IsCancellationRequested ||
                !ReferenceEquals(_beforeAfterRenderCts, cts) ||
                !ReferenceEquals(SelectedImage, image) || !IsBeforeAfterSplit)
            {
                result.Bitmap?.Dispose();
                return;
            }
            var previous = BeforeAfterPreviewImage;
            BeforeAfterPreviewImage = result.Bitmap;
            BeforeAfterOriginalViewPixelSize = result.OriginalViewPixelSize;
            _beforeAfterRenderedHash = hash;
            if (previous != null)
                _bitmapRetirement.Retire(previous,
                    () => ReferenceEquals(BeforeAfterPreviewImage, previous));
        }
        catch (OperationCanceledException) { }
        finally
        {
            if (ReferenceEquals(_beforeAfterRenderCts, cts))
            {
                _beforeAfterRenderCts = null;
                _beforeAfterRequest = default;
                cts.Dispose();
            }
        }
    }
    private void ResetBeforeAfterRender()
    {
        CancelAndDispose(ref _beforeAfterRenderCts);
        _beforeAfterRenderedHash = null;
        _beforeAfterRequest = default;
        BeforeAfterOriginalViewPixelSize = default;
        var bitmap = BeforeAfterPreviewImage;
        BeforeAfterPreviewImage = null;
        if (bitmap != null)
            _bitmapRetirement.Retire(bitmap, () => false);
    }
    private void CloseBeforeAfterSplit()
    {
        IsBeforeAfterSplit = false;
        ResetBeforeAfterRender();
    }
}
