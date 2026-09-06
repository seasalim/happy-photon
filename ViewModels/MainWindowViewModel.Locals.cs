using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HappyPhoton.Models;

namespace HappyPhoton.ViewModels;

public partial class MainWindowViewModel
{
    [ObservableProperty] private bool _isLocalsMode;
    [ObservableProperty] private bool _isLocalCreationArmed;
    [ObservableProperty] private bool _showLocalMask;
    private string? _selectedLocalId;
    public System.Collections.ObjectModel.ObservableCollection<LocalRowViewModel> LocalRows { get; } = [];
    public LocalRowViewModel? SelectedLocalRow
    {
        get => LocalRows.FirstOrDefault(row => row.Local.Id == _selectedLocalId);
        set { if (value != null) SelectedLocal = value.Local; }
    }

    public IReadOnlyList<LocalAdjustment> Locals => SelectedImage?.EditSettings.Locals ?? [];
    public bool HasLocals => Locals.Count > 0;
    public LocalAdjustment? SelectedLocal
    {
        get => Locals.FirstOrDefault(local => local.Id == _selectedLocalId);
        set
        {
            if (value?.Id == _selectedLocalId) return;
            DiscardLocalsGesture();
            _selectedLocalId = value?.Id;
            NotifyLocalsState();
        }
    }
    public bool HasSelectedLocal => SelectedLocal != null;
    public bool CanAddLocal => CanEditLocals && Locals.Count < LocalAdjustment.MaximumCount;
    public bool CanEditLocals => !_renderOutcomeChannelClosed && IsLocalsMode && CanEditSelectedImage && IsDevelopMode &&
        !IsFullScreenMode && !IsBeforeAfterSplit && !_isBeforeAfterSplitTransitioning &&
        _requestedPreviewIntent != PreviewSurfaceIntent.Original &&
        !_isHoveringPreset && _hoveredHistoryEntry == null && !IsWhiteBalancePicking;
    public bool IsLocalMaskVisible => CanEditLocals && HasSelectedLocal &&
        (ShowLocalMask || IsLocalCreationArmed);
    public string LocalsInstruction => Locals.Count == 8
        ? "8 of 8 locals — delete a local to add another"
        : IsLocalCreationArmed ? "Drag to place; Escape cancels"
        : !HasLocals ? "Add Linear, then drag on the image, or Place at center."
        : SelectedLocal is { Enabled: false } ? "Disabled" : "";
    public LocalsFrame? LocalsFrame => SelectedImage is { } image && IsLocalsMode
        ? ImageService.Previews.GetLocalsFrame(image, CaptureLiveEditState()) : null;
    public double LocalExposure
    {
        get => SelectedLocal?.Exposure ?? 0;
        set
        {
            if (!CanEditLocals || SelectedLocal is not { } local || !double.IsFinite(value)) return;
            value = Math.Clamp(value, -4, 4);
            if (value == local.Exposure) return;
            local.Exposure = value;
            NotifyLocalsState();
            OnEditValueChanged();
        }
    }

    [RelayCommand]
    private async Task ToggleLocalsModeAsync()
    {
        if (IsLocalsMode) { CloseLocals(); return; }
        if (!IsDevelopMode || SelectedImage == null) return;
        if (IsCropMode) await CancelCropCoreAsync();
        if (IsCropMode) return;
        IsWhiteBalancePicking = false;
        IsLocalsMode = true;
        RebindLocalSelection(first: true);
    }

    [RelayCommand]
    private void CloseLocals()
    {
        DiscardLocalsGesture();
        IsLocalsMode = false;
    }

    [RelayCommand(CanExecute = nameof(CanAddLocal))]
    private void AddLinear()
    {
        DiscardLocalsGesture();
        IsLocalCreationArmed = true;
    }

    [RelayCommand(CanExecute = nameof(CanAddLocal))]
    private Task PlaceLocalAtCenterAsync() => ChangeLocalAsync("Add Linear", () =>
    {
        if (Locals.Count >= LocalAdjustment.MaximumCount) return;
        var local = NewLinear();
        (SelectedImage!.EditSettings.Locals ??= []).Add(local);
        _selectedLocalId = local.Id;
    });

    [RelayCommand]
    private Task DeleteLocalAsync(LocalAdjustment? local)
    {
        local ??= SelectedLocal;
        if (local == null) return Task.CompletedTask;
        var id = local.Id;
        return ChangeLocalAsync("Delete local", () =>
        {
            var list = SelectedImage!.EditSettings.Locals!;
            var index = list.FindIndex(item => item.Id == id);
            if (index < 0) return;
            list.RemoveAt(index);
            _selectedLocalId = list.Count == 0 ? null : list[Math.Min(index, list.Count - 1)].Id;
        });
    }

    [RelayCommand]
    private Task ToggleLocalEnabledAsync(LocalAdjustment? local)
    {
        var id = (local ?? SelectedLocal)?.Id;
        var label = (local ?? SelectedLocal)?.Enabled == true ? "Disable local" : "Enable local";
        return ChangeLocalAsync(label, () =>
        {
            var target = Locals.FirstOrDefault(item => item.Id == id);
            if (target != null) target.Enabled = !target.Enabled;
        });
    }

    private LocalAdjustment NewLinear() => new()
    { Ordinal = Locals.Select(local => local.Ordinal).DefaultIfEmpty(0).Max() + 1 };

    [RelayCommand]
    private async Task ResumeLocalEditingAsync()
    {
        DiscardLocalsGesture();
        EndHistoryHover();
        await RestoreFromHoverAsync();
        if (IsBeforeAfterSplit) CloseBeforeAfterSplit();
        IsFullScreenMode = false;
        IsWhiteBalancePicking = false;
        if (_requestedPreviewIntent == PreviewSurfaceIntent.Original)
            await ToggleBeforeAfterAsync();
        NotifyLocalsState();
    }

    private async Task ChangeLocalAsync(string label, Action change)
    {
        DiscardLocalsGesture();
        if (!CanEditLocals || SelectedImage == null) return;
        var before = CaptureLiveEditState();
        change();
        NotifyLocalsState();
        await CommitLocalAsync(before, label);
    }

    private async Task CommitLocalAsync(EditSettings before, string label)
    {
        if (SelectedImage is not { } image) return;
        var after = CaptureLiveEditState();
        if (before.HasSameEdits(after)) return;
        image.HasEdits = after.HasEdits;
        var previousIntent = _requestedPreviewIntent;
        var generation = RequestEditedRender();
        var save = SaveEditSettingsCoreAsync(image, after, label, before, recordHistory: true,
            beforeSave: () => RenderCommittedLocalAsync(image, before, generation, previousIntent));
        TrackHistoryCommit(save);
        try { await save; }
        catch
        {
            RollbackEditReservation(image, before, generation, previousIntent);
            ShowTransientStatus("Unable to save local adjustment");
        }
        RebindLocalSelection();
        UpdateCanReset();
        RefreshSelectedThumbnail();
    }

    private async Task<bool> RenderCommittedLocalAsync(ImageFile image, EditSettings before,
        long generation, PreviewSurfaceIntent previousIntent)
    {
        var renderSucceeded = false;
        await UpdatePreviewWithCurrentSliders(generation: generation,
            observeRenderSucceeded: value => renderSucceeded = value);
        if (!renderSucceeded && generation == LatestPreviewOutcomeGeneration)
        {
            RollbackEditReservation(image, before, generation, previousIntent);
            return false;
        }
        // Navigation may supersede the preview, but the released gesture still
        // belongs in its captured image's catalog row.
        return true;
    }

    private void RebindLocalSelection(bool first = false)
    {
        if (first || SelectedLocal == null) _selectedLocalId = Locals.FirstOrDefault()?.Id;
        NotifyLocalsState();
    }

    private void NotifyLocalsState()
    {
        for (var i = LocalRows.Count - 1; i >= 0; i--)
            if (!Locals.Any(local => local.Id == LocalRows[i].Local.Id)) LocalRows.RemoveAt(i);
        foreach (var local in Locals)
        {
            var row = LocalRows.FirstOrDefault(item => item.Local.Id == local.Id);
            if (row == null) LocalRows.Add(new(local));
            else row.Refresh(local);
        }
        OnPropertyChanged(nameof(SelectedLocalRow));
        foreach (var property in new[] { nameof(Locals), nameof(HasLocals), nameof(SelectedLocal),
            nameof(HasSelectedLocal), nameof(LocalExposure), nameof(CanAddLocal), nameof(CanEditLocals),
            nameof(IsLocalMaskVisible), nameof(LocalsInstruction), nameof(LocalsFrame),
            nameof(LocalX), nameof(LocalY), nameof(LocalAngle), nameof(LocalWidth), nameof(CanEditLocalGeometry) })
            OnPropertyChanged(property);
        AddLinearCommand.NotifyCanExecuteChanged();
        PlaceLocalAtCenterCommand.NotifyCanExecuteChanged();
        CenterLocalInViewCommand.NotifyCanExecuteChanged();
        UndoCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsLocalsModeChanged(bool value) => NotifyLocalsState();
    partial void OnIsLocalCreationArmedChanged(bool value) => NotifyLocalsState();
    partial void OnShowLocalMaskChanged(bool value) => NotifyLocalsState();
}
