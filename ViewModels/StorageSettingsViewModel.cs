using System.Runtime.InteropServices;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HappyPhoton.Services;

namespace HappyPhoton.ViewModels;

public partial class StorageSettingsViewModel : ViewModelBase
{
    private readonly AppDataLocations _locations;
    private readonly CatalogLocationMigrator _migrator;
    private readonly bool _isPackagedWindows;
    private readonly IFileOperationService _fileOperationService;

    public StorageSettingsViewModel(
        AppDataLocations locations,
        CatalogLocationMigrator migrator)
        : this(
            locations,
            migrator,
            PackagedWindowsDetector.IsPackaged,
            fileOperationService: null)
    {
    }

    internal StorageSettingsViewModel(
        AppDataLocations locations,
        CatalogLocationMigrator migrator,
        bool isPackagedWindows,
        IFileOperationService? fileOperationService = null)
    {
        _locations = locations;
        _migrator = migrator;
        _isPackagedWindows = isPackagedWindows;
        _fileOperationService = fileOperationService ?? new FileOperationService();
        CatalogRoot = locations.CatalogRoot;
        CacheRoot = locations.CacheRoot;

        foreach (var command in new[] { RestoreBackupCommand, MoveCatalogCommand, MoveCacheCommand })
            command.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(IAsyncRelayCommand.IsRunning)) NotifyAvailability();
            };
    }

    [ObservableProperty]
    private string _catalogRoot;

    [ObservableProperty]
    private string _cacheRoot;

    [ObservableProperty]
    private string? _pendingCatalogRoot;

    [ObservableProperty]
    private string? _pendingCacheRoot;

    [ObservableProperty]
    private string? _catalogStatus;

    [ObservableProperty]
    private string? _cacheStatus;

    [ObservableProperty]
    private string? _catalogError;

    [ObservableProperty]
    private string? _cacheError;

    private bool HasEnvironmentManagedRoot =>
        _locations.IsCatalogEnvironmentManaged || _locations.IsCacheEnvironmentManaged;
    public bool CanChangeCatalog => !HasEnvironmentManagedRoot && !HasPendingRestore && !IsStaging;
    public bool CanChangeCache => !HasEnvironmentManagedRoot && !HasPendingRestore && !IsStaging;
    public string CatalogManagementNote => HasEnvironmentManagedRoot
        ? $"Moves are unavailable while {ManagedEnvironmentVariables} manages a storage location. Remove or repoint it first."
        : "Catalog database and presets. Moves run safely at next launch.";
    public string CacheManagementNote => HasEnvironmentManagedRoot
        ? $"Moves are unavailable while {ManagedEnvironmentVariables} manages a storage location. Remove or repoint it first."
        : "Regenerable thumbnails and previews. Moves run safely at next launch.";
    public bool ShowCatalogUninstallWarning =>
        _isPackagedWindows && IsUnderLocalAppData(PendingCatalogRoot ?? CatalogRoot);

    partial void OnPendingCatalogRootChanged(string? value) =>
        OnPropertyChanged(nameof(ShowCatalogUninstallWarning));

    public Func<bool, Task<string?>>? RequestDestinationAsync { get; set; }

    [RelayCommand]
    private async Task RevealCatalogAsync() =>
        await _fileOperationService.OpenFolderAsync(CatalogRoot);

    [RelayCommand]
    private async Task RevealCacheAsync() =>
        await _fileOperationService.OpenFolderAsync(CacheRoot);

    [RelayCommand(CanExecute = nameof(CanChangeCatalog))]
    private Task ChangeCatalogAsync() => GuardAsync(catalog: true, async () =>
    {
        PendingCatalogRoot = RequestDestinationAsync == null
            ? null
            : await RequestDestinationAsync(true);
        MoveCatalogCommand.NotifyCanExecuteChanged();
    });

    [RelayCommand(CanExecute = nameof(CanChangeCache))]
    private Task ChangeCacheAsync() => GuardAsync(catalog: false, async () =>
    {
        PendingCacheRoot = RequestDestinationAsync == null
            ? null
            : await RequestDestinationAsync(false);
        MoveCacheCommand.NotifyCanExecuteChanged();
    });

    [RelayCommand(CanExecute = nameof(CanMoveCatalog))]
    private Task MoveCatalogAsync() => GuardAsync(catalog: true, async () =>
    {
        await _migrator.StageMoveAsync(
            _locations,
            CatalogLocationMoveKind.Catalog,
            PendingCatalogRoot);
        await RefreshPendingAsync();
    });

    [RelayCommand(CanExecute = nameof(CanMoveCache))]
    private Task MoveCacheAsync() => GuardAsync(catalog: false, async () =>
    {
        await _migrator.StageMoveAsync(
            _locations,
            CatalogLocationMoveKind.Cache,
            PendingCacheRoot);
        await RefreshPendingAsync();
    });

    // Command exceptions otherwise escape through async void into the
    // dispatcher and crash the app; a refusal must land inside the card whose
    // button was clicked.
    private async Task GuardAsync(bool catalog, Func<Task> action)
    {
        try
        {
            CatalogError = null;
            CacheError = null;
            await action();
        }
        catch (Exception exception)
        {
            if (catalog) CatalogError = exception.Message;
            else CacheError = exception.Message;
        }
    }

    private CatalogLocationMoveJournal? _pendingJournal;

    public bool HasPendingRestore => _pendingJournal?.Kind == CatalogLocationMoveKind.Restore;

    private bool HasStagedMove => _pendingJournal != null || File.Exists(_migrator.JournalPath);

    private bool IsStaging => RestoreBackupCommand.IsRunning || MoveCatalogCommand.IsRunning || MoveCacheCommand.IsRunning;

    private bool CanRestore => !HasStagedMove && !IsStaging;

    internal Func<Task<(string Path, bool Acknowledged, CheckedCatalogBackup Check)?>>? RequestRestoreAsync { get; set; }

    internal Task RefreshPendingAsync() => GuardAsync(catalog: true, async () =>
    {
        _pendingJournal = File.Exists(_migrator.JournalPath) ? await _migrator.ReadJournalAsync() : null;
        CatalogStatus = _pendingJournal?.Kind == CatalogLocationMoveKind.Catalog
            ? "Catalog move staged for the next launch." : null;
        CacheStatus = _pendingJournal?.Kind == CatalogLocationMoveKind.Cache
            ? "Cache move staged for the next launch." : null;
        if (_pendingJournal?.Restore is { } restore)
            CatalogStatus = $"Restore from {restore.BackupUtc?.ToString("g") ?? Path.GetFileName(restore.BackupPath)} staged for the next launch.";

        OnPropertyChanged(nameof(HasPendingRestore));
        NotifyAvailability();
    });

    private void NotifyAvailability()
    {
        OnPropertyChanged(nameof(CanChangeCatalog));
        OnPropertyChanged(nameof(CanChangeCache));
        ChangeCatalogCommand.NotifyCanExecuteChanged();
        ChangeCacheCommand.NotifyCanExecuteChanged();
        MoveCatalogCommand.NotifyCanExecuteChanged();
        MoveCacheCommand.NotifyCanExecuteChanged();
        RestoreBackupCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanRestore))]
    private Task RestoreBackupAsync() => GuardAsync(catalog: true, async () =>
    {
        if (RequestRestoreAsync == null || !CanRestore) return;
        if (await RequestRestoreAsync() is not { } chosen) return;
        await new CatalogRestoreExecutor(_migrator) { Step = _migrator.RestoreStep }.StageAsync(
            _locations, chosen.Path, chosen.Check, chosen.Acknowledged);
        await RefreshPendingAsync();
    });

    [RelayCommand]
    private Task CancelRestoreAsync() => GuardAsync(catalog: true, async () =>
    {
        if (File.Exists(_migrator.JournalPath) && await _migrator.ReadJournalAsync() is
            { Kind: CatalogLocationMoveKind.Restore, Restore.Phase: CatalogRestorePhase.Prepared })
            _migrator.DeleteJournal();
        await RefreshPendingAsync();
    });

    private bool CanMoveCatalog() =>
        CanChangeCatalog && PendingCatalogRoot != null && !HasStagedMove;

    private bool CanMoveCache() =>
        CanChangeCache && PendingCacheRoot != null && !HasStagedMove;

    private string ManagedEnvironmentVariables => string.Join(
        " and ",
        new[]
        {
            _locations.IsCatalogEnvironmentManaged
                ? AppDataLocationService.CatalogEnvironmentVariable
                : null,
            _locations.IsCacheEnvironmentManaged
                ? AppDataLocationService.CacheEnvironmentVariable
                : null
        }.Where(name => name != null));

    private static bool IsUnderLocalAppData(string path)
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return !string.IsNullOrWhiteSpace(local) &&
               AppDataRootOwnership.IsSameOrDescendant(
                   Path.GetFullPath(local), Path.GetFullPath(path));
    }
}

internal static partial class PackagedWindowsDetector
{
    public static bool IsPackaged { get; } = Detect();

    private static bool Detect()
    {
        if (!OperatingSystem.IsWindows()) return false;
        uint length = 0;
        return GetCurrentPackageFullName(ref length, null) != 15700;
    }

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int GetCurrentPackageFullName(
        ref uint packageFullNameLength,
        char[]? packageFullName);
}
