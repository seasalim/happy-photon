using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HappyPhoton.Models;

namespace HappyPhoton.ViewModels;

public partial class MainWindowViewModel
{
    [ObservableProperty] private bool _isLocalLuminanceExpanded;
    public object LocalRangeEditIdentity => (SelectedImage, _selectedLocalId);
    public bool IsLocalLuminanceEnabled => SelectedLocal?.Luminance?.Enabled == true;
    public bool CanEditLocalLuminance => CanEditLocalGeometry && IsLocalLuminanceEnabled;
    public double LocalLuminanceLower
    {
        get => (SelectedLocal?.Luminance?.Lower ?? 0) * 100;
        set => SetLocalLuminance(value, 0);
    }
    public double LocalLuminanceUpper
    {
        get => (SelectedLocal?.Luminance?.Upper ?? 1) * 100;
        set => SetLocalLuminance(value, 1);
    }
    public double LocalLuminanceSoftness
    {
        get => (SelectedLocal?.Luminance?.Softness ?? .1) * 100;
        set => SetLocalLuminance(value, 2);
    }

    [RelayCommand]
    private Task ToggleLocalLuminanceAsync() => ChangeLocalAsync("Luminance Range", () =>
    {
        if (SelectedLocal is { } local)
            local.Luminance = local.Luminance is { } range ? range with { Enabled = !range.Enabled } : new() { Enabled = true };
    });

    private void SetLocalLuminance(double value, int field)
    {
        if (!CanEditLocalLuminance || SelectedLocal is not { Luminance: { } range } local || !double.IsFinite(value)) return;
        var next = field switch
        {
            0 => range with { Lower = Math.Clamp(value / 100, 0, range.Upper) },
            1 => range with { Upper = Math.Clamp(value / 100, range.Lower, 1) },
            _ => range with { Softness = Math.Clamp(value / 100, 0, .5) }
        };
        if (range == next) return;
        local.Luminance = next;
        NotifyLocalsState();
        UpdateCanReset();
        SchedulePreviewUpdate("Luminance Range");
    }

    private void NotifyLocalRangeState()
    {
        foreach (var name in new[] { nameof(LocalRangeEditIdentity), nameof(IsLocalLuminanceEnabled), nameof(CanEditLocalLuminance),
            nameof(LocalLuminanceLower), nameof(LocalLuminanceUpper), nameof(LocalLuminanceSoftness) })
            OnPropertyChanged(name);
        RefreshLocalRangeMask();
    }
}
