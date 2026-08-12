using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HappyPhoton.Services;

namespace HappyPhoton.ViewModels;

public partial class MainWindowViewModel
{
    internal const string XmpSidecarModeKey = "XmpSidecarMode";
    internal const string XmpSidecarNamingKey = "XmpSidecarNaming";
    private bool _restoringXmpSettings;
    private readonly SemaphoreSlim _xmpSettingsGate = new(1, 1);

    [ObservableProperty]
    private XmpSidecarMode _xmpSidecarMode;

    [ObservableProperty]
    private XmpSidecarNaming _xmpSidecarNaming = XmpSidecarNaming.FullName;

    [ObservableProperty]
    private bool _areXmpSettingsReady;

    public bool IsXmpReadWrite => XmpSidecarMode == XmpSidecarMode.ReadWrite;
    public Func<Task>? RequestSettingsDialogAsync { get; set; }

    public async Task RestoreXmpSettingsAsync()
    {
        var mode = ParseSetting(
            await _catalogService.GetAppSettingAsync(XmpSidecarModeKey),
            XmpSidecarMode.Off);
        var naming = ParseSetting(
            await _catalogService.GetAppSettingAsync(XmpSidecarNamingKey),
            XmpSidecarNaming.FullName);
        _restoringXmpSettings = true;
        try
        {
            XmpSidecarNaming = naming;
            XmpSidecarMode = mode;
        }
        finally
        {
            _restoringXmpSettings = false;
        }
        AreXmpSettingsReady = true;
        await ApplyXmpModeTransitionAsync(mode);
    }

    partial void OnXmpSidecarModeChanged(XmpSidecarMode oldValue, XmpSidecarMode newValue)
    {
        OnPropertyChanged(nameof(IsXmpReadWrite));
        if (!_restoringXmpSettings && AreXmpSettingsReady)
            _ = ChangeXmpModeAsync();
    }

    partial void OnXmpSidecarNamingChanged(XmpSidecarNaming value)
    {
        if (!_restoringXmpSettings && AreXmpSettingsReady)
            _ = PersistXmpNamingAsync();
    }

    [RelayCommand]
    private async Task OpenSettingsAsync()
    {
        if (RequestSettingsDialogAsync != null)
            await RequestSettingsDialogAsync();
    }

    private async Task ChangeXmpModeAsync()
    {
        await _xmpSettingsGate.WaitAsync();
        try
        {
            var desired = XmpSidecarMode;
            await ApplyXmpModeTransitionAsync(desired);
            await PersistXmpSettingAsync(XmpSidecarModeKey, desired.ToString());
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine(
                $"XMP mode change failed: {exception.Message}");
            ShowTransientStatus("Unable to change XMP sidecar mode");
        }
        finally
        {
            _xmpSettingsGate.Release();
        }
    }

    private async Task PersistXmpNamingAsync()
    {
        await _xmpSettingsGate.WaitAsync();
        try
        {
            await PersistXmpSettingAsync(
                XmpSidecarNamingKey, XmpSidecarNaming.ToString());
        }
        finally
        {
            _xmpSettingsGate.Release();
        }
    }

    private Task PersistXmpSettingAsync(string key, string value) =>
        _catalogService.SetAppSettingAsync(key, value);

    private static T ParseSetting<T>(string? value, T fallback)
        where T : struct, Enum =>
        Enum.TryParse<T>(value, ignoreCase: true, out var parsed) &&
        Enum.IsDefined(parsed)
            ? parsed
            : fallback;
}
