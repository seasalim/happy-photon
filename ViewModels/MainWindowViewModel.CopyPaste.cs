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
        TransientStatus ?? _backupNotice;

    private bool CanCopyEditSettings =>
        CanEditSelectedImage && !IsFullScreenMode;

    [RelayCommand(CanExecute = nameof(CanCopyEditSettings))]
    private void CopyEditSettings()
    {
        if (SelectedImage == null) return;

        var liveSettings = SelectedImage.EditSettings.Clone();
        SaveSlidersTo(liveSettings);
        _copiedSettings = liveSettings;
        _copiedSource = SelectedImage;
        _copiedSourceName = Path.GetFileName(SelectedImage.FilePath);
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
        NotifyCompareGateChanged();
    }

    partial void OnWorkspaceModeChanged(
        WorkspaceMode oldValue,
        WorkspaceMode newValue)
    {
        PasteEditSettingsCommand.NotifyCanExecuteChanged();
        ChoosePasteSettingsCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanPasteEditSettings))]
    private Task PasteEditSettingsAsync() => PasteEditSettingsCoreAsync(showDialog: false);

    private async Task PasteEditSettingsCoreAsync(bool showDialog)
    {
        DiscardSpotsGesture();
        DiscardLocalsGesture();
        if (_copiedSettings == null) return;

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
            !await ChoosePasteGroupsAsync(targets, !resolution.IsBrowseSelection))
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
            await PasteToSelectionAsync(targets, groups);
            return;
        }

        await PasteToCurrentImageAsync(targets[0], groups);
    }

    private async Task PasteToCurrentImageAsync(ImageFile selectedImage,
        IReadOnlyCollection<EditSettingsGroup> groups)
    {
        if (_copiedSettings == null) return;

        _previewDebounce?.Cancel();
        var changesFrame = groups.Any(group => group.Name is "Crop & Straighten" or "Geometry");
        var previousSettings = CapturePasteState(selectedImage, changesFrame);
        var snapshot = (_copiedSource!, _copiedSettings);

        PasteProposal proposal;

        try
        {
            proposal = await PreparePasteAsync(selectedImage, previousSettings, groups, snapshot);
            if (!ReferenceEquals(SelectedImage, selectedImage) || !IsDevelopMode) return;

            _previewDebounce?.Cancel();
            var current = CapturePasteState(selectedImage, changesFrame);

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

        var previousIntent = _requestedPreviewIntent;
        var surfaceGeneration = RequestEditedRender();
        var settings = proposal.Settings;
        InstallDevelopDocument(selectedImage, settings, preserveCropDraft: !changesFrame, preserveLensDraft: true);

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

        _ = TrackDirectThumbnailOperation(
            RefreshThumbnailAsync(selectedImage));
        var replaced = groups.Where(group => PasteSettingsViewModel.HasOwn(previousSettings, group.Name) &&
                !(proposal.FactsUnavailable && group.Name == "Crop & Straighten"))
            .Select(group => group.Name == "Crop & Straighten" ? "Crop" : group.Name).ToArray();
        ReportPaste("Pasted settings" + (replaced.Length == 0 ? "" : $" · replaced {string.Join(", ", replaced)}"),
            proposal.Reframed ? 1 : 0, proposal.FactsUnavailable ? 1 : 0);
    }

    private EditSettings CaptureLiveEditState()
    {
        var liveState = SelectedImage!.EditSettings.Clone();
        SaveSlidersTo(liveState);

        return liveState;
    }

    private async Task PasteToSelectionAsync(IReadOnlyList<ImageFile> targets,
        IReadOnlyCollection<EditSettingsGroup> groups)
    {
        if (_copiedSettings == null) return;

        List<(ImageFile Target, EditSettings Previous, EditSettings Settings)> proposed = [];
        var reframed = 0;
        var unavailable = 0;
        var invalid = 0;
        var skipped = 0;
        var snapshot = (_copiedSource!, _copiedSettings);
        var changesFrame = groups.Any(group => group.Name is "Crop & Straighten" or "Geometry");
        Dictionary<ImageFile, CropWriteContext>? cropContexts = null;

        if (changesFrame)
        {
            var folderPaths = CaptureCropWritePaths(targets);
            cropContexts = targets.ToDictionary(target => target,
                target => CaptureCropWriteContext(target, folderPaths));
        }

        var originals = targets.Select(target => (Target: target, Settings: target.EditSettings.Clone())).ToArray();

        try
        {
            if (PasteNeedsFrameFacts(snapshot.Item2, groups))
            {
                await Task.Run(BuildProposals);
            }
            else
            {
                BuildProposals();
            }

            foreach (var update in proposed)
            {
                await update.Target.EnsureCatalogIdAsync(_catalogService);
            }

            await _catalogService.SaveEditSettingsBatchWithHistoryAsync(proposed
                .Select(update => new CatalogEditSettingsUpdate(
                    update.Target.CatalogId,
                    update.Settings,
                    update.Previous))
                .ToList(), "Paste settings");

            foreach (var update in proposed)
            {
                update.Target.EditSettings = update.Settings;
                update.Target.HasEdits = update.Settings.HasEdits;
                await CommitCropAxisIfGeometryChangedAsync(update.Target, update.Previous,
                    update.Settings, cropContexts?.GetValueOrDefault(update.Target) ?? default);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Batch paste failed: {ex.Message}");
            ShowTransientStatus("Unable to apply edit settings");
            return;
        }

        long? surfaceGeneration = null;

        if (SelectedImage != null && proposed.Any(update => ReferenceEquals(update.Target, SelectedImage)))
        {
            surfaceGeneration = RequestEditedRender();
            InstallDevelopDocument(SelectedImage, SelectedImage.EditSettings,
                preserveCropDraft: !changesFrame, preserveLensDraft: true);

            _lastSavedState = SelectedImage.EditSettings.Clone();

            if (IsDevelopMode)
            {
                BeginDevelopHistoryLoad(SelectedImage);

                if (_pendingHistoryLoad is { } load)
                {
                    await load;
                }
            }

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

        var applied = targets.Count - invalid - skipped;
        var noun = applied == 1 ? "photo" : "photos";
        ReportPaste($"Applied to {applied} {noun}", reframed, unavailable, invalid);

        void BuildProposals()
        {
            foreach (var (target, previous) in originals)
            {
                try
                {
                    var proposal = PreparePaste(target, previous, groups, snapshot);
                    if (proposal.Reframed) reframed++;
                    if (proposal.FactsUnavailable) unavailable++;

                    if (proposal.FactsUnavailable && groups.Count == 1)
                    {
                        skipped++;
                        continue;
                    }

                    proposed.Add((target, previous, proposal.Settings));
                }
                catch (Exception ex) when (ex is System.Text.Json.JsonException or NotSupportedException)
                {
                    invalid++;
                }
            }
        }
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
        if (value != null && _backupNoticePresented) _backupNotice = null;
        OnPropertyChanged(nameof(StatusMessage));
    }

    partial void OnPinnedStatusChanged(string? value) =>
        OnPropertyChanged(nameof(StatusMessage));
}
