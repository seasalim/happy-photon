using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using CommunityToolkit.Mvvm.Input;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;

namespace HappyPhoton.Views;

public partial class MainWindow
{
    private AppSettingsService? _appSettingsService;
    private MainWindowViewModel? _subscribedViewModel;
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
        vm.RequestExport = () => OnExportRequested(this, EventArgs.Empty);
        vm.ConfirmMoveToTrashAsync = ConfirmMoveToTrashAsync;
        vm.ConfirmDeleteRejectedAsync = ConfirmDeleteRejectedAsync;
        vm.ConfirmBatchApplyAsync = ConfirmBatchApplyAsync;
        vm.ShowDeleteRejectedFailuresAsync = ShowDeleteRejectedFailuresAsync;
        vm.PersistAppSettingsAsync = () => SaveAppSettingsAsync(vm);
        vm.CopyToClipboardAsync = async text =>
        {
            if (Clipboard is { } clipboard)
            {
                await clipboard.SetTextAsync(text);
            }
        };
        _presetsPanel?.SetPresetSource(vm.PresetService);

        SetSubscribedViewModel(vm);

        _appSettingsService = new AppSettingsService(App.CatalogService!);
    }

    internal async Task RestoreSessionAsync(MainWindowViewModel vm)
    {
        if (_appSettingsService == null || !ReferenceEquals(DataContext, vm))
        {
            return;
        }

        var settings = await _appSettingsService.LoadAsync();
        if (!ReferenceEquals(DataContext, vm)) return;

        vm.Library.FileTypeFilter = settings.FileTypeFilter;
        if (!string.IsNullOrEmpty(settings.RootFolderPath) && Directory.Exists(settings.RootFolderPath))
        {
            await vm.InitializeFolderTreeWithRootAsync(settings.RootFolderPath, settings.SelectedFolderPath);
        }
        else
        {
            await vm.InitializeFolderTreeAsync(settings.SelectedFolderPath);
        }

        if (!ReferenceEquals(DataContext, vm)) return;
        vm.InitializeAgentSettings(settings.McpServerEnabled, settings.McpToken);
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

        if (args.PropertyName == nameof(MainWindowViewModel.IsExportPanelVisible) &&
            vm.IsExportPanelVisible)
        {
            UpdateExportPanel(vm);
        }
        else if (args.PropertyName == nameof(MainWindowViewModel.IsFullScreenMode))
        {
            ApplyFullScreenWindowState(vm.IsFullScreenMode);
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

        return _appSettingsService.SaveAsync(new AppSettings
        {
            RootFolderPath = vm.RootFolders.FirstOrDefault()?.Path,
            SelectedFolderPath = vm.CurrentFolderPath,
            FileTypeFilter = vm.Library.FileTypeFilter,
            McpServerEnabled = vm.IsAgentServerEnabled,
            McpToken = vm.AgentToken
        });
    }
}
