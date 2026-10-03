using HappyPhoton.Models;
using HappyPhoton.Services;

namespace HappyPhoton.ViewModels;

public partial class MainWindowViewModel
{
    private Task PasteToSelectionAsync(PasteSnapshot snapshot, IReadOnlyList<ImageFile> targets,
        IReadOnlyCollection<EditSettingsGroup> groups) => BeginPasteBatchAsync(snapshot, targets, groups, "paste");

    private async Task BeginPasteBatchAsync(PasteSnapshot snapshot, IReadOnlyList<ImageFile> targets,
        IReadOnlyCollection<EditSettingsGroup> groups, string kind)
    {
        var operation = ClearBatchOffer();
        var folderGeneration = _browseGeneration;
        await _batchMutationGate.WaitAsync();

        try
        {
            await PasteToSelectionCoreAsync(snapshot, targets, groups, kind, operation, folderGeneration);
        }
        finally
        {
            _batchMutationGate.Release();
        }
    }

    private async Task PasteToSelectionCoreAsync(PasteSnapshot snapshot, IReadOnlyList<ImageFile> targets,
        IReadOnlyCollection<EditSettingsGroup> groups, string kind, long operation, long folderGeneration)
    {
        List<(ImageFile Target, EditSettings Previous, EditSettings Settings, bool LensApplied)> proposed = [];
        var reframed = 0;
        var skips = new List<KeyValuePair<string, string>>();
        var invalid = 0;
        var skipped = 0;
        var changesFrame = groups.Any(group => group.Name is "Crop & Straighten" or "Geometry");
        Dictionary<ImageFile, CropWriteContext>? cropContexts = null;

        if (changesFrame)
        {
            var folderPaths = CaptureCropWritePaths(targets);
            cropContexts = targets.ToDictionary(target => target,
                target => CaptureCropWriteContext(target, folderPaths));
        }

        if (SelectedImage != null) RememberLoadedCamera(SelectedImage);

        var originals = targets.Select(target => (Target: target, Settings: target.EditSettings.Clone())).ToArray();

        try
        {
            if (PasteNeedsFrameFacts(snapshot.Settings, groups, originals.Any(item => item.Settings.Repairs is { Count: > 0 })))
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

            var batch = await _catalogService.SaveEditSettingsBatchWithHistoryAsync(proposed
                .Select(update => new CatalogEditSettingsUpdate(
                    update.Target.CatalogId,
                    update.Settings,
                    update.Previous))
                .ToList(), "Paste settings");

            if (folderGeneration == _browseGeneration && operation == _batchOperationId && batch.Count > 0)
            {
                _lastBatch = new(folderGeneration, operation, kind, batch,
                    proposed.ToDictionary(update => update.Target.CatalogId, update => update.Target));
                NotifyBatchOfferChanged();
            }

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

        await RefreshBatchSelectionAsync(proposed, changesFrame);

        _ = TrackDirectThumbnailOperation(
            RefreshThumbnailsAsync(proposed.Select(update =>
                (update.Target, update.Previous))));

        var applied = targets.Count - invalid - skipped;
        var noun = applied == 1 ? "photo" : "photos";
        ReportPaste($"Applied to {applied} {noun}", reframed, skips, invalid);

        void BuildProposals()
        {
            foreach (var (target, previous) in originals)
            {
                try
                {
                    var proposal = PreparePaste(target, previous, groups, snapshot);
                    if (proposal.Reframed) reframed++;
                    skips.AddRange(proposal.Skips);

                    if (EditSettingsJson.Serialize(proposal.Settings) == EditSettingsJson.Serialize(previous))
                    {
                        if (proposal.Skips.Count == groups.Count) skipped++;
                        continue;
                    }

                    proposed.Add((target, previous, proposal.Settings, proposal.LensApplied));
                }
                catch (Exception ex) when (ex is System.Text.Json.JsonException or NotSupportedException)
                {
                    invalid++;
                }
            }
        }
    }

    private async Task RefreshBatchSelectionAsync(
        IReadOnlyList<(ImageFile Target, EditSettings Previous, EditSettings Settings, bool LensApplied)> proposed,
        bool changesFrame)
    {
        if (SelectedImage != null && proposed.Any(update => ReferenceEquals(update.Target, SelectedImage) &&
            BatchModelMatches(update.Target, update.Settings)))
        {
            var surfaceGeneration = RequestEditedRender();
            var update = proposed.Single(update => ReferenceEquals(update.Target, SelectedImage));
            InstallDevelopDocument(SelectedImage, update.Settings,
                preserveCropDraft: !changesFrame, preserveLensDraft: !update.LensApplied, previous: update.Previous);

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

            if (IsLoupeMode)
            {
                ReloadLoupe(SelectedImage);
                await LoupeLoadingTask;
            }

            UpdateCanReset();
        }
    }
}
