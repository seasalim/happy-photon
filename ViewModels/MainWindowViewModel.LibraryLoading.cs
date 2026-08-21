using Avalonia.Threading;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.Input;
using HappyPhoton.Models;
using HappyPhoton.Services;

namespace HappyPhoton.ViewModels;

internal enum PreviewPaintSource
{
    CachedJpeg,
    FreshRender,
    BackgroundRefresh,
    RestingRender
}

public partial class MainWindowViewModel
{
    private static readonly TimeSpan BaseArmingDelay =
        TimeSpan.FromMilliseconds(150);
    private readonly Action<Action> _postSelection;
    private long _activeBaseRefreshRequestId;

    public async Task<int> LoadFolderAsync(string folderPath)
    {
        var generation = Interlocked.Increment(ref _libraryGeneration);
        await CancelLibrarySelectionSummaryAsync();
        await CancelXmpReconcileAsync();
        _xmpIndexedSidecars = [];
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
        SetRawHistogram(null);

        CurrentFolderPath = folderPath;
        CurrentFolderHasSubfolders = false;
        SelectedImage = null;
        var pumpStarted = false;

        try
        {
            var folderContents = await Task.Run(
                () => (
                    scan: _folderService.ScanFolder(folderPath),
                    hasSubfolders: _folderTreeService.HasSubfolders(folderPath)),
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            var imageFiles = folderContents.scan.Images.ToList();
            _xmpIndexedSidecars = folderContents.scan.SidecarPaths;
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
                imageFile.ColorLabel = state.ColorLabel;
                imageFile.AssessmentRevision = state.AssessmentRevision;
                imageFile.AssessedUtc = state.AssessedUtc;
                imageFile.PendingAssessmentAxes = state.PendingAxes;
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
            await StartXmpReconcileAsync(generation);
            ReportPendingXmpAssessments(imageFiles);
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

    private async Task LoadPreviewAsync(
        ImageFile imageFile,
        bool wakeActivity = true)
    {
        if (wakeActivity) SignalBackgroundActivityStarted();
        _previewLoadingCts?.Cancel();
        SetRawHistogram(null);
        ClearPreviewClippingArtifacts();
        // Every entry load declares fit intent; a manual zoom during the load
        // window flips it and then wins over the entry refit below.
        IsZoomFitMode = true;
        var requestCts = new CancellationTokenSource();
        _previewLoadingCts = requestCts;
        var ct = requestCts.Token;
        var armingIndicatorCts = new CancellationTokenSource();

        try
        {
            var cachedTask = ImageService.Previews.LoadCachedPreviewAsync(
                imageFile,
                imageFile.EditSettings,
                ct);
            var freshTask = ImageService.Previews.LoadPreviewArtifactsAsync(
                imageFile,
                imageFile.EditSettings,
                LibraryThumbnailRequest,
                skipHistogram: true,
                RequestedClippingOverlaySides,
                ct);
            ClearPreviewImage();
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
                    ReplacePreviewImage(
                        cached.DetachBitmap(),
                        PreviewPaintSource.CachedJpeg);
                }
                cached?.Dispose();
            }

            using var artifacts = await freshTask;
            var preview = artifacts.DetachBitmap();
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
                ReconcileHighlightReconstructionCapability(
                    imageFile,
                    artifacts.IsRawSource);
                ApplyRawProfileState(
                    imageFile,
                    artifacts.IsRawSource,
                    artifacts.ProfileState);
                InstallPreviewClipping(artifacts);
                ReplacePreviewImage(preview, PreviewPaintSource.FreshRender);
            }

            // The entry refit only applies while the user hasn't taken manual
            // zoom control during the load window — their zoom wins over the
            // default fit ("snaps back after render" defect).
            if (IsZoomFitMode)
            {
                RequestZoomFit?.Invoke();
            }
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
                    ReplacePreviewImage(
                        cached.DetachBitmap(),
                        PreviewPaintSource.CachedJpeg);
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
            await Task.Delay(BaseArmingDelay, _timeProvider, ct);
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

    internal void ReplacePreviewImage(
        Bitmap preview,
        PreviewPaintSource source)
    {
        ArgumentNullException.ThrowIfNull(preview);
        if (ReferenceEquals(PreviewImage, preview))
            return;

        if (ImageServiceHelpers.DisplayTraceLoggingEnabled)
        {
            var identity =
                ImageService.Previews.TryGetPreviewRenderIdentity(preview);
            ImageServiceHelpers.LogDisplayTrace(
                $"paint source={PaintSourceLabel(source)} " +
                $"bitmap={preview.PixelSize.Width}x{preview.PixelSize.Height} " +
                $"luma={BitmapConversionService.EstimateMeanLuma(preview):F4} " +
                $"decode={identity?.DecodeKey ?? "none"} " +
                $"settings={identity?.SettingsHash ?? "none"}");
        }
        if (source is PreviewPaintSource.FreshRender or
            PreviewPaintSource.BackgroundRefresh)
        {
            UpdateOriginalViewPixelSize(preview);
        }
        var previous = PreviewImage;
        PreviewImage = preview;
        if (previous != null)
        {
            _bitmapRetirement.Retire(
                previous,
                () => ReferenceEquals(PreviewImage, previous));
        }
    }

    internal void ClearPreviewImage()
    {
        var previous = PreviewImage;
        if (previous == null) return;
        PreviewImage = null;
        _bitmapRetirement.Retire(
            previous,
            () => ReferenceEquals(PreviewImage, previous));
    }

    private static string PaintSourceLabel(PreviewPaintSource source) =>
        source switch
        {
            PreviewPaintSource.CachedJpeg => "cached-jpeg",
            PreviewPaintSource.FreshRender => "fresh-render",
            PreviewPaintSource.BackgroundRefresh => "background-refresh",
            PreviewPaintSource.RestingRender => "resting-render",
            _ => throw new ArgumentOutOfRangeException(nameof(source))
        };

    private void RetireThumbnail(ImageFile image, Bitmap thumbnail) =>
        _bitmapRetirement.Retire(
            thumbnail,
            () => ReferenceEquals(image.Thumbnail, thumbnail));

    private void OnPreviewRefreshed(object? sender, PreviewRefresh refresh)
    {
        var bitmap = refresh.DetachBitmap();
        var clippingMask = refresh.DetachClippingMask();
        var imageFile = refresh.ImageFile;
        var histogram = refresh.Histogram;
        var rawHistogram = refresh.RawHistogram;
        var hasHistogram = refresh.HasHistogram;
        var generation = refresh.Generation;
        var profileState = refresh.ProfileState;
        Dispatcher.UIThread.Post(() => ApplyPreviewRefresh(
            imageFile,
            bitmap,
            histogram,
            hasHistogram,
            rawHistogram,
            generation,
            refresh.Clipping,
            refresh.IsRawSource,
            profileState,
            clippingMask));
    }

    internal void ApplyPreviewRefresh(
        ImageFile imageFile,
        Bitmap bitmap,
        HistogramData histogram,
        bool hasHistogram,
        HistogramData? rawHistogram,
        long generation,
        ClippingStats? clipping = null,
        bool? isRawSource = null,
        DcpProfileState? profileState = null,
        ClippingMask? clippingMask = null)
    {
        // A refresh can settle after its ready gate while a newer render
        // generation has already been applied. Reject the stale bitmap the same
        // way the main preview path rejects superseded load outcomes.
        if (!ReferenceEquals(SelectedImage, imageFile) ||
            generation < Volatile.Read(ref _latestPreviewOutcomeGeneration))
        {
            bitmap.Dispose();
            clippingMask?.Dispose();
            return;
        }

        if (!IsDevelopMode && !IsFullScreenMode)
        {
            bitmap.Dispose();
            clippingMask?.Dispose();
            ClearPreviewClippingArtifacts();
            _ = TrackDirectThumbnailOperation(
                RefreshThumbnailAsync(imageFile));
            return;
        }

        var effectiveIsRawSource = isRawSource ?? imageFile.IsRaw;
        if (isRawSource.HasValue)
        {
            ReconcileHighlightReconstructionCapability(imageFile, effectiveIsRawSource);
        }
        ApplyRawProfileState(imageFile, effectiveIsRawSource, profileState);
        using var artifacts = new PreviewArtifacts(
            null,
            histogram,
            clipping,
            effectiveIsRawSource,
            profileState,
            generation,
            clippingMask);
        InstallPreviewClipping(artifacts);
        ReplacePreviewImage(bitmap, PreviewPaintSource.BackgroundRefresh);
        if (hasHistogram)
        {
            Histogram = histogram;
            if (!IsCropMode && !IsShowingOriginal && !_isHoveringPreset)
            {
                OnAcceptedInteractivePreview(bitmap);
            }
        }
        SetRawHistogram(rawHistogram);
        _ = TrackDirectThumbnailOperation(
            RefreshThumbnailAsync(imageFile));
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
            if (IsDevelopMode || IsFullScreenMode)
                SetRawHistogram(null);
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
            NotifyRawHistogramState();
        }
    }

    private async Task ShowReplacementBaseArmingAfterDelay(
        ImageFile imageFile,
        long requestId)
    {
        await Task.Delay(
            BaseArmingDelay,
            _timeProvider,
            CancellationToken.None);
        if (Volatile.Read(ref _activeBaseRefreshRequestId) == requestId &&
            ReferenceEquals(SelectedImage, imageFile))
        {
            IsBaseArming = true;
        }
    }
}
