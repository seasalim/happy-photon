using System.Text.Json;
using HappyPhoton.Services;

namespace HappyPhoton.ViewModels;

public partial class MainWindowViewModel
{
    private readonly OneTimeNotice _oneTimeNotice = new();

    internal bool IsNoticePending => _oneTimeNotice.IsPending && StatusMessage == _oneTimeNotice.Text;

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
            var text = attempt?.Status switch
            {
                "failed" => "The last catalog backup failed; it will retry when you quit.",
                "catalog-damaged" => "The last backup found a damaged catalog. Restore a backup in Settings → Storage.",
                _ => null
            };
            _oneTimeNotice.Offer(text, CatalogBackupService.PresentedOutcomeKey, outcome);
            OnPropertyChanged(nameof(StatusMessage));
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Backup notice: {ex.Message}"); }
    }

    internal async Task AcknowledgeNoticeAsync(string? displayed)
    {
        if (StatusMessage != displayed || StartupGateState != StartupGateState.Ready) return;

        if (_oneTimeNotice.IsPending && displayed == DcpHintNotice) _dcpHintPresented = true;

        await _oneTimeNotice.AcknowledgeAsync(_catalogService, displayed);
    }
}
