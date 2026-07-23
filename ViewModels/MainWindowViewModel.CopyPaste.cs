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

    public Func<int, Task<bool>>? ConfirmBatchApplyAsync { get; set; }

    private bool CanCopyEditSettings => HasSelectedImage && !IsFullScreenMode;

    [RelayCommand(CanExecute = nameof(CanCopyEditSettings))]
    private void CopyEditSettings()
    {
        if (SelectedImage == null) return;

        var liveSettings = new EditSettings();
        SaveSlidersTo(liveSettings);
        _copiedSettings = EditSettingsTransfer.CopySubset(liveSettings);
        HasCopiedSettings = true;
        ShowTransientStatus("Copied edit settings");
    }

    private bool CanPasteEditSettings =>
        HasCopiedSettings && HasSelectedImage && !IsFullScreenMode;

    [RelayCommand(CanExecute = nameof(CanPasteEditSettings))]
    private async Task PasteEditSettingsAsync()
    {
        if (_copiedSettings == null || SelectedImage == null) return;

        if (!IsDevelopMode && SelectedCount > 0)
        {
            await PasteToSelectionAsync();
            return;
        }

        await PasteToCurrentImageAsync();
    }

    private async Task PasteToCurrentImageAsync()
    {
        var selectedImage = SelectedImage;
        if (selectedImage == null || _copiedSettings == null) return;

        PushLiveUndoState();
        var currentRotation = Rotation;
        var currentHorizonRotation = HorizonRotation;
        var currentCrop = CurrentCrop?.Clone();
        var storedRotation = selectedImage.EditSettings.Rotation;
        var storedHorizonRotation = selectedImage.EditSettings.HorizonRotation;
        var storedCrop = selectedImage.EditSettings.Crop?.Clone();

        _isLoadingImage = true;
        try
        {
            LoadSlidersFrom(_copiedSettings);
            CurrentCurve = _copiedSettings.Curve.Clone();
            Rotation = currentRotation;
            HorizonRotation = currentHorizonRotation;
            CurrentCrop = currentCrop;
        }
        finally
        {
            _isLoadingImage = false;
        }

        SaveSlidersTo(selectedImage.EditSettings);
        selectedImage.EditSettings.Rotation = storedRotation;
        selectedImage.EditSettings.HorizonRotation = storedHorizonRotation;
        selectedImage.EditSettings.Crop = storedCrop;
        selectedImage.HasEdits = selectedImage.EditSettings.HasEdits;
        await SaveEditSettingsAsync(selectedImage);

        if (ReferenceEquals(SelectedImage, selectedImage))
        {
            _lastSavedState = selectedImage.EditSettings.Clone();
            await UpdatePreviewWithCurrentSliders();
            UpdateCanReset();
        }

        _ = RefreshThumbnailAsync(selectedImage);
        ShowTransientStatus("Pasted edit settings");
    }

    private void PushLiveUndoState()
    {
        // No dedup: the pre-paste state may differ from the stack top only by
        // curve, which the dedup comparison ignores.
        _history.PushEdit(CaptureLiveEditState(), dedup: false);
        SyncHistoryFlags();
    }

    private EditSettings CaptureLiveEditState()
    {
        var liveState = SelectedImage!.EditSettings.Clone();
        SaveSlidersTo(liveState);
        liveState.Curve = (CurrentCurve ?? new CurveData()).Clone();
        return liveState;
    }

    private async Task PasteToSelectionAsync()
    {
        if (_copiedSettings == null) return;

        var targets = Library.GetSelectedImages().ToList();
        if (targets.Count == 0 || ConfirmBatchApplyAsync == null) return;
        if (!await ConfirmBatchApplyAsync(targets.Count)) return;

        try
        {
            foreach (var target in targets)
            {
                await EnsureCatalogIdAsync(target);
            }

            var proposed = targets.Select(target =>
            {
                var settings = target.EditSettings.Clone();
                EditSettingsTransfer.ApplySubset(_copiedSettings, settings);
                return (Target: target, Settings: settings);
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

        if (SelectedImage != null && targets.Contains(SelectedImage))
        {
            _history.Clear();
            SyncHistoryFlags();

            _isLoadingImage = true;
            try
            {
                LoadSlidersFrom(SelectedImage.EditSettings);
                CurrentCurve = SelectedImage.EditSettings.Curve;
            }
            finally
            {
                _isLoadingImage = false;
            }

            _lastSavedState = SelectedImage.EditSettings.Clone();
            await UpdatePreviewWithCurrentSliders();
            UpdateCanReset();
        }

        _ = RefreshThumbnailsAsync(targets);

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

    private async Task RefreshThumbnailAsync(ImageFile image)
    {
        try
        {
            var thumbnail = await ImageService.LoadThumbnailAsync(
                image,
                CancellationToken.None);
            if (!Library.Contains(image))
            {
                thumbnail?.Dispose();
                return;
            }

            image.ThumbnailLoadFailed = thumbnail == null;
            if (thumbnail != null) image.ReplaceThumbnail(thumbnail);
        }
        catch (Exception ex)
        {
            if (Library.Contains(image)) image.ThumbnailLoadFailed = true;
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
}
