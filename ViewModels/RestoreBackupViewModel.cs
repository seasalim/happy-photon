using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HappyPhoton.Services;

namespace HappyPhoton.ViewModels;

public sealed partial class RestoreBackupViewModel : ViewModelBase
{
    private readonly CatalogBackupService _service;
    private readonly string _root;
    public RestoreBackupViewModel(string root)
    {
        _root = root;
        _service = new CatalogBackupService(new CatalogService(root));
        Refresh();
        Selected = Rows.FirstOrDefault(row => row.CanRestore);
    }
    [ObservableProperty] private IReadOnlyList<CatalogBackupRow> _rows = [];
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RestoreCommand))]
    private CatalogBackupRow? _selected;
    [ObservableProperty] private string? _error;
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RestoreCommand))]
    private bool _isBusy;
    public Func<Task<string?>>? ChooseFileAsync { get; set; }
    public Func<string, Task<bool>>? ConfirmAsync { get; set; }
    internal Action<string, bool, CheckedCatalogBackup>? Accepted { get; set; }
    private void Refresh() => Rows = _service.ListForRestore();

    [RelayCommand]
    private async Task ChooseFile()
    {
        if (IsBusy || ChooseFileAsync == null) return;
        if (await ChooseFileAsync() is { } path) await CheckAsync(path);
    }

    private bool CanRestoreSelected() => !IsBusy && Selected is { CanRestore: true };

    [RelayCommand(CanExecute = nameof(CanRestoreSelected))]
    private Task Restore() => !IsBusy && Selected is { CanRestore: true } row
        ? CheckAsync(row.Path) : Task.CompletedTask;

    private async Task CheckAsync(string path)
    {
        IsBusy = true;
        Error = null;
        try
        {
            using var check = await Task.Run(() => _service.CheckForRestore(path));
            var different = CatalogBackupService.ReadIdentity(_root) != check.Manifest.CatalogIdentity;
            var message = $"Restore the backup from {check.Manifest.Utc:g}? Changes since then are not included. " +
                "The current catalog will be kept as a Before restore backup. Thumbnails will rebuild." +
                (different ? "\n\nThis backup belongs to a different catalog. Continuing replaces this catalog with that one." : "") +
                "\n\nIf XMP reading is enabled, newer sidecar ratings will be read back on the next folder load.";
            if (ConfirmAsync != null && await ConfirmAsync(message)) Accepted?.Invoke(path, different, check);
        }
        catch (Exception ex) { Error = ex.Message; Refresh(); }
        finally { IsBusy = false; }
    }
}
