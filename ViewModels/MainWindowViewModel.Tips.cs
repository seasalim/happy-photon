using CommunityToolkit.Mvvm.Input;
using HappyPhoton.Models;

namespace HappyPhoton.ViewModels;

public partial class MainWindowViewModel
{
    private bool _tipsSettingsApplied;

    private bool _showTips = true;

    private bool _browseTipsSeen;

    private bool _developTipsSeen;

    private bool _exportTipsSeen;

    private bool CanShowTips => _tipsSettingsApplied && _showTips &&
        StartupGateState == StartupGateState.Ready &&
        FirstRunExperienceVersion >= CurrentFirstRunExperienceVersion && !IsFullScreenMode;

    public bool IsBrowseTipsVisible => CanShowTips && !_browseTipsSeen && IsBrowseGridVisible;

    public bool IsDevelopTipsVisible => CanShowTips && !_developTipsSeen && IsDevelopMode;

    public bool IsExportTipsVisible => CanShowTips && !_exportTipsSeen && IsExportMode;

    public Func<Task>? RequestKeyboardShortcutsAsync { get; set; }

    public void RestoreTipsSettings(AppSettings settings)
    {
        _showTips = settings.ShowTips;
        _browseTipsSeen = settings.BrowseTipsSeen;
        _developTipsSeen = settings.DevelopTipsSeen;
        _exportTipsSeen = settings.ExportTipsSeen;
        _tipsSettingsApplied = true;
        NotifyTipsVisibilityChanged();
    }

    public bool CaptureTipsSettings(AppSettings settings)
    {
        if (!_tipsSettingsApplied) return false;

        settings.ShowTips = _showTips;
        settings.BrowseTipsSeen = _browseTipsSeen;
        settings.DevelopTipsSeen = _developTipsSeen;
        settings.ExportTipsSeen = _exportTipsSeen;

        return true;
    }

    [RelayCommand]
    private void DismissTips()
    {
        if (IsBrowseTipsVisible) _browseTipsSeen = true;
        else if (IsDevelopTipsVisible) _developTipsSeen = true;
        else if (IsExportTipsVisible) _exportTipsSeen = true;

        NotifyTipsVisibilityChanged();
    }

    [RelayCommand]
    private Task OpenKeyboardShortcutsAsync() =>
        RequestKeyboardShortcutsAsync?.Invoke() ?? Task.CompletedTask;

    private void NotifyTipsVisibilityChanged()
    {
        OnPropertyChanged(nameof(IsBrowseTipsVisible));
        OnPropertyChanged(nameof(IsDevelopTipsVisible));
        OnPropertyChanged(nameof(IsExportTipsVisible));
    }
}
