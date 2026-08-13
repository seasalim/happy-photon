using System.Diagnostics;
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

    public StorageSettingsViewModel(
        AppDataLocations locations,
        CatalogLocationMigrator migrator)
        : this(locations, migrator, PackagedWindowsDetector.IsPackaged)
    {
    }

    internal StorageSettingsViewModel(
        AppDataLocations locations,
        CatalogLocationMigrator migrator,
        bool isPackagedWindows)
    {
        _locations = locations;
        _migrator = migrator;
        _isPackagedWindows = isPackagedWindows;
        CatalogRoot = locations.CatalogRoot;
        CacheRoot = locations.CacheRoot;
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
    public bool CanChangeCatalog => !HasEnvironmentManagedRoot;
    public bool CanChangeCache => !HasEnvironmentManagedRoot;
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
    private void RevealCatalog() => Reveal(CatalogRoot);

    [RelayCommand]
    private void RevealCache() => Reveal(CacheRoot);

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
        CatalogStatus = "Catalog move staged for the next launch.";
        MoveCatalogCommand.NotifyCanExecuteChanged();
        MoveCacheCommand.NotifyCanExecuteChanged();
    });

    [RelayCommand(CanExecute = nameof(CanMoveCache))]
    private Task MoveCacheAsync() => GuardAsync(catalog: false, async () =>
    {
        await _migrator.StageMoveAsync(
            _locations,
            CatalogLocationMoveKind.Cache,
            PendingCacheRoot);
        CacheStatus = "Cache move staged for the next launch.";
        MoveCatalogCommand.NotifyCanExecuteChanged();
        MoveCacheCommand.NotifyCanExecuteChanged();
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

    // The journal holds a single staged move, so either staged status
    // disables both MOVE buttons until the next launch executes it.
    private bool HasStagedMove => CatalogStatus != null || CacheStatus != null;

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

    private static void Reveal(string path)
    {
        if (!Directory.Exists(path)) return;
        var start = OperatingSystem.IsWindows()
            ? new ProcessStartInfo("explorer.exe", $"\"{path}\"")
            : OperatingSystem.IsMacOS()
                ? new ProcessStartInfo("open", path)
                : new ProcessStartInfo("xdg-open", path);
        start.UseShellExecute = false;
        Process.Start(start);
    }

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
