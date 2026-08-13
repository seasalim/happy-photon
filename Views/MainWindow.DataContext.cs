using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;

namespace HappyPhoton.Views;

public partial class MainWindow
{
    private AppSettingsService? _appSettingsService;
    private MainWindowViewModel? _subscribedViewModel;
    private CatalogService? _startupCatalogService;
    private AppDataLocationService? _dataLocationService;
    private CatalogLocationMigrator? _locationMigrator;
    private AppDataLocations? _startupLocations;
    private string? _startupPicturesPath;
    private bool _startupAttemptInProgress;
    private bool _isClosing;
    private bool _closeReady;

    private void ZoomFit()
    {
        var zoomControl = GetActiveZoomPanControl();
        if (zoomControl == null || DataContext is not MainWindowViewModel vm) return;

        vm.ZoomLevel = zoomControl.GetFitZoomLevel();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (_subscribedViewModel != null && !ReferenceEquals(DataContext, _subscribedViewModel))
        {
            _subscribedViewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _subscribedViewModel = null;
        }

        if (DataContext is not MainWindowViewModel vm)
        {
            _presetsPanel?.SetPresetSource(null);
            return;
        }

        vm.ZoomFitCommand = new RelayCommand(ZoomFit);
        vm.RequestZoomFit = () => GetActiveZoomPanControl()?.RequestFitToView(zoom => vm.ZoomLevel = zoom);
        vm.RequestExportDialogAsync = ShowExportDialogAsync;
        vm.RequestCatalogImportAsync = ShowImportCatalogAsync;
        vm.CaptureLibraryViewportAnchor =
            () => _libraryGridView?.CaptureViewportAnchorPath();
        vm.RestoreLibraryViewportAnchor =
            path => _libraryGridView?.RestoreViewportAnchorPath(path);
        vm.RequestSettingsDialogAsync = async () =>
            await new SettingsDialog(vm).ShowDialog(this);
        vm.ConfirmMoveToTrashAsync = ConfirmMoveToTrashAsync;
        vm.ConfirmDeleteRejectedAsync = ConfirmDeleteRejectedAsync;
        vm.ConfirmBatchApplyAsync = ConfirmBatchApplyAsync;
        vm.ShowDeleteRejectedFailuresAsync = ShowDeleteRejectedFailuresAsync;
        vm.PersistAppSettingsAsync = () => SaveAppSettingsAsync(vm);
        vm.PersistFirstRunCompletionAsync =
            path => PersistFirstRunCompletionAsync(vm, path);
        vm.PersistImportedFirstRunCompletionAsync =
            completion => PersistImportedFirstRunCompletionAsync(vm, completion);
        vm.BrowseLocationRequested = () => _ = BrowseForFirstRunLocationAsync(vm);
        vm.RetryStartupAsync = () => TryInitializeApplicationAsync(vm);
        vm.CompleteDataLocationSetupAsync = () =>
            CompleteDataLocationSetupAsync(vm);
        vm.ChangeSetupLocationAsync = catalog =>
            ChangeSetupLocationAsync(vm, catalog);
        vm.LocateExistingCatalogAsync = () => LocateExistingCatalogAsync(vm);
        vm.RecoverLocationPointerAsync = () => RecoverLocationPointerAsync(vm);
        vm.SetAsideCatalogAsync = () => SetAsideCatalogAsync(vm);
        vm.CloseApplicationRequested = Close;
        vm.RequestFolderTreeFocus = FocusFolderTree;
        vm.CopyToClipboardAsync = async text =>
        {
            if (Clipboard is { } clipboard)
            {
                await clipboard.SetTextAsync(text);
            }
        };
        _presetsPanel?.SetPresetSource(vm.PresetService);

        SetSubscribedViewModel(vm);
        ApplyAppTheme(vm.AppTheme);
        ApplyWorkspaceKeyboardState(vm.IsWorkspaceInteractionEnabled);
    }

    internal async Task InitializeApplicationAsync(
        MainWindowViewModel vm,
        CatalogService catalogService,
        AppDataLocationService locationService,
        CatalogLocationMigrator locationMigrator,
        string? picturesPath)
    {
        _startupCatalogService = catalogService;
        _dataLocationService = locationService;
        _locationMigrator = locationMigrator;
        vm.BindDataLocationService(locationService);
        _startupPicturesPath = picturesPath;
        _appSettingsService = new AppSettingsService(catalogService);
        await TryInitializeApplicationAsync(vm);
    }

    private async Task TryInitializeApplicationAsync(MainWindowViewModel vm)
    {
        if (_startupAttemptInProgress ||
            _appSettingsService == null ||
            _startupCatalogService == null ||
            _dataLocationService == null ||
            _locationMigrator == null ||
            !ReferenceEquals(DataContext, vm))
        {
            return;
        }

        _startupAttemptInProgress = true;
        vm.ShowInitializing();
        try
        {
            await _locationMigrator.ExecutePendingAsync();
            _startupLocations = await _dataLocationService.ResolveAsync();
            if (_startupLocations == null)
            {
                vm.ShowDataLocationSetup(_dataLocationService);
                return;
            }

            vm.SetResolvedDataLocations(_startupLocations, _locationMigrator);
            ConfigureStorageSettings(vm);
            await Task.Run(() =>
                _startupCatalogService.InitializeAsync(_startupLocations));
            await vm.InitializeAsync(_startupLocations);

            var settings = await _appSettingsService.LoadAsync();
            var colorLabelNames = await new ColorLabelNames(
                _startupCatalogService).LoadAsync();
            if (!ReferenceEquals(DataContext, vm))
            {
                return;
            }

            vm.Library.FileTypeFilter = settings.FileTypeFilter;
            vm.SetColorLabelNames(colorLabelNames);
            await vm.RestoreXmpSettingsAsync();
            vm.RestoreLibraryThumbnailSize(settings.LibraryThumbnailSize);
            vm.RestoreAppTheme(settings.AppTheme);
            vm.ExportSettings.StripLocationData = settings.StripLocationData;
            vm.ExportSettings.OutputSharpening = settings.OutputSharpening;
            vm.InitializeAgentSettings(settings.McpServerEnabled, settings.McpToken);

            var firstRunDecision =
                MainWindowViewModel.DecideFirstRunStartup(settings);
            if (firstRunDecision == FirstRunStartupDecision.Restore)
            {
                await RestoreFolderSessionAsync(vm, settings);
                vm.ShowWorkspaceReady(
                    settings.FirstRunExperienceVersion ??
                    MainWindowViewModel.CurrentFirstRunExperienceVersion);
                return;
            }

            if (firstRunDecision ==
                FirstRunStartupDecision.GrandfatherExistingInstallation)
            {
                await _appSettingsService.SaveFirstRunVersionAsync(
                    MainWindowViewModel.CurrentFirstRunExperienceVersion);
                await RestoreFolderSessionAsync(vm, settings);
                vm.ShowWorkspaceReady(
                    MainWindowViewModel.CurrentFirstRunExperienceVersion);
                return;
            }

            if (_startupPicturesPath != null)
            {
                await vm.InitializeFolderTreeWithRootAsync(
                    _startupPicturesPath,
                    selectFolder: false);
            }
            else
            {
                vm.ClearFolderTree();
            }

            vm.ShowFirstRunWelcome(_startupPicturesPath);
        }
        catch (AppDataLocationPointerException exception)
        {
            System.Diagnostics.Debug.WriteLine(
                $"Storage pointer recovery required: {exception}");
            vm.ShowPointerRecovery(exception.Message);
        }
        catch (CatalogSchemaMismatchException exception)
        {
            System.Diagnostics.Debug.WriteLine(
                $"Catalog schema mismatch: {exception}");
            vm.ShowCatalogSchemaMismatch(exception.Message);
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine(
                $"Startup initialization failed: {exception}");
            vm.ShowStartupFailure(exception.Message);
        }
        finally
        {
            _startupAttemptInProgress = false;
        }
    }

    private async Task CompleteDataLocationSetupAsync(MainWindowViewModel vm)
    {
        if (_dataLocationService == null ||
            string.IsNullOrWhiteSpace(vm.SetupCatalogRoot) ||
            string.IsNullOrWhiteSpace(vm.SetupCacheRoot))
        {
            throw new InvalidOperationException("Choose both storage locations.");
        }
        await _dataLocationService.CreateFreshAsync(
            catalogRoot: vm.SetupCatalogRoot,
            cacheRoot: vm.SetupCacheRoot);
        await TryInitializeApplicationAsync(vm);
    }

    private async Task ChangeSetupLocationAsync(
        MainWindowViewModel vm,
        bool catalog)
    {
        var destination = await PickDataLocationAsync(catalog);
        if (destination == null || !ReferenceEquals(DataContext, vm)) return;
        if (catalog) vm.SetupCatalogRoot = destination;
        else vm.SetupCacheRoot = destination;
    }

    private async Task LocateExistingCatalogAsync(MainWindowViewModel vm)
    {
        if (_dataLocationService == null) return;
        var root = await PickRawFolderAsync("Locate the Happy Photon Catalog");
        if (root == null) return;
        await _dataLocationService.AdoptLocatedCatalogAsync(root);
        await TryInitializeApplicationAsync(vm);
    }

    private async Task RecoverLocationPointerAsync(MainWindowViewModel vm)
    {
        if (_dataLocationService == null) return;
        await _dataLocationService.QuarantineCorruptPointerAsync();
        await TryInitializeApplicationAsync(vm);
    }

    private async Task SetAsideCatalogAsync(MainWindowViewModel vm)
    {
        if (_locationMigrator == null || _startupLocations == null) return;
        await _locationMigrator.StageMoveAsync(
            _startupLocations,
            CatalogLocationMoveKind.SetAside);
        await TryInitializeApplicationAsync(vm);
    }

    private void ConfigureStorageSettings(MainWindowViewModel vm)
    {
        if (vm.StorageSettings == null) return;
        vm.StorageSettings.RequestDestinationAsync = PickDataLocationAsync;
    }

    private async Task<string?> PickDataLocationAsync(bool catalog)
    {
        var parent = await PickRawFolderAsync(
            catalog ? "Choose a Catalog Parent Folder" : "Choose a Cache Parent Folder");
        return parent == null
            ? null
            : AppDataRootOwnership.CreateDedicatedChild(
                parent,
                catalog ? "Happy Photon Catalog" : "Happy Photon Cache",
                _startupLocations == null
                    ? null
                    : [_startupLocations.CatalogRoot, _startupLocations.CacheRoot]);
    }

    private async Task<string?> PickRawFolderAsync(string title)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(
            new Avalonia.Platform.Storage.FolderPickerOpenOptions
            {
                Title = title,
                AllowMultiple = false
            });
        return folders.Count == 0 ? null : folders[0].Path.LocalPath;
    }

    private async void OnExportSettingsPropertyChanged(
        object? sender,
        PropertyChangedEventArgs args)
    {
        if (sender is not ExportSettings settings ||
            DataContext is not MainWindowViewModel vm ||
            !ReferenceEquals(settings, vm.ExportSettings))
        {
            return;
        }

        if (args.PropertyName is not nameof(ExportSettings.StripLocationData) and
            not nameof(ExportSettings.OutputSharpening))
        {
            return;
        }

        await PersistAppSettingsSafelyAsync(vm);
    }

    private async Task RestoreFolderSessionAsync(
        MainWindowViewModel vm,
        AppSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.RootFolderPath) &&
            vm.ValidateBrowseLocation(settings.RootFolderPath) ==
            BrowseLocationValidation.Valid)
        {
            await vm.InitializeFolderTreeWithRootAsync(
                settings.RootFolderPath,
                settings.SelectedFolderPath);
            return;
        }

        if (_startupPicturesPath != null)
        {
            await vm.InitializeFolderTreeWithRootAsync(_startupPicturesPath);
            return;
        }

        vm.ClearFolderTree();
    }

    private void SetSubscribedViewModel(MainWindowViewModel vm)
    {
        if (_subscribedViewModel != null)
        {
            _subscribedViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _subscribedViewModel = vm;
        _subscribedViewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (sender is not MainWindowViewModel vm)
        {
            return;
        }

        if (args.PropertyName == nameof(MainWindowViewModel.IsFullScreenMode))
        {
            ApplyFullScreenWindowState(vm.IsFullScreenMode);
            Dispatcher.UIThread.Post(
                () =>
                {
                    if (!ReferenceEquals(DataContext, vm)) return;
                    var control = GetActiveZoomPanControl();
                    control?.RequestFitToView(zoom =>
                    {
                        if (ReferenceEquals(control, GetActiveZoomPanControl()))
                        {
                            vm.ZoomLevel = zoom;
                        }
                    });
                },
                DispatcherPriority.Loaded);
        }
        else if (args.PropertyName ==
                 nameof(MainWindowViewModel.IsWorkspaceInteractionEnabled))
        {
            ApplyWorkspaceKeyboardState(vm.IsWorkspaceInteractionEnabled);
        }
        else if (args.PropertyName == nameof(MainWindowViewModel.AppTheme))
        {
            ApplyAppTheme(vm.AppTheme);
        }
    }

    private static void ApplyAppTheme(AppTheme theme)
    {
        if (Application.Current is { } application)
        {
            application.RequestedThemeVariant = HappyPhotonThemes.For(theme);
        }
    }

    protected override async void OnClosing(WindowClosingEventArgs e)
    {
        if (_closeReady || DataContext is not MainWindowViewModel vm)
        {
            base.OnClosing(e);
            return;
        }

        e.Cancel = true;
        if (_isClosing) return;

        _isClosing = true;
        await PersistAppSettingsSafelyAsync(vm);

        try
        {
            await vm.ShutdownAgentServerAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Agent server shutdown failed: {ex.Message}");
        }

        Hide();
        DataContext = null;
        await Dispatcher.UIThread.InvokeAsync(
            static () => { },
            DispatcherPriority.Loaded);

        try
        {
            await vm.DisposeAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Image service shutdown failed: {ex.Message}");
        }

        _closeReady = true;
        Close();
    }

    private async Task PersistAppSettingsSafelyAsync(MainWindowViewModel vm)
    {
        try
        {
            await SaveAppSettingsAsync(vm);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"App settings persistence failed: {ex.Message}");
        }
    }

    private Task SaveAppSettingsAsync(MainWindowViewModel vm)
    {
        if (_appSettingsService == null)
        {
            return Task.CompletedTask;
        }

        var settings = new AppSettings
        {
            RootFolderPath = vm.RootFolders.FirstOrDefault()?.Path,
            SelectedFolderPath = vm.CurrentFolderPath,
            FirstRunExperienceVersion = vm.FirstRunExperienceVersion,
            FileTypeFilter = vm.Library.FileTypeFilter,
            LibraryThumbnailSize = vm.LibraryThumbnailSize,
            AppTheme = vm.AppTheme,
            StripLocationData = vm.ExportSettings.StripLocationData,
            OutputSharpening = vm.ExportSettings.OutputSharpening,
            McpServerEnabled = vm.IsAgentServerEnabled,
            McpToken = vm.AgentToken
        };

        return vm.CanPersistFolderSession
            ? _appSettingsService.SaveAsync(settings)
            : _appSettingsService.SavePreferencesAsync(settings);
    }
}
