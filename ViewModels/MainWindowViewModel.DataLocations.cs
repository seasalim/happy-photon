using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HappyPhoton.Services;

namespace HappyPhoton.ViewModels;

public partial class MainWindowViewModel
{
    [ObservableProperty]
    private string? _setupCatalogRoot;

    [ObservableProperty]
    private string? _setupCacheRoot;

    [ObservableProperty]
    private bool _showSetupUninstallWarning;

    [ObservableProperty]
    private StorageSettingsViewModel? _storageSettings;

    [ObservableProperty]
    private bool _isDataLocationBusy;

    [ObservableProperty]
    private bool _isFirstRunStorageReadOnly;

    [ObservableProperty]
    private bool _isSchemaMismatch;

    public Func<Task>? CompleteDataLocationSetupAsync { get; set; }
    public Func<bool, Task>? ChangeSetupLocationAsync { get; set; }
    public Func<Task>? RecoverLocationPointerAsync { get; set; }
    public Func<Task>? SetAsideCatalogAsync { get; set; }

    public bool IsPointerRecoveryVisible =>
        StartupGateState == StartupGateState.PointerRecovery;
    public bool CanChangeFirstRunStorage =>
        IsFirstRunStorageStep && !IsFirstRunStorageReadOnly;
    public bool CanSetAsideCatalog =>
        IsSchemaMismatch && StartupGateState == StartupGateState.Error &&
        SetAsideCatalogAsync != null &&
        StorageSettings is { CanChangeCatalog: true, CanChangeCache: true };

    public void PrepareFirstRunStorage(
        AppDataLocationService service,
        AppDataLocations? resolvedLocations)
    {
        SetupCatalogRoot = resolvedLocations?.CatalogRoot ?? service.DefaultCatalogRoot;
        SetupCacheRoot = resolvedLocations?.CacheRoot ?? service.StandardCacheRoot;
        IsFirstRunStorageReadOnly = resolvedLocations != null ||
                                    IsFirstRunStorageCommitted;
        ShowSetupUninstallWarning = false;
        FirstRunErrorMessage = null;
        BindDataLocationService(service);
    }

    public void ShowPointerRecovery(string message)
    {
        FirstRunErrorMessage = message;
        StartupGateState = StartupGateState.PointerRecovery;
    }

    public void ShowCatalogSchemaMismatch(string message)
    {
        IsSchemaMismatch = true;
        ShowStartupFailure(
            StorageSettings is { CanChangeCatalog: true, CanChangeCache: true }
                ? message
                : $"{message} Remove or repoint the storage environment variables before setting these roots aside.");
        OnPropertyChanged(nameof(CanSetAsideCatalog));
        SetAsideCatalogCommand.NotifyCanExecuteChanged();
    }

    public void SetResolvedDataLocations(
        AppDataLocations locations,
        CatalogLocationMigrator migrator)
    {
        StorageSettings = new StorageSettingsViewModel(locations, migrator);
        IsSchemaMismatch = false;
    }

    [RelayCommand(CanExecute = nameof(CanChangeFirstRunStorage))]
    private Task ChangeSetupCatalogAsync() => GuardGateActionAsync(() =>
        ChangeSetupLocationAsync?.Invoke(true) ?? Task.CompletedTask);

    [RelayCommand(CanExecute = nameof(CanChangeFirstRunStorage))]
    private Task ChangeSetupCacheAsync() => GuardGateActionAsync(() =>
        ChangeSetupLocationAsync?.Invoke(false) ?? Task.CompletedTask);

    private async Task CompleteStorageSetupAsync()
    {
        if (CompleteDataLocationSetupAsync == null || IsDataLocationBusy) return;
        IsDataLocationBusy = true;
        IsFirstRunBusy = true;
        FirstRunErrorMessage = null;
        try
        {
            await CompleteDataLocationSetupAsync();
        }
        catch (Exception exception)
        {
            FirstRunErrorMessage = exception.Message;
        }
        finally
        {
            IsDataLocationBusy = false;
            IsFirstRunBusy = false;
        }
    }

    [RelayCommand]
    private Task RecoverLocationPointer() => GuardGateActionAsync(() =>
        RecoverLocationPointerAsync?.Invoke() ?? Task.CompletedTask);

    [RelayCommand(CanExecute = nameof(CanSetAsideCatalog))]
    private Task SetAsideCatalog() => GuardGateActionAsync(() =>
        SetAsideCatalogAsync?.Invoke() ?? Task.CompletedTask);

    // Gate command exceptions otherwise escape through async void into the
    // dispatcher and crash startup; refusals must land in the gate instead.
    private async Task GuardGateActionAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception exception)
        {
            FirstRunErrorMessage = exception.Message;
        }
    }

    partial void OnSetupCatalogRootChanged(string? value) => UpdateSetupWarning();

    partial void OnIsFirstRunStorageReadOnlyChanged(bool value)
    {
        OnPropertyChanged(nameof(CanChangeFirstRunStorage));
        ChangeSetupCatalogCommand.NotifyCanExecuteChanged();
        ChangeSetupCacheCommand.NotifyCanExecuteChanged();
    }

    private AppDataLocationService? _dataLocationService;

    internal void BindDataLocationService(AppDataLocationService service)
    {
        _dataLocationService = service;
        UpdateSetupWarning();
    }

    private void UpdateSetupWarning()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        ShowSetupUninstallWarning = PackagedWindowsDetector.IsPackaged &&
            !string.IsNullOrWhiteSpace(SetupCatalogRoot) &&
            !string.IsNullOrWhiteSpace(local) &&
            AppDataRootOwnership.IsSameOrDescendant(local, SetupCatalogRoot);
    }
}
