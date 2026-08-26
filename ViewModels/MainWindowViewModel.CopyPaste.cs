using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HappyPhoton.Models;
using HappyPhoton.Services;

namespace HappyPhoton.ViewModels;

public partial class MainWindowViewModel
{
    private EditSettings? _copiedSettings;
    private CancellationTokenSource? _transientStatusCts;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PasteEditSettingsCommand))]
    private bool _hasCopiedSettings;

    [ObservableProperty]
    private string? _transientStatus;

    [ObservableProperty]
    private string? _pinnedStatus;

    public string? StatusMessage =>
        PinnedStatus ??
        PreviewSourceFailureStatus ??
        SelectedRawDecodeFailureStatus ??
        GlobalRawRuntimeFailureStatus ??
        TransientStatus;

    public Func<int, Task<bool>>? ConfirmBatchApplyAsync { get; set; }

    private bool CanCopyEditSettings =>
        CanEditSelectedImage && !IsFullScreenMode;

    [RelayCommand(CanExecute = nameof(CanCopyEditSettings))]
    private void CopyEditSettings()
    {
        if (SelectedImage == null) return;

        var liveSettings = SelectedImage.EditSettings.Clone();
        SaveSlidersTo(liveSettings);
        _copiedSettings = EditSettingsTransfer.CopySubset(liveSettings);
        HasCopiedSettings = true;
        ShowTransientStatus("Copied edit settings");
    }

    private bool CanPasteEditSettings
    {
        get
        {
            if (!HasCopiedSettings || IsFullScreenMode) return false;
            var targets = ResolveActionTargets().Targets;
            return targets.Count > 0 &&
                   targets.All(target => !target.SourceRequiresHydration);
        }
    }

    partial void OnSelectedCountChanged(int value)
    {
        PasteEditSettingsCommand.NotifyCanExecuteChanged();
        NotifyCompareGateChanged();
    }

    partial void OnWorkspaceModeChanged(
        WorkspaceMode oldValue,
        WorkspaceMode newValue) =>
        PasteEditSettingsCommand.NotifyCanExecuteChanged();

    [RelayCommand(CanExecute = nameof(CanPasteEditSettings))]
    private async Task PasteEditSettingsAsync()
    {
        if (_copiedSettings == null) return;

        var resolution = ResolveActionTargets();
        if (resolution.Targets.Count == 0) return;
        if (resolution.Targets.Any(target => target.SourceRequiresHydration))
        {
            ShowTransientStatus(
                "Download online-only originals before applying edit settings");
            return;
        }

        if (resolution.IsBrowseSelection)
        {
            await PasteToSelectionAsync(resolution.Targets);
            return;
        }

        await PasteToCurrentImageAsync();
    }

    private async Task PasteToCurrentImageAsync()
    {
        var selectedImage = SelectedImage;
        if (selectedImage == null || _copiedSettings == null) return;
        var previousSettings = CaptureLiveEditState();
        var previousIntent = _requestedPreviewIntent;
        var surfaceGeneration = RequestEditedRender();

        PushLiveUndoState();
        var currentRotation = Rotation;
        var currentHorizonRotation = HorizonRotation;
        var currentCrop = CurrentCrop?.Clone();
        var currentGeometry = previousSettings.Geometry?.Clone();
        var storedRotation = selectedImage.EditSettings.Rotation;
        var storedHorizonRotation = selectedImage.EditSettings.HorizonRotation;
        var storedCrop = selectedImage.EditSettings.Crop?.Clone();

        _isLoadingImage = true;
        try
        {
            LoadSlidersFrom(_copiedSettings);
            Rotation = currentRotation;
            HorizonRotation = currentHorizonRotation;
            LoadGeometryFrom(previousSettings);
            CurrentCrop = currentCrop;
        }
        finally
        {
            _isLoadingImage = false;
        }

        EditSettingsTransfer.ApplySubset(_copiedSettings, selectedImage.EditSettings);
        selectedImage.EditSettings.Rotation = storedRotation;
        selectedImage.EditSettings.HorizonRotation = storedHorizonRotation;
        selectedImage.EditSettings.Crop = storedCrop;
        selectedImage.EditSettings.Geometry = currentGeometry;
        LoadCurrentCurveFrom(selectedImage.EditSettings);
        selectedImage.HasEdits = selectedImage.EditSettings.HasEdits;
        try
        {
            await SaveEditSettingsAsync(selectedImage);
        }
        catch
        {
            RollbackEditReservation(
                selectedImage,
                previousSettings,
                surfaceGeneration,
                previousIntent);
            throw;
        }

        if (ReferenceEquals(SelectedImage, selectedImage))
        {
            _lastSavedState = selectedImage.EditSettings.Clone();
            if (IsDevelopMode || IsFullScreenMode)
            {
                await UpdatePreviewWithCurrentSliders(
                    generation: surfaceGeneration);
            }
            UpdateCanReset();
        }

        _ = TrackDirectThumbnailOperation(
            RefreshThumbnailAsync(selectedImage));
        ShowTransientStatus("Pasted edit settings");
    }

    private void PushLiveUndoState()
    {
        // Paste owns one explicit pre-apply history snapshot.
        _history.PushEdit(CaptureLiveEditState(), dedup: false);
        SyncHistoryFlags();
    }

    private EditSettings CaptureLiveEditState()
    {
        var liveState = SelectedImage!.EditSettings.Clone();
        SaveSlidersTo(liveState);
        return liveState;
    }

    private async Task PasteToSelectionAsync(IReadOnlyList<ImageFile> targets)
    {
        if (_copiedSettings == null) return;

        if (targets.Count == 0 || ConfirmBatchApplyAsync == null) return;
        if (!await ConfirmBatchApplyAsync(targets.Count)) return;

        List<(ImageFile Target, EditSettings Previous, EditSettings Settings)> proposed;

        try
        {
            foreach (var target in targets)
            {
                await target.EnsureCatalogIdAsync(_catalogService);
            }

            proposed = targets.Select(target =>
            {
                var previous = target.EditSettings.Clone();
                var settings = target.EditSettings.Clone();
                EditSettingsTransfer.ApplySubset(_copiedSettings, settings);
                return (Target: target, Previous: previous, Settings: settings);
            }).ToList();
            await _catalogService.SaveEditSettingsBatchAsync(proposed
                .Select(update => new CatalogEditSettingsUpdate(
                    update.Target.CatalogId,
                    update.Settings))
                .ToList());

            foreach (var update in proposed)
            {
                update.Target.EditSettings = update.Settings;
                update.Target.HasEdits = update.Settings.HasEdits;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Batch paste failed: {ex.Message}");
            ShowTransientStatus("Unable to apply edit settings");
            return;
        }

        long? surfaceGeneration = null;
        if (SelectedImage != null && targets.Contains(SelectedImage))
        {
            surfaceGeneration = RequestEditedRender();
            _history.Clear();
            SyncHistoryFlags();

            _isLoadingImage = true;
            try
            {
                LoadSlidersFrom(SelectedImage.EditSettings);
            }
            finally
            {
                _isLoadingImage = false;
            }

            _lastSavedState = SelectedImage.EditSettings.Clone();
            if (IsDevelopMode || IsFullScreenMode)
            {
                await UpdatePreviewWithCurrentSliders(
                    generation: surfaceGeneration);
            }
            UpdateCanReset();
        }

        _ = TrackDirectThumbnailOperation(
            RefreshThumbnailsAsync(proposed.Select(update =>
                (update.Target, update.Previous))));

        var applied = targets.Count;
        var noun = applied == 1 ? "image" : "images";
        ShowTransientStatus($"Applied to {applied} {noun}");
    }

    private async Task RefreshThumbnailsAsync(IEnumerable<ImageFile> images)
    {
        var targets = images.ToList();
        var nextIndex = -1;
        var workerCount = Math.Min(ThumbnailConcurrency, targets.Count);
        var workers = Enumerable.Range(0, workerCount).Select(async _ =>
        {
            while (true)
            {
                var index = Interlocked.Increment(ref nextIndex);
                if (index >= targets.Count) return;
                await RefreshThumbnailAsync(targets[index]);
            }
        });
        await Task.WhenAll(workers);
        QueueRequestedThumbnailRange();
    }

    private Task RefreshThumbnailsAsync(
        IEnumerable<(ImageFile Image, EditSettings Previous)> changes) =>
        RefreshThumbnailsAsync(changes
            .Where(change => ShouldRefreshThumbnail(
                change.Image,
                change.Previous))
            .Select(change => change.Image));

    private bool ShouldRefreshThumbnail(
        ImageFile image,
        EditSettings previous)
    {
        if (!image.IsRaw) return true;
        if (ReferenceEquals(image, SelectedImage) &&
            (IsDevelopMode || IsFullScreenMode)) return true;
        if (!GeometryMatches(previous, image.EditSettings)) return true;
        return ImageService.Thumbnails.HasRenderedCacheEntry(image);
    }

    private static bool GeometryMatches(EditSettings left, EditSettings right) =>
        left.Rotation == right.Rotation &&
        left.HorizonRotation == right.HorizonRotation &&
        GeometrySettingsMatch(left.Geometry, right.Geometry) &&
        CropMatches(left.Crop, right.Crop);

    private static bool GeometrySettingsMatch(
        GeometrySettings? left,
        GeometrySettings? right) =>
        (left?.Vertical ?? 0) == (right?.Vertical ?? 0) &&
        (left?.Horizontal ?? 0) == (right?.Horizontal ?? 0) &&
        (left?.Aspect ?? 0) == (right?.Aspect ?? 0) &&
        (left?.Distortion ?? 0) == (right?.Distortion ?? 0);

    private static bool CropMatches(CropRegion? left, CropRegion? right)
    {
        if (left == null || left.IsFullImage)
            return right == null || right.IsFullImage;
        return right != null &&
            left.Left == right.Left &&
            left.Top == right.Top &&
            left.Right == right.Right &&
            left.Bottom == right.Bottom;
    }

    private async Task RefreshThumbnailAsync(ImageFile image)
    {
        var sizeGeneration = Volatile.Read(ref _thumbnailSizeGeneration);
        try
        {
            using var result = await ImageService.LoadThumbnailAsync(
                image,
                BrowseThumbnailRequest,
                CancellationToken.None);
            if (!Browse.Contains(image) ||
                sizeGeneration != Volatile.Read(ref _thumbnailSizeGeneration))
            {
                return;
            }

            ApplyThumbnailLoadResult(image, result);
            if (result.Status == ThumbnailLoadStatus.Loaded)
            {
                Browse.ReplaceThumbnail(image, result.DetachBitmap());
                UpdateThumbnailMemoryDiagnostics();
            }
        }
        catch (Exception ex)
        {
            if (Browse.Contains(image)) image.ThumbnailLoadFailed = true;
            System.Diagnostics.Debug.WriteLine(
                $"Thumbnail refresh failed for {image.FilePath}: {ex.Message}");
        }
    }

    private void ShowTransientStatus(string text)
    {
        TransientStatus = text;
        var debounce = ReplaceDebounce(ref _transientStatusCts);
        _ = DebouncedAction.RunAsync(
            "transient status",
            TimeSpan.FromSeconds(3),
            debounce.Token,
            () =>
        {
            TransientStatus = null;
            return Task.CompletedTask;
        });
    }

    private void ShowPinnedStatus(string text) => PinnedStatus = text;

    private void ClearPinnedStatus(string text)
    {
        if (PinnedStatus == text)
        {
            PinnedStatus = null;
        }
    }

    partial void OnTransientStatusChanged(string? value) =>
        OnPropertyChanged(nameof(StatusMessage));

    partial void OnPinnedStatusChanged(string? value) =>
        OnPropertyChanged(nameof(StatusMessage));
}
