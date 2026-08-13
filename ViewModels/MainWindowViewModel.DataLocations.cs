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
    private bool _setupUsesStandardCatalog;

    [ObservableProperty]
    private bool _showSetupUninstallWarning;

    [ObservableProperty]
    private StorageSettingsViewModel? _storageSettings;

    [ObservableProperty]
    private bool _isDataLocationBusy;

    [ObservableProperty]
    private bool _isSchemaMismatch;

    public Func<Task>? CompleteDataLocationSetupAsync { get; set; }
    public Func<bool, Task>? ChangeSetupLocationAsync { get; set; }
    public Func<Task>? LocateExistingCatalogAsync { get; set; }
    public Func<Task>? RecoverLocationPointerAsync { get; set; }
    public Func<Task>? SetAsideCatalogAsync { get; set; }

    public bool IsDataLocationSetupVisible =>
        StartupGateState == StartupGateState.DataLocations;
    public bool IsPointerRecoveryVisible =>
        StartupGateState == StartupGateState.PointerRecovery;
    public bool CanSetAsideCatalog =>
        IsSchemaMismatch && StartupGateState == StartupGateState.Error &&
        SetAsideCatalogAsync != null &&
        StorageSettings is { CanChangeCatalog: true, CanChangeCache: true };

    public void ShowDataLocationSetup(AppDataLocationService service)
    {
        SetupCatalogRoot = service.DefaultCatalogRoot;
        SetupCacheRoot = service.StandardCacheRoot;
        SetupUsesStandardCatalog = false;
        ShowSetupUninstallWarning = false;
        FirstRunErrorMessage = null;
        StartupGateState = StartupGateState.DataLocations;
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

    [RelayCommand]
    private void UsePicturesCatalog()
    {
        SetupUsesStandardCatalog = false;
    }

    [RelayCommand]
    private void UseStandardCatalog()
    {
        SetupUsesStandardCatalog = true;
    }

    [RelayCommand]
    private Task ChangeSetupCatalogAsync() => GuardGateActionAsync(() =>
        ChangeSetupLocationAsync?.Invoke(true) ?? Task.CompletedTask);

    [RelayCommand]
    private Task ChangeSetupCacheAsync() => GuardGateActionAsync(() =>
        ChangeSetupLocationAsync?.Invoke(false) ?? Task.CompletedTask);

    [RelayCommand]
    private async Task CompleteStorageSetupAsync()
    {
        if (CompleteDataLocationSetupAsync == null || IsDataLocationBusy) return;
        IsDataLocationBusy = true;
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
        }
    }

    [RelayCommand]
    private Task LocateExistingCatalog() => GuardGateActionAsync(() =>
        LocateExistingCatalogAsync?.Invoke() ?? Task.CompletedTask);

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

    partial void OnSetupUsesStandardCatalogChanged(bool value)
    {
        if (_dataLocationService == null) return;
        SetupCatalogRoot = value
            ? _dataLocationService.StandardCatalogRoot
            : _dataLocationService.DefaultCatalogRoot;
        UpdateSetupWarning();
    }

    partial void OnSetupCatalogRootChanged(string? value) => UpdateSetupWarning();

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
