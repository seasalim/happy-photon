using System.Text.Json;
using HappyPhoton.Services;

namespace HappyPhoton.ViewModels;

public partial class MainWindowViewModel
{
    private string? _backupNotice;

    private string? _backupNoticeOutcome;

    private bool _backupNoticePresented;

    internal Task BackupNoticeLoad { get; private set; } = Task.CompletedTask;

    private async Task LoadBackupNoticeAsync()
    {
        try
        {
            // Capture before the restore dialog can acknowledge and remove its notice.
            var restoreNoticePending = File.Exists(CatalogRestoreExecutor.NoticePath(_catalogService.CatalogPath));
            var (outcome, presented) = await Task.Run(async () => (
                await _catalogService.GetAppSettingAsync(CatalogBackupService.OutcomeKey),
                await _catalogService.GetAppSettingAsync(CatalogBackupService.PresentedOutcomeKey)));
            if (outcome == null || outcome == presented) return;
            if (restoreNoticePending)
            {
                await Task.Run(() => _catalogService.SetAppSettingAsync(CatalogBackupService.PresentedOutcomeKey, outcome));
                return;
            }

            var attempt = JsonSerializer.Deserialize<BackupOutcome>(outcome);
            _backupNotice = attempt?.Status switch
            {
                "failed" => "The last catalog backup failed; it will retry when you quit.",
                "catalog-damaged" => "The last backup found a damaged catalog. Restore a backup in Settings → Storage.",
                _ => null
            };
            _backupNoticeOutcome = outcome;
            OnPropertyChanged(nameof(StatusMessage));
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Backup notice: {ex.Message}"); }
    }

    internal async Task AcknowledgeBackupNoticeAsync(string? displayed)
    {
        if (_backupNoticePresented || _backupNotice == null || displayed != _backupNotice ||
            StatusMessage != _backupNotice || StartupGateState != StartupGateState.Ready) return;
        _backupNoticePresented = true;
        try { await _catalogService.SetAppSettingAsync(CatalogBackupService.PresentedOutcomeKey, _backupNoticeOutcome!); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Backup notice acknowledgement: {ex.Message}"); }
    }
}
