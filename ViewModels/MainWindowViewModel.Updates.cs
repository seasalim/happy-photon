using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HappyPhoton.Services;

namespace HappyPhoton.ViewModels;

public partial class MainWindowViewModel
{
    private readonly UpdateCheckService _updateCheckService;
    private readonly UpdateInstallChannel _updateInstallChannel;
    private readonly CancellationTokenSource _updatesLifetimeCts = new();
    private Task? _manualUpdateTask;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CheckForUpdatesCommand))]
    private bool _isUpdateCheckBusy;

    [ObservableProperty]
    private string _updateStatusText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsUpdateAvailable))]
    [NotifyPropertyChangedFor(nameof(UpgradeUri))]
    [NotifyPropertyChangedFor(nameof(UpdateChannelText))]
    [NotifyPropertyChangedFor(nameof(UpgradeActionText))]
    private UpdateCheckResult? _latestUpdateResult;

    public bool IsUpdateAvailable =>
        LatestUpdateResult?.Status == UpdateCheckStatus.UpdateAvailable;

    public Uri? UpgradeUri => !IsUpdateAvailable
        ? null
        : _updateInstallChannel == UpdateInstallChannel.MicrosoftStore
            ? new Uri(UpdateChannelSelector.MicrosoftStoreUri)
            : LatestUpdateResult?.ReleaseUri;

    public string UpdateChannelText => _updateInstallChannel ==
        UpdateInstallChannel.MicrosoftStore
        ? "The Microsoft Store manages updates for this installation."
        : "Download the update from the Happy Photon release page.";

    public string UpgradeActionText => _updateInstallChannel ==
        UpdateInstallChannel.MicrosoftStore
        ? "Open Microsoft Store"
        : "View release";

    [RelayCommand(CanExecute = nameof(CanCheckForUpdates))]
    private Task CheckForUpdatesAsync()
    {
        _manualUpdateTask = RunManualUpdateCheckAsync();
        return _manualUpdateTask;
    }

    private async Task RunManualUpdateCheckAsync()
    {
        IsUpdateCheckBusy = true;
        try
        {
            var task = Task.Run(
                () => _updateCheckService.CheckAsync(_updatesLifetimeCts.Token),
                _updatesLifetimeCts.Token);
            var result = await task.ConfigureAwait(false);
            await ApplyUpdateResultOnUiThreadAsync(result).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_updatesLifetimeCts.IsCancellationRequested)
        {
        }
        finally
        {
            await Dispatcher.UIThread.InvokeAsync(() => IsUpdateCheckBusy = false);
        }
    }

    private bool CanCheckForUpdates() => !IsUpdateCheckBusy;

    private async Task ApplyUpdateResultOnUiThreadAsync(UpdateCheckResult result)
    {
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            LatestUpdateResult = result;
            UpdateStatusText = result.Status switch
            {
                UpdateCheckStatus.UpToDate => "Happy Photon is up to date.",
                UpdateCheckStatus.UpdateAvailable =>
                    $"Update available · v{result.Version}",
                _ => "Couldn’t check for updates. Try again later."
            };
        });
    }

    private async Task DisposeUpdatesAsync()
    {
        _updatesLifetimeCts.Cancel();
        try
        {
            if (_manualUpdateTask != null)
            {
                await _manualUpdateTask;
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            _updatesLifetimeCts.Dispose();
        }
    }
}
