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
    private readonly Action<Action> _postSelection;
    private long _activeBaseRefreshRequestId;

    public async Task<int> LoadFolderAsync(string folderPath)
    {
        CancelAdjacentPreviewWarm(true, dropRetained: true);
        var generation = Interlocked.Increment(ref _browseGeneration);
        await CancelBrowseSelectionSummaryAsync();
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
            var sourceFiles = folderContents.scan.Images.ToList();
            _xmpIndexedSidecars = folderContents.scan.SidecarPaths;
            CurrentFolderHasSubfolders = folderContents.hasSubfolders;
            var imagePaths = sourceFiles.Select(image => image.FilePath).ToArray();
            // Microsoft.Data.Sqlite async APIs can perform synchronous disk work.
            var catalogStates = await Task.Run(
                () => _catalogService.LoadOrCreateImageStatesAsync(
                    imagePaths, cancellationToken),
                cancellationToken);
            var imageFiles = new List<ImageFile>();
            foreach (var source in sourceFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var versions = catalogStates[source.FilePath];
                foreach (var state in versions)
                {
                    var imageFile = state.Version == 1
                        ? source
                        : new ImageFile(
                            source.FilePath, source.SourceAvailabilityHint);
                    ApplyCatalogState(imageFile, state, versions.Count);
                    imageFiles.Add(imageFile);
                }
            }

            cancellationToken.ThrowIfCancellationRequested();

            // Fresh ImageFile instances replace the old burst indicators immediately.
            ResetBurstState();
            ResetThumbnailViewport();
            RecomputeCapturePairs(imageFiles);
            Browse.SetImages(imageFiles);

            // Defer first image selection until after UI settles.
            if (Browse.VisibleImages.Count > 0)
            {
                _postSelection(() =>
                {
                    if (!cancellationToken.IsCancellationRequested &&
                        Browse.VisibleImages.Count > 0 && SelectedImage == null)
                    {
                        SelectedImage = Browse.FirstVisible();
                    }
                });
            }

            pumpStarted = true;
            StartThumbnailSession(
                Browse.VisibleImages.ToList(), imageFiles, requestCts, generation);
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
                Browse.SetImages(Array.Empty<ImageFile>());
                ShowTransientStatus($"Unable to load folder: {ex.Message}");
            }
            return 0;
        }
    }

    private async Task LoadPreviewAsync(
        ImageFile imageFile,
        long surfaceGeneration)
    {
        using var previewActivity = BeginInitialPreviewActivity();
        _previewLoadingCts?.Cancel();
        // Ordinary entry loads declare fit intent. A representation switch
        // carries its viewport until the first accepted paint instead.
        if (!PrepareCaptureMemberViewport(imageFile)) IsZoomFitMode = true;
        var requestCts = new CancellationTokenSource();
        _previewLoadingCts = requestCts;
        var ct = requestCts.Token;

        try
        {
            var intent = _requestedPreviewIntent;
            var renderSettings = intent == PreviewSurfaceIntent.Original
                // A preview load stands the stored image back up, so its frame
                // comes from the file rather than from live edit state.
                ? BuildOriginalRenderSettings(
                    imageFile,
                    imageFile.EditSettings.Rotation,
                    imageFile.EditSettings.HorizonRotation,
                    imageFile.EditSettings.Crop?.Clone())
                : imageFile.EditSettings;
            if (IsExportMode && ExportSettings.ShowProof)
            {
                var proofPainted = await LoadExportProofAsync(
                    imageFile,
                    renderSettings,
                    surfaceGeneration,
                    ct);
                if (proofPainted)
                {
                    RefreshSourceAvailability(imageFile);
                    if (IsZoomFitMode) RequestZoomFit?.Invoke();
                }
                return;
            }
            var cachedIdentity = RenderSettingsHash.Compute(renderSettings);
            var cachedTask = ImageService.Previews.LoadCachedPreviewAsync(
                imageFile,
                renderSettings,
                ct);
            var freshTask = ImageService.Previews.LoadPreviewArtifactsAsync(
                imageFile,
                renderSettings,
                BrowseThumbnailRequest,
                skipHistogram: false,
                RequestedClippingOverlaySides,
                ct,
                surfaceGeneration,
                computeWaveform: true);

            var firstCompleted = await Task.WhenAny(cachedTask, freshTask);
            if (ReferenceEquals(firstCompleted, cachedTask))
            {
                var cached = await cachedTask;
                if (cached != null && IsCurrentPreviewRequest(imageFile, requestCts))
                {
                    var cachedAccepted = ApplyRenderOutcome(RenderOutcome.Cached(
                        imageFile,
                        surfaceGeneration,
                        cached,
                        cachedIdentity));
                    if (cachedAccepted)
                        RestoreCaptureMemberViewportAfterPaint(imageFile);
                }
                cached?.Dispose();
            }

            using var artifacts = await freshTask;
            if (!IsCurrentPreviewRequest(imageFile, requestCts))
            {
                if (!cachedTask.IsCompleted)
                {
                    _ = DisposeCachedPreviewWhenReadyAsync(cachedTask);
                }
                return;
            }
            RefreshSourceAvailability(imageFile);

            var succeeded = artifacts.Bitmap != null;
            var accepted = ApplyRenderOutcome(RenderOutcome.FromArtifacts(
                imageFile,
                surfaceGeneration,
                intent,
                RenderOutcomeClass.StateDefining,
                PreviewPaintSource.FreshRender,
                artifacts,
                promotable: true));
            var painted = succeeded && accepted;
            if (painted) RestoreCaptureMemberViewportAfterPaint(imageFile);
            if (painted && intent == PreviewSurfaceIntent.Edited)
            {
                _lastAppliedEditSettings = imageFile.EditSettings.Clone();
            }

            // The entry refit only applies while the user hasn't taken manual
            // zoom control during the load window — their zoom wins over the
            // default fit ("snaps back after render" defect).
            if (painted && IsZoomFitMode)
            {
                RequestZoomFit?.Invoke();
            }

            if (!ReferenceEquals(firstCompleted, cachedTask))
            {
                using var cached = await cachedTask;
                if (cached != null &&
                    IsCurrentPreviewRequest(imageFile, requestCts))
                {
                    ApplyRenderOutcome(RenderOutcome.Cached(
                        imageFile,
                        surfaceGeneration,
                        cached,
                        cachedIdentity));
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (ReferenceEquals(_previewLoadingCts, requestCts))
            {
                _previewLoadingCts = null;
            }

            requestCts.Dispose();
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
        PreviewPaintSource source,
        bool isProof = false)
    {
        ArgumentNullException.ThrowIfNull(preview);
        if (ReferenceEquals(PreviewImage, preview)) return;

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
        SetProofDisplayed(isProof);
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
        SetProofDisplayed(false);
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
        var outcome = RenderOutcome.FromRefresh(
            refresh,
            refresh.DetachBitmap(),
            refresh.DetachClippingMask(),
            refresh.DetachPromotionLease(),
            PreviewSurfaceIntent.Edited);
        Dispatcher.UIThread.Post(() =>
        {
            // Requested intent belongs to the UI thread. A refresh preserves
            // whatever intent is current when its outcome is actually applied.
            outcome.Intent = _requestedPreviewIntent;
            var image = outcome.Image;
            ApplyRenderOutcome(outcome);
            if (image != null)
            {
                _ = TrackDirectThumbnailOperation(RefreshThumbnailAsync(image));
            }
        });
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
        ClippingMask? clippingMask = null,
        bool isMonochrome = false)
    {
        using var refresh = new PreviewRefresh(
            imageFile,
            bitmap,
            histogram,
            hasHistogram,
            generation,
            rawHistogram,
            clipping,
            isRawSource ?? imageFile.IsRaw,
            profileState,
            clippingMask,
            isMonochrome: isMonochrome);
        ApplyRenderOutcome(RenderOutcome.FromRefresh(
            refresh,
            refresh.DetachBitmap(),
            refresh.DetachClippingMask(),
            promotionLease: null,
            _requestedPreviewIntent));
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
            if (IsWorkspacePreviewSurfaceActive || IsFullScreenMode)
            {
                ApplyRenderOutcome(new RenderOutcome
                {
                    Image = state.ImageFile,
                    Generation = Volatile.Read(
                        ref _latestPreviewOutcomeGeneration),
                    Class = RenderOutcomeClass.StateDefining,
                    Intent = _requestedPreviewIntent,
                    ClippingMode = OutcomeFieldMode.Clear,
                    RawHistogramMode = OutcomeFieldMode.Clear
                });
            }
            return;
        }

        if (Volatile.Read(ref _activeBaseRefreshRequestId) ==
            state.RequestId)
        {
            Volatile.Write(ref _activeBaseRefreshRequestId, 0);
            NotifyRawHistogramState();
        }
    }
}
