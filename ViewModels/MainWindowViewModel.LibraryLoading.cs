using Avalonia.Threading;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.Input;
using HappyPhoton.Models;
using HappyPhoton.Services;

namespace HappyPhoton.ViewModels;

public partial class MainWindowViewModel
{
    private static readonly TimeSpan BaseArmingDelay =
        TimeSpan.FromMilliseconds(150);
    private readonly Action<Action> _postSelection;
    private long _activeBaseRefreshRequestId;

    public async Task<int> LoadFolderAsync(string folderPath)
    {
        var generation = Interlocked.Increment(ref _libraryGeneration);
        CancelSourceHydration();
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
        CurrentFolderHasSubfolders = false;
        SelectedImage = null;
        var pumpStarted = false;

        try
        {
            var folderContents = await Task.Run(
                () => (
                    images: _folderService.GetImagesInFolder(folderPath).ToList(),
                    hasSubfolders: _folderTreeService.HasSubfolders(folderPath)),
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            var imageFiles = folderContents.images;
            CurrentFolderHasSubfolders = folderContents.hasSubfolders;
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
            InitializeCloudSourceCount(imageFiles);

            // Defer first image selection until after UI settles.
            if (Library.VisibleImages.Count > 0)
            {
                _postSelection(() =>
                {
                    if (!cancellationToken.IsCancellationRequested &&
                        Library.VisibleImages.Count > 0 && SelectedImage == null)
                    {
                        SelectedImage = Library.FirstVisible();
                    }
                });
            }

            pumpStarted = true;
            StartThumbnailSession(imageFiles, requestCts, generation);
            StartBurstAnalysisIfRequested();
            return generation;
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
            return 0;
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
                CurrentFolderHasSubfolders = false;
                ResetBurstState();
                Library.SetImages(Array.Empty<ImageFile>());
                InitializeCloudSourceCount(Array.Empty<ImageFile>());
                ShowTransientStatus($"Unable to load folder: {ex.Message}");
            }
            return 0;
        }
    }

    private async Task LoadPreviewAsync(ImageFile imageFile)
    {
        _previewLoadingCts?.Cancel();
        var requestCts = new CancellationTokenSource();
        _previewLoadingCts = requestCts;
        var ct = requestCts.Token;
        var armingIndicatorCts = new CancellationTokenSource();

        try
        {
            var cachedTask = ImageService.LoadCachedPreviewAsync(
                imageFile,
                imageFile.EditSettings,
                ct);
            var freshTask = ImageService.LoadPreviewWithHistogramAsync(
                imageFile, imageFile.EditSettings, skipHistogram: true, ct);
            ReplacePreviewImage(null);
            _ = ShowBaseArmingAfterDelay(
                requestCts,
                freshTask,
                armingIndicatorCts.Token);

            var firstCompleted = await Task.WhenAny(cachedTask, freshTask);
            if (ReferenceEquals(firstCompleted, cachedTask))
            {
                var cached = await cachedTask;
                if (cached != null && IsCurrentPreviewRequest(imageFile, requestCts))
                {
                    ReplacePreviewImage(cached.DetachBitmap());
                }
                cached?.Dispose();
            }

            var (preview, _) = await freshTask;
            if (!IsCurrentPreviewRequest(imageFile, requestCts))
            {
                preview?.Dispose();
                if (!cachedTask.IsCompleted)
                {
                    _ = DisposeCachedPreviewWhenReadyAsync(cachedTask);
                }
                return;
            }
            IsBaseArming = false;
            RefreshSourceAvailability(imageFile);

            if (preview != null)
            {
                ReplacePreviewImage(preview);
            }

            RequestZoomFit?.Invoke();
            if (imageFile.SourceRequiresHydration)
            {
                Histogram = null;
            }
            else
            {
                ScheduleHistogramUpdate();
                await RefreshWhiteBalanceContextAsync(imageFile, ct);
            }

            if (!ReferenceEquals(firstCompleted, cachedTask))
            {
                using var cached = await cachedTask;
                if (preview == null &&
                    cached != null &&
                    IsCurrentPreviewRequest(imageFile, requestCts))
                {
                    ReplacePreviewImage(cached.DetachBitmap());
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            armingIndicatorCts.Cancel();
            armingIndicatorCts.Dispose();

            if (ReferenceEquals(_previewLoadingCts, requestCts))
            {
                _previewLoadingCts = null;
                IsBaseArming = false;
            }

            requestCts.Dispose();
        }
    }

    private async Task ShowBaseArmingAfterDelay(
        CancellationTokenSource requestCts,
        Task freshPreview,
        CancellationToken ct)
    {
        try
        {
            await Task.Delay(BaseArmingDelay, ct);
            if (!freshPreview.IsCompleted &&
                !ct.IsCancellationRequested &&
                ReferenceEquals(_previewLoadingCts, requestCts))
            {
                IsBaseArming = true;
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private bool IsCurrentPreviewRequest(
        ImageFile imageFile,
        CancellationTokenSource requestCts) =>
        !requestCts.IsCancellationRequested &&
        ReferenceEquals(_previewLoadingCts, requestCts) &&
        ReferenceEquals(SelectedImage, imageFile);

    private static async Task DisposeCachedPreviewWhenReadyAsync(
        Task<CachedPreviewBitmap?> cachedTask)
    {
        try
        {
            using var cached = await cachedTask;
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
        }
    }

    internal void ReplacePreviewImage(Bitmap? preview)
    {
        if (ReferenceEquals(PreviewImage, preview))
            return;

        var previous = PreviewImage;
        PreviewImage = preview;
        if (previous != null)
        {
            _bitmapRetirement.Retire(
                previous,
                () => ReferenceEquals(PreviewImage, previous));
        }
    }

    private void RetireThumbnail(ImageFile image, Bitmap thumbnail) =>
        _bitmapRetirement.Retire(
            thumbnail,
            () => ReferenceEquals(image.Thumbnail, thumbnail));

    private void OnPreviewRefreshed(object? sender, PreviewRefresh refresh)
    {
        var bitmap = refresh.DetachBitmap();
        var imageFile = refresh.ImageFile;
        var histogram = refresh.Histogram;
        var hasHistogram = refresh.HasHistogram;
        Dispatcher.UIThread.Post(() =>
        {
            if (!ReferenceEquals(SelectedImage, imageFile))
            {
                bitmap.Dispose();
                return;
            }

            ReplacePreviewImage(bitmap);
            if (hasHistogram)
            {
                Histogram = histogram;
            }
            _ = RefreshThumbnailAsync(imageFile);
        });
    }

    private void OnBaseRefreshStateChanged(
        object? sender,
        PreviewBaseRefreshState state) =>
        Dispatcher.UIThread.Post(() => ApplyBaseRefreshState(state));

    internal void ApplyBaseRefreshState(PreviewBaseRefreshState state)
    {
        if (!ReferenceEquals(SelectedImage, state.ImageFile))
        {
            return;
        }

        if (state.IsRefreshing)
        {
            Volatile.Write(
                ref _activeBaseRefreshRequestId,
                state.RequestId);
            _ = ShowReplacementBaseArmingAfterDelay(
                state.ImageFile,
                state.RequestId);
            return;
        }

        if (Volatile.Read(ref _activeBaseRefreshRequestId) ==
            state.RequestId)
        {
            Volatile.Write(ref _activeBaseRefreshRequestId, 0);
            IsBaseArming = false;
        }
    }

    private async Task ShowReplacementBaseArmingAfterDelay(
        ImageFile imageFile,
        long requestId)
    {
        await Task.Delay(BaseArmingDelay);
        if (Volatile.Read(ref _activeBaseRefreshRequestId) == requestId &&
            ReferenceEquals(SelectedImage, imageFile))
        {
            IsBaseArming = true;
        }
    }
}
