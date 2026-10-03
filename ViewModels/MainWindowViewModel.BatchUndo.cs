using CommunityToolkit.Mvvm.Input;
using HappyPhoton.Models;
using HappyPhoton.Services;

namespace HappyPhoton.ViewModels;

public partial class MainWindowViewModel
{
    private sealed record BatchOffer(long FolderGeneration, long OperationId, string Kind,
        IReadOnlyList<CatalogEditBatchTarget> Targets, IReadOnlyDictionary<long, ImageFile> Images);

    private BatchOffer? _lastBatch;

    private long _batchOperationId;

    private readonly SemaphoreSlim _batchMutationGate = new(1, 1);

    public bool IsBatchUndoOffered => _lastBatch != null && IsBrowseMode && !IsCompareMode && !IsFullScreenMode;

    public string? UndoBatchText => _lastBatch is { } batch
        ? $"Undo {batch.Kind} ({batch.Targets.Count} {(batch.Targets.Count == 1 ? "photo" : "photos")})"
        : null;

    private long ClearBatchOffer()
    {
        _lastBatch = null;
        var operation = ++_batchOperationId;
        NotifyBatchOfferChanged();

        return operation;
    }

    private void NotifyBatchOfferChanged()
    {
        OnPropertyChanged(nameof(IsBatchUndoOffered));
        OnPropertyChanged(nameof(UndoBatchText));
        UndoBatchCommand.NotifyCanExecuteChanged();
        UndoCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(IsBatchUndoOffered))]
    private async Task UndoBatchAsync()
    {
        if (!IsBatchUndoOffered || _lastBatch is not { } batch) return;

        await WaitForPendingHistoryWorkAsync();
        await _batchMutationGate.WaitAsync();

        try
        {
            if (!ReferenceEquals(_lastBatch, batch) || !IsBatchUndoOffered) return;

            var undo = UndoBatchCoreAsync(batch);
            TrackHistoryCommit(undo);
            await undo;
        }
        finally
        {
            _batchMutationGate.Release();
        }
    }

    private async Task UndoBatchCoreAsync(BatchOffer batch)
    {
        var persistence = RestoreBatchModelsAsync(batch);

        foreach (var completed in _imageHistorySaves.Where(pair => pair.Value.IsCompleted).ToArray())
        {
            _imageHistorySaves.Remove(completed.Key);
        }

        foreach (var image in batch.Images.Values)
        {
            _imageHistorySaves[image.FilePath] = persistence;
        }

        var (result, updates) = await persistence;

        if (batch.FolderGeneration == _browseGeneration)
        {
            // History loads await the per-path persistence task, never this refresh.
            await RefreshBatchSelectionAsync(updates, changesFrame: true);
            _ = TrackDirectThumbnailOperation(RefreshThumbnailsAsync(
                updates.Select(update => (update.Target, update.Previous))));
        }

        if (ReferenceEquals(_lastBatch, batch))
        {
            ClearBatchOffer();
            ShowTransientStatus(result.Skipped.Count == 0
                ? $"Restored {result.Restored.Count} {(result.Restored.Count == 1 ? "photo" : "photos")}"
                : $"Restored {result.Restored.Count} of {batch.Targets.Count} · {result.Skipped.Count} changed since");
        }
    }

    private async Task<(CatalogEditBatchUndoResult Result,
        (ImageFile Target, EditSettings Previous, EditSettings Settings, bool LensApplied)[] Updates)>
        RestoreBatchModelsAsync(BatchOffer batch)
    {
        var originals = batch.Images.ToDictionary(pair => pair.Key, pair => pair.Value.EditSettings.Clone());
        var result = await _catalogService.UndoEditSettingsBatchAsync(batch.Targets);
        var restored = result.Restored.ToHashSet();
        var updates = batch.Targets.Where(target => restored.Contains(target.CatalogId))
            .Select(target => (Target: batch.Images[target.CatalogId],
                Previous: originals[target.CatalogId],
                Settings: target.Previous, LensApplied: true)).ToArray();
        var paths = CaptureCropWritePaths(updates.Select(update => update.Target).ToArray());
        var published = new List<(ImageFile Target, EditSettings Previous, EditSettings Settings, bool LensApplied)>();

        foreach (var update in updates)
        {
            // A Develop save may already own the live model while awaiting this persistence task.
            if (!BatchModelMatches(update.Target, update.Previous)) continue;

            update.Target.EditSettings = update.Settings.Clone();
            update.Target.HasEdits = update.Settings.HasEdits;
            published.Add(update);
            await CommitCropAxisIfGeometryChangedAsync(update.Target, update.Previous, update.Settings,
                CaptureCropWriteContext(update.Target, paths));
        }

        return (result, published.ToArray());
    }

    private static bool BatchModelMatches(ImageFile image, EditSettings settings) =>
        image.EditSettings.HasSameEdits(settings) && image.EditSettings.AppliedPresetId == settings.AppliedPresetId;
}
