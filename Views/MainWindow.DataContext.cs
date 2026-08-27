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
    private MainWindowViewModel? _exportWorkspaceSettingsViewModel;
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

        vm.ApplyFitZoom(zoomControl.GetFitZoomLevel());
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        _isDevelopViewportPublicationSuppressed = false;

        if (_subscribedViewModel != null && !ReferenceEquals(DataContext, _subscribedViewModel))
        {
            SetExportWorkspaceSettingsSubscription(null);
            _subscribedViewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _subscribedViewModel = null;
        }

        if (DataContext is not MainWindowViewModel vm)
        {
            _presetsPanel?.SetPresetSource(null);
            return;
        }

        vm.ZoomFitCommand = new RelayCommand(ZoomFit);
        vm.RequestZoomFit = () => GetActiveZoomPanControl()?.RequestFitToView(vm.ApplyFitZoom);
        vm.ConfirmExportOverwriteAsync = ConfirmExportOverwriteAsync;
        vm.ConfirmExportHydrationAsync = ConfirmExportHydrationAsync;
        vm.RequestCatalogImportAsync = async () =>
            await ShowImportCatalogAsync();
        vm.CaptureBrowseViewportAnchor =
            () => _browseGridView?.CaptureViewportAnchorPath();
        vm.RestoreBrowseViewportAnchor =
            path => _browseGridView?.RestoreViewportAnchorPath(path);
        vm.RequestSettingsDialogAsync = async () =>
            await new SettingsDialog(vm).ShowDialog(this);
        vm.ConfirmMoveToTrashAsync = ConfirmMoveToTrashAsync;
        vm.RequestVersionLabelAsync = image => TextInputDialog.ShowAsync(
            this,
            "Rename Version",
            "Version label (leave blank to use V-number):",
            image.VersionLabel ?? string.Empty);
        vm.ConfirmDeleteRejectedAsync = ConfirmDeleteRejectedAsync;
        vm.CancelActiveLoupePeek = () =>
            _compareView?.CancelLoupePeek() == true ||
            GetActiveZoomPanControl()?.CancelLoupePeek() == true;
        vm.ConfirmBatchApplyAsync = ConfirmBatchApplyAsync;
        vm.ShowFileOperationFailuresAsync = ShowFileOperationFailuresAsync;
        vm.PersistAppSettingsAsync = () => SaveAppSettingsAsync(vm);
        vm.PersistFirstRunCompletionAsync =
            path => PersistFirstRunCompletionAsync(vm, path);
        vm.DetectLightroomAsync = DetectLightroomAsync;
        vm.RequestFirstRunCatalogImportAsync = ShowFirstRunImportCatalogAsync;
        vm.RequestFirstRunCatalogPathAsync = PickLightroomCatalogPathAsync;
        vm.BrowseLocationRequested = () => _ = BrowseForFirstRunLocationAsync(vm);
        vm.RetryStartupAsync = () => TryInitializeApplicationAsync(vm);
        vm.CompleteDataLocationSetupAsync = () =>
            CompleteDataLocationSetupAsync(vm);
        vm.ChangeSetupLocationAsync = catalog =>
            ChangeSetupLocationAsync(vm, catalog);
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
        vm.LaunchUriAsync = uri => Launcher.LaunchUriAsync(uri);
        _presetsPanel?.SetPresetSource(vm.PresetService);

        SetSubscribedViewModel(vm);
        SetExportWorkspaceSettingsSubscription(vm.IsExportMode ? vm : null);
        ApplyAppTheme(vm.AppTheme);
        ApplyWorkspaceKeyboardState(vm.IsWorkspaceInteractionEnabled);
        UpdateDevelopViewportPublication(vm);
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
            await vm.EnsureRawRuntimeReadyAsync();
            await _locationMigrator.ExecutePendingAsync();
            _startupLocations = await _dataLocationService.ResolveAsync();
            if (_startupLocations == null)
            {
                vm.PrepareFirstRunStorage(_dataLocationService, null);
                vm.ShowFirstRunWelcome(_startupPicturesPath);
                return;
            }

            vm.SetResolvedDataLocations(_startupLocations, _locationMigrator);
            ConfigureStorageSettings(vm);
            vm.PrepareFirstRunStorage(_dataLocationService, _startupLocations);
            var hasCatalogSignature = AppDataLocationService.HasCatalogSignature(
                _startupLocations.CatalogRoot);
            if (!hasCatalogSignature &&
                !vm.IsFirstRunStorageCommitted)
            {
                vm.ShowFirstRunWelcome(_startupPicturesPath);
                return;
            }
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

            vm.Browse.FileTypeFilter = settings.FileTypeFilter;
            vm.SetColorLabelNames(colorLabelNames);
            await vm.RestoreXmpSettingsAsync();
            vm.RestoreBrowseThumbnailSize(settings.BrowseThumbnailSize);
            vm.RestoreAppTheme(settings.AppTheme);
            vm.ExportSettings.StripLocationData = settings.StripLocationData;
            vm.ExportSettings.OutputSharpening = settings.OutputSharpening;

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

            if (vm.IsFirstRunStorageCommitted)
                vm.ResumeFirstRunAfterStorage(_startupPicturesPath);
            else
            {
                vm.MarkFirstRunStorageCommitted();
                vm.ShowFirstRunWelcome(_startupPicturesPath);
            }
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
                    if (control == null)
                    {
                        ReleaseDevelopViewportPublication(vm, null);
                        return;
                    }
                    control.RequestFitToView(zoom =>
                    {
                        if (ReferenceEquals(control, GetActiveZoomPanControl()))
                        {
                            vm.ApplyFitZoom(zoom);
                            ReleaseDevelopViewportPublication(vm, control);
                        }
                    });
                },
                DispatcherPriority.Loaded);
        }
        else if (args.PropertyName ==
                 nameof(MainWindowViewModel.IsDevelopPreviewSurfaceActive))
        {
            UpdateDevelopViewportPublication(vm);
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
        else if (args.PropertyName == nameof(MainWindowViewModel.WorkspaceMode))
        {
            SetExportWorkspaceSettingsSubscription(vm.IsExportMode ? vm : null);
        }
        else if (args.PropertyName == nameof(MainWindowViewModel.IsCompareMode))
        {
            // The grid tile holding keyboard focus collapses when compare
            // opens; without an explicit handoff the window stops receiving
            // key events and Escape cannot reach OnWorkspaceKeyDown.
            Dispatcher.UIThread.Post(
                () =>
                {
                    if (!ReferenceEquals(DataContext, vm)) return;
                    if (vm.IsCompareMode)
                    {
                        _compareView?.Focus();
                    }
                    else if (vm.IsBrowseGridVisible)
                    {
                        // Only when the grid is actually showing: compare also
                        // closes when the workspace leaves Browse, and focusing
                        // a hidden control fails, stranding focus on the hidden
                        // compare view.
                        _browseGridView?.Focus();
                    }
                },
                DispatcherPriority.Loaded);
        }
    }

    private void SetExportWorkspaceSettingsSubscription(MainWindowViewModel? vm)
    {
        if (ReferenceEquals(_exportWorkspaceSettingsViewModel, vm)) return;
        if (_exportWorkspaceSettingsViewModel != null)
        {
            _exportWorkspaceSettingsViewModel.ExportSettings.PropertyChanged -=
                OnExportSettingsPropertyChanged;
        }
        _exportWorkspaceSettingsViewModel = vm;
        if (_exportWorkspaceSettingsViewModel != null)
        {
            _exportWorkspaceSettingsViewModel.ExportSettings.PropertyChanged +=
                OnExportSettingsPropertyChanged;
        }
    }

    private void UpdateDevelopViewportPublication(MainWindowViewModel vm)
    {
        if (!vm.IsDevelopPreviewSurfaceActive)
        {
            if (vm.IsFullScreenMode)
            {
                _isDevelopViewportPublicationSuppressed = true;
            }
            vm.PublishNavigatorVisibleRegion(null);
            return;
        }

        if (!_isDevelopViewportPublicationSuppressed)
        {
            _zoomPanControl?.RequestVisibleRegionPublication(force: true);
        }
    }

    private void ReleaseDevelopViewportPublication(
        MainWindowViewModel vm,
        ZoomPanControl? refittedControl)
    {
        if (vm.IsFullScreenMode)
        {
            return;
        }

        Dispatcher.UIThread.Post(
            () =>
            {
                if (!ReferenceEquals(DataContext, vm) ||
                    vm.IsFullScreenMode ||
                    (refittedControl != null &&
                     !ReferenceEquals(refittedControl, _zoomPanControl)))
                {
                    return;
                }

                _isDevelopViewportPublicationSuppressed = false;
                if (vm.IsDevelopPreviewSurfaceActive)
                {
                    _zoomPanControl?.RequestVisibleRegionPublication(force: true);
                }
            },
            DispatcherPriority.Render);
    }

    private static void ApplyAppTheme(AppTheme theme)
    {
        if (Application.Current is { } application)
        {
            application.RequestedThemeVariant = HappyPhotonThemes.For(theme);
        }
    }

}
