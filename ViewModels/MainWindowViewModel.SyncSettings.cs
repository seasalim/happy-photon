using CommunityToolkit.Mvvm.Input;
using HappyPhoton.Models;
using HappyPhoton.Services;

namespace HappyPhoton.ViewModels;

public partial class MainWindowViewModel
{
    // One change notification shares one scan between can-execute and the tooltip.
    private (int Count, bool Hydration)? _syncNotificationScan;
    private bool _isNotifyingSync;

    internal int SyncSelectionScanCount { get; private set; }

    private bool CanSyncSettings => SyncTargetCount(out _) > 0;

    public string SyncSettingsToolTip
    {
        get
        {
            if (IsCompareMode) return "Leave Compare to sync settings";

            var count = SyncTargetCount(out var hydration);
            if (hydration) return "Download online-only originals to sync settings";
            if (SelectedImage is { IsSelected: false } && SelectedCount > 0)
                return "Select the outlined photo too; it is the source.";
            if (count == 0) return "Select two or more photos to sync. The outlined photo is the source.";

            return $"Sync settings from {SelectedImage!.FileName} to {count} " +
                $"{(count == 1 ? "photo" : "photos")} (Ctrl+Shift+S)";
        }
    }

    private int SyncTargetCount(out bool hydration)
    {
        if (_isNotifyingSync && _syncNotificationScan is { } shared)
        {
            hydration = shared.Hydration;

            return shared.Count;
        }

        var count = ScanSyncTargets(out hydration);
        if (_isNotifyingSync) _syncNotificationScan = (count, hydration);

        return count;
    }

    private int ScanSyncTargets(out bool hydration)
    {
        hydration = SelectedImage?.SourceRequiresHydration == true;
        if (!IsBrowseMode || IsCompareMode || IsFullScreenMode || hydration ||
            SelectedImage is not { IsSelected: true } source || SelectedCount < 2 ||
            IsDeleteTargetClaimed(source.FilePath))
            return 0;

        SyncSelectionScanCount++;
        var count = 0;

        foreach (var target in Browse.GetSelectedImages())
        {
            if (SyncIdentity(target) == SyncIdentity(source) || IsDeleteTargetClaimed(target.FilePath)) continue;

            hydration |= target.SourceRequiresHydration;
            count++;
        }

        return hydration ? 0 : count;
    }

    private void NotifySyncSettingsChanged()
    {
        _isNotifyingSync = true;
        _syncNotificationScan = null;

        try
        {
            SyncSettingsCommand.NotifyCanExecuteChanged();
            OnPropertyChanged(nameof(SyncSettingsToolTip));
        }
        finally
        {
            _isNotifyingSync = false;
            _syncNotificationScan = null;
        }
    }

    private static (string Path, int Version) SyncIdentity(ImageFile image) => (image.FilePath, image.Version);

    [RelayCommand(CanExecute = nameof(CanSyncSettings))]
    private async Task SyncSettingsAsync()
    {
        if (!CanSyncSettings) return;

        var source = SelectedImage!;
        var sourceIdentity = SyncIdentity(source);
        var workspace = WorkspaceMode;
        var targets = ResolveActionTargets().Targets.Where(target => SyncIdentity(target) != sourceIdentity).ToArray();
        var identities = targets.Select(SyncIdentity).ToHashSet();
        if (targets.Length == 0 || targets.Any(target => target.SourceRequiresHydration)) return;

        var snapshot = CapturePasteSnapshot(source);
        if (!await ChoosePasteGroupsAsync(snapshot, targets, currentPhoto: false, PasteSettingsMode.Sync)) return;
        if (WorkspaceMode != workspace || !ReferenceEquals(SelectedImage, source) ||
            SyncIdentity(source) != sourceIdentity || !CanSyncSettings ||
            !identities.SetEquals(ResolveActionTargets().Targets
                .Where(target => SyncIdentity(target) != sourceIdentity).Select(SyncIdentity)))
            return;

        if (targets.Any(target => _sourceAvailabilityService.GetAvailability(target.FilePath).IsOnlineOnly()))
        {
            ShowTransientStatus("Download online-only originals before applying edit settings");
            return;
        }

        await PasteToSelectionAsync(snapshot, targets, RememberedPasteGroups);
    }
}
