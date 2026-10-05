using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HappyPhoton.Models;
using HappyPhoton.Services;

namespace HappyPhoton.ViewModels;

public partial class MainWindowViewModel
{
    private EditSettings? _copiedSettings;

    private string? _copiedSourceName;

    private CancellationTokenSource? _transientStatusCts;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PasteEditSettingsCommand))]
    [NotifyCanExecuteChangedFor(nameof(ChoosePasteSettingsCommand))]
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
        TransientStatus ?? _oneTimeNotice.Text;

    private bool CanCopyEditSettings =>
        CanEditSelectedImage && !IsFullScreenMode;

    [RelayCommand(CanExecute = nameof(CanCopyEditSettings))]
    private void CopyEditSettings()
    {
        if (SelectedImage == null) return;

        var snapshot = CapturePasteSnapshot(SelectedImage);
        _copiedSettings = snapshot.Settings;
        _copiedSource = snapshot.Source;
        _copiedSourceName = snapshot.SourceName;
        _copiedProfileSource = snapshot.Profiles;
        _copiedSpotSource = snapshot.Spots;
        HasCopiedSettings = true;
        ShowTransientStatus($"Copied settings from {_copiedSourceName}");
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
        ChoosePasteSettingsCommand.NotifyCanExecuteChanged();
        NotifySyncSettingsChanged();
        NotifyCompareGateChanged();
    }

    partial void OnWorkspaceModeChanged(
        WorkspaceMode oldValue,
        WorkspaceMode newValue)
    {
        PasteEditSettingsCommand.NotifyCanExecuteChanged();
        ChoosePasteSettingsCommand.NotifyCanExecuteChanged();
        NotifySyncSettingsChanged();
    }

    [RelayCommand(CanExecute = nameof(CanPasteEditSettings))]
    private Task PasteEditSettingsAsync() => PasteEditSettingsCoreAsync(CopiedPasteSnapshot(), showDialog: false);

    private async Task PasteEditSettingsCoreAsync(PasteSnapshot? snapshot, bool showDialog)
    {
        DiscardSpotsGesture();
        DiscardLocalsGesture();
        if (snapshot == null) return;

        var resolution = ResolveActionTargets();
        if (resolution.Targets.Count == 0) return;

        var workspaceMode = WorkspaceMode;
        var selectedImage = SelectedImage;
        var targets = resolution.Targets.ToArray();

        if (targets.Any(target => target.SourceRequiresHydration))
        {
            ShowTransientStatus(
                "Download online-only originals before applying edit settings");
            return;
        }

        if ((showDialog || workspaceMode == WorkspaceMode.Browse) &&
            !await ChoosePasteGroupsAsync(snapshot, targets, !resolution.IsBrowseSelection))
        {
            return;
        }

        if (WorkspaceMode != workspaceMode || !ReferenceEquals(SelectedImage, selectedImage)) return;

        if (targets.Any(target => _sourceAvailabilityService.GetAvailability(target.FilePath).IsOnlineOnly()))
        {
            ShowTransientStatus("Download online-only originals before applying edit settings");
            return;
        }

        var groups = RememberedPasteGroups;
        if (groups.Length == 0) return;

        if (workspaceMode == WorkspaceMode.Browse)
        {
            await PasteToSelectionAsync(snapshot, targets, groups);
            return;
        }

        await PasteToCurrentImageAsync(snapshot, targets[0], groups);
    }

    private async Task PasteToCurrentImageAsync(PasteSnapshot snapshot, ImageFile selectedImage,
        IReadOnlyCollection<EditSettingsGroup> groups)
    {
        ClearBatchOffer();
        var changesFrame = groups.Any(group => group.Name is "Crop & Straighten" or "Geometry");
        var previousSettings = CapturePasteState(selectedImage, changesFrame,
            groups.Any(group => group.Name == "Lens Profile"));

        PasteProposal proposal;

        try
        {
            proposal = await PreparePasteAsync(selectedImage, previousSettings, groups, snapshot);
            if (!ReferenceEquals(SelectedImage, selectedImage) || !IsDevelopMode) return;

            var current = CapturePasteState(selectedImage, changesFrame,
                groups.Any(group => group.Name == "Lens Profile"));

            if (EditSettingsJson.Serialize(current) != EditSettingsJson.Serialize(previousSettings))
            {
                previousSettings = current;
                proposal = PreparePaste(selectedImage, previousSettings, groups, snapshot, cachedOnly: true);
            }
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or NotSupportedException)
        {
            ShowTransientStatus("Settings unchanged (invalid settings)");
            _transientStatusCts?.Cancel();
            return;
        }

        if (!ReferenceEquals(SelectedImage, selectedImage) || !IsDevelopMode) return;

        var unchanged = EditSettingsJson.Serialize(proposal.Settings) == EditSettingsJson.Serialize(previousSettings);

        if (unchanged)
        {
            if (changesFrame || proposal.LensApplied && LensProfileOverride != proposal.Settings.Lens.ProfileOverride)
            {
                InstallDevelopDocument(selectedImage, proposal.Settings, preserveCropDraft: !changesFrame,
                    preserveLensDraft: !proposal.LensApplied);
                SchedulePreviewUpdate();
            }

            ReportPaste("Pasted settings", proposal.Reframed ? 1 : 0, proposal.Skips);
            return;
        }

        _previewDebounce?.Cancel();
        var previousIntent = _requestedPreviewIntent;
        var surfaceGeneration = RequestEditedRender();
        var settings = proposal.Settings;
        InstallDevelopDocument(selectedImage, settings, preserveCropDraft: !changesFrame,
            preserveLensDraft: !proposal.LensApplied);

        try
        {
            await SaveEditSettingsAsync(
                selectedImage, "Paste settings", previousSettings);
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

        if (!unchanged)
        {
            _ = TrackDirectThumbnailOperation(RefreshThumbnailAsync(selectedImage));
        }

        var replaced = groups.Where(group => group.Kind == EditSettingsGroupKind.PhotoSpecific &&
                PasteSettingsViewModel.HasOwn(previousSettings, group.Name) &&
                !proposal.Skips.ContainsKey(group.Name))
            .Select(group => group.Name == "Crop & Straighten" ? "Crop" : group.Name).ToArray();
        ReportPaste("Pasted settings" + (replaced.Length == 0 ? "" : $" · replaced {string.Join(", ", replaced)}"),
            proposal.Reframed ? 1 : 0, proposal.Skips);
    }

    private EditSettings CaptureLiveEditState()
    {
        var liveState = SelectedImage!.EditSettings.Clone();
        SaveSlidersTo(liveState);

        return liveState;
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
        var requestState = _thumbnailRequests.GetOrCreateValue(image);
        var requestGeneration = Interlocked.Increment(ref requestState.Value);
        try
        {
            using var result = await ImageService.LoadThumbnailAsync(
                image,
                BrowseThumbnailRequest,
                CancellationToken.None);
            if (requestGeneration != Volatile.Read(ref requestState.Value) ||
                !Browse.Contains(image) ||
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
            if (requestGeneration == Volatile.Read(ref requestState.Value) &&
                Browse.Contains(image)) image.ThumbnailLoadFailed = true;
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

    partial void OnTransientStatusChanged(string? value)
    {
        if (value != null) _oneTimeNotice.ClearPresented();

        OnPropertyChanged(nameof(StatusMessage));
    }

    partial void OnPinnedStatusChanged(string? value) =>
        OnPropertyChanged(nameof(StatusMessage));
}
