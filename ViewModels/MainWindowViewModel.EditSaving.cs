using HappyPhoton.Models;
using HappyPhoton.Services;

namespace HappyPhoton.ViewModels;

public partial class MainWindowViewModel
{
    private async Task AutoSaveAsync(
        string? historyLabel = null,
        EditSettings? before = null)
    {
        var image = SelectedImage;
        if (image == null) return;

        SaveSlidersTo(image.EditSettings);
        image.HasEdits = image.EditSettings.HasEdits;

        await SaveEditSettingsAsync(image, historyLabel, before);
        if (ReferenceEquals(image, SelectedImage))
            _lastSavedState = image.EditSettings.Clone();
    }

    private async Task SaveEditSettingsAsync(ImageFile imageFile)
    {
        await SaveEditSettingsAsync(imageFile, null, null);
    }

    private async Task SaveEditSettingsAsync(
        ImageFile imageFile,
        EditSettings settings,
        bool recordHistory = true)
    {
        await SaveEditSettingsCoreAsync(
            imageFile, settings, null, null, recordHistory);
    }

    private Task SaveEditSettingsAsync(
        ImageFile imageFile,
        string? historyLabel,
        EditSettings? before = null) =>
        SaveEditSettingsCoreAsync(
            imageFile, imageFile.EditSettings, historyLabel, before, true);

    private Task SaveEditSettingsCoreAsync(
        ImageFile imageFile,
        EditSettings settings,
        string? historyLabel,
        EditSettings? before,
        bool recordHistory,
        Func<Task<bool>>? beforeSave = null)
    {
        var settingsSnapshot = settings.Clone();
        var tracksHistory = recordHistory && IsDevelopMode &&
                            ReferenceEquals(imageFile, SelectedImage);
        var historyGeneration = Volatile.Read(ref _historySubjectGeneration);
        var predecessor = tracksHistory ? _serializedHistoryCommit : null;
        var predecessorSettings = predecessor is { IsCompleted: false } &&
                                  ReferenceEquals(
                                      imageFile, _serializedHistoryImage)
            ? _serializedHistorySettings
            : null;
        var beforeSnapshot = (before ?? predecessorSettings ??
                              _lastSavedState ?? settings).Clone();
        var load = tracksHistory ? _pendingHistoryLoad : null;
        var cropWriteContext = XmpCropProjection.GeometryChanged(
            beforeSnapshot, settingsSnapshot)
            ? CaptureCropWriteContext(imageFile)
            : default;
        var save = SaveEditSettingsOperationAsync(
            imageFile, settingsSnapshot, historyLabel, beforeSnapshot,
            tracksHistory, historyGeneration, predecessor, load,
            cropWriteContext, beforeSave);
        if (tracksHistory)
        {
            _serializedHistoryCommit = save;
            _serializedHistoryImage = imageFile;
            _serializedHistorySettings = settingsSnapshot;
            TrackHistoryCommit(save);
        }
        return save;
    }

    private async Task SaveEditSettingsOperationAsync(
        ImageFile imageFile,
        EditSettings settings,
        string? historyLabel,
        EditSettings before,
        bool tracksHistory,
        long historyGeneration,
        Task? predecessor,
        Task? load,
        CropWriteContext cropWriteContext,
        Func<Task<bool>>? beforeSave = null)
    {
        if (beforeSave != null && !await beforeSave()) return;
        await imageFile.EnsureCatalogIdAsync(_catalogService);
        if (predecessor != null)
            await ObservePendingHistoryWorkAsync(predecessor);
        if (load != null)
            await load;

        var mutation = tracksHistory &&
                       IsCurrentHistorySubject(imageFile, historyGeneration) &&
                       IsHistoryLoaded
            ? _history.PrepareAppend(before, settings, historyLabel)
            : null;
        await _catalogService.SaveEditSettingsWithHistoryAsync(
            imageFile.CatalogId, settings, mutation);
        await CommitCropAxisIfGeometryChangedAsync(
            imageFile, before, settings, cropWriteContext);
        if (ReferenceEquals(imageFile, SelectedImage) &&
            (!tracksHistory ||
             IsCurrentHistorySubject(imageFile, historyGeneration)))
            _lastSavedState = settings.Clone();
        if (mutation != null &&
            IsCurrentHistorySubject(imageFile, historyGeneration))
        {
            _history.Publish(mutation);
            SyncHistoryFlags();
        }
    }

    private async Task CommitCropAxisIfGeometryChangedAsync(
        ImageFile image,
        EditSettings before,
        EditSettings after,
        CropWriteContext writeContext)
    {
        if (!XmpCropProjection.GeometryChanged(before, after)) return;
        await CommitCropAssessmentAsync(image, writeContext);
    }

    private void TrackHistoryCommit(Task commit)
    {
        var pending = _pendingHistoryCommit;
        _pendingHistoryCommit = pending is { IsCompleted: false }
            ? Task.WhenAll(pending, commit)
            : commit;
    }
}
