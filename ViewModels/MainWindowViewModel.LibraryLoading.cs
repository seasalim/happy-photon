using Avalonia.Threading;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.Input;
using HappyPhoton.Models;
using HappyPhoton.Services;

namespace HappyPhoton.ViewModels;

public partial class MainWindowViewModel
{
    public async Task LoadFolderAsync(string folderPath)
    {
        var generation = Interlocked.Increment(ref _libraryGeneration);
        var requestCts = new CancellationTokenSource();
        var previousThumbnailLoad = Interlocked.Exchange(
            ref _thumbnailLoadingCts, requestCts);
        if (previousThumbnailLoad != null)
        {
            _ = CancelAsync(previousThumbnailLoad);
        }
        var cancellationToken = requestCts.Token;

        // Cancel any in-progress preview loading
        _previewLoadingCts?.Cancel();

        CurrentFolderPath = folderPath;
        SelectedImage = null;
        var pumpStarted = false;

        try
        {
            var imageFiles = await Task.Run(
                () => _folderService.GetImagesInFolder(folderPath).ToList(),
                cancellationToken);
            var imagePaths = imageFiles.Select(image => image.FilePath).ToArray();
            // Microsoft.Data.Sqlite async APIs can perform synchronous disk work.
            var catalogStates = await Task.Run(
                () => _catalogService.LoadOrCreateImageStatesAsync(
                    imagePaths, cancellationToken),
                cancellationToken);
            foreach (var imageFile in imageFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var state = catalogStates[imageFile.FilePath];
                imageFile.CatalogId = state.CatalogId;
                imageFile.EditSettings = state.EditSettings;
                imageFile.HasEdits = state.EditSettings.HasEdits;
                imageFile.Flag = state.Flag;
                imageFile.Rating = state.Rating;
            }

            cancellationToken.ThrowIfCancellationRequested();

            // Fresh ImageFile instances replace the old burst indicators immediately.
            ResetBurstState();
            ResetThumbnailViewport();
            Library.SetImages(imageFiles);

            // Defer first image selection until after UI settles.
            if (Library.VisibleImages.Count > 0)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    if (!cancellationToken.IsCancellationRequested &&
                        Library.VisibleImages.Count > 0 && SelectedImage == null)
                    {
                        SelectedImage = Library.FirstVisible();
                    }
                }, DispatcherPriority.Background);
            }

            pumpStarted = true;
            StartThumbnailSession(imageFiles, requestCts, generation);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (!pumpStarted)
            {
                // Before the pump starts, LoadFolderAsync still owns disposal.
                Interlocked.CompareExchange(
                    ref _thumbnailLoadingCts, null, requestCts);
                requestCts.Dispose();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"Folder load failed for {folderPath}: {ex.Message}");
            var wasCurrent = false;
            if (!pumpStarted)
            {
                // Before the pump starts, LoadFolderAsync still owns disposal.
                wasCurrent = ReferenceEquals(
                    Interlocked.CompareExchange(
                        ref _thumbnailLoadingCts, null, requestCts),
                    requestCts);
                requestCts.Dispose();
            }
            if (wasCurrent)
            {
                ResetBurstState();
                Library.SetImages(Array.Empty<ImageFile>());
                ShowTransientStatus($"Unable to load folder: {ex.Message}");
            }
        }
    }

    private async Task LoadPreviewAsync(ImageFile imageFile)
    {
        _previewLoadingCts?.Cancel();
        var requestCts = new CancellationTokenSource();
        _previewLoadingCts = requestCts;
        var ct = requestCts.Token;

        // Delay showing loading indicator to avoid flicker on fast loads
        var loadingIndicatorCts = new CancellationTokenSource();
        _ = ShowLoadingIndicatorAfterDelay(500, requestCts, loadingIndicatorCts.Token);

        try
        {
            // Clear the old preview immediately to avoid showing stale image
            ReplacePreviewImage(null);

            // Show thumbnail as placeholder immediately while full preview loads
            PlaceholderImage = imageFile.Thumbnail;

            // Load preview without histogram for faster initial display
            var (preview, _) = await ImageService.LoadPreviewWithHistogramAsync(
                imageFile, imageFile.EditSettings, skipHistogram: true, ct);

            if (ct.IsCancellationRequested ||
                !ReferenceEquals(_previewLoadingCts, requestCts) ||
                !ReferenceEquals(SelectedImage, imageFile))
            {
                preview?.Dispose();
                return;
            }

            ReplacePreviewImage(preview);
            if (preview != null)
            {
                PlaceholderImage = null;
            }

            // Request zoom-to-fit after image loads
            RequestZoomFit?.Invoke();

            // Schedule histogram calculation after preview is displayed
            ScheduleHistogramUpdate();
        }
        catch (OperationCanceledException)
        {
            // Preview loading was cancelled, ignore
            if (ReferenceEquals(_previewLoadingCts, requestCts))
            {
                PlaceholderImage = null;
            }
        }
        finally
        {
            loadingIndicatorCts.Cancel();
            loadingIndicatorCts.Dispose();

            if (ReferenceEquals(_previewLoadingCts, requestCts))
            {
                _previewLoadingCts = null;
                IsLoadingPreview = false;
            }

            requestCts.Dispose();
        }
    }

    private async Task ShowLoadingIndicatorAfterDelay(
        int delayMs,
        CancellationTokenSource requestCts,
        CancellationToken ct)
    {
        try
        {
            await Task.Delay(delayMs, ct);
            if (!ct.IsCancellationRequested && ReferenceEquals(_previewLoadingCts, requestCts))
                IsLoadingPreview = true;
        }
        catch (OperationCanceledException)
        {
            // Delay was cancelled, don't show indicator
        }
    }

    private void ReplacePreviewImage(Bitmap? preview)
    {
        if (ReferenceEquals(PreviewImage, preview))
            return;

        var previous = PreviewImage;
        PreviewImage = preview;
        previous?.Dispose();
    }
}
