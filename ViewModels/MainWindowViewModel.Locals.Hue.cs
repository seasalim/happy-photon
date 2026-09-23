using Avalonia;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HappyPhoton.Models;
using HappyPhoton.Services;

namespace HappyPhoton.ViewModels;

public partial class MainWindowViewModel
{
    [ObservableProperty] private bool _isLocalHueExpanded;
    [ObservableProperty] private bool _isLocalHuePicking;
    private bool _huePickPreviousMask;
    private long _huePickRequest;
    private string? _huePickSettings;
    private BaseImage? _huePickBase;
    private PixelSize _huePickSize;
    internal Func<Task>? LocalHuePickGateAsync { get; set; }
    public bool IsLocalHueEnabled => SelectedLocal?.Hue?.Enabled == true;
    public bool CanEditLocalHue => CanEditLocalColor && IsLocalHueEnabled;
    public bool IsSelectedLocalRangeRestricted => SelectedLocal?.Luminance?.IsEffective == true ||
        !IsMonochromeSource && IsLocalHueEnabled;
    public double LocalHueCenter { get => SelectedLocal?.Hue?.Center ?? 240; set => SetLocalHue(value, 0); }
    public double LocalHueWidth { get => SelectedLocal?.Hue?.Width ?? 60; set => SetLocalHue(value, 1); }
    public double LocalHueSoftness { get => SelectedLocal?.Hue?.Softness ?? 30; set => SetLocalHue(value, 2); }
    private string _localHuePickUnavailableReason = "Pick Hue needs a matching loaded base image";
    public bool CanPickLocalHue { get; private set; }
    public string LocalHuePickAvailability => IsLocalHuePicking && CanPickLocalHue
        ? "Click a color in the image · Escape cancels" : _localHuePickUnavailableReason;
    partial void OnIsLocalHuePickingChanged(bool value) => OnPropertyChanged(nameof(LocalHuePickAvailability));
    private PreviewBaseSnapshot? AcquireHueBase() => SelectedImage is { } image && PreviewImage is { } preview
        ? ImageService.Previews.AcquireLocalRangeBase(image, CaptureLiveEditState(),
            Math.Max(preview.PixelSize.Width, preview.PixelSize.Height), preview) : null;

    [RelayCommand]
    private Task ToggleLocalHueAsync() => !CanEditLocalColor ? Task.CompletedTask : ChangeLocalAsync("Hue Range", () =>
    {
        if (SelectedLocal is { } local)
            local.Hue = local.Hue is { } hue ? hue with { Enabled = !hue.Enabled } : new() { Enabled = true };
    });

    private void SetLocalHue(double value, int field)
    {
        if (!CanEditLocalHue || SelectedLocal is not { Hue: { } hue } local || !double.IsFinite(value)) return;
        var next = field switch
        {
            0 => hue with { Center = (value % 360 + 360) % 360 },
            1 => hue with { Width = Math.Clamp(value, 0, 360) },
            _ => hue with { Softness = Math.Clamp(value, 0, 90) }
        };
        if (hue == next) return;
        local.Hue = next;
        NotifyLocalsState(); UpdateCanReset(); SchedulePreviewUpdate("Hue Range");
    }

    [RelayCommand]
    private void ToggleLocalHuePick()
    {
        if (IsLocalHuePicking) { CancelLocalHuePick(); return; }
        DiscardLocalsGesture();
        IsWhiteBalancePicking = false;
        if (!CanPickLocalHue) return;
        _huePickPreviousMask = ShowLocalMask;
        IsLocalHuePicking = true;
        ShowLocalMask = true;
    }

    private void CancelLocalHuePick()
    {
        if (!IsLocalHuePicking) return;
        ++_huePickRequest;
        _huePickSettings = null; _huePickBase = null;
        IsLocalHuePicking = false;
        ShowLocalMask = _huePickPreviousMask;
    }

    public async Task PickLocalHueAsync(Point correctedPoint)
    {
        if (!IsLocalHuePicking || !CanEditLocalColor || SelectedLocal is not { } local || PreviewImage is not { } preview) return;
        var request = ++_huePickRequest;
        var image = SelectedImage;
        var size = preview.PixelSize;
        var settings = CaptureLiveEditState();
        var key = LocalMaskSettingsKey(settings, local);
        using var lease = AcquireHueBase();
        if (lease == null) { ShowTransientStatus("Pick Hue: no matching base"); return; }
        _huePickSettings = key; _huePickBase = lease.Base; _huePickSize = size;
        (double? Hue, string? Rejection, int Count) result;
        try
        {
            if (LocalHuePickGateAsync is { } gate) await gate();
            result = await Task.Run(() => LocalRangeSampling.Pick(lease.Base, settings, correctedPoint, size));
        }
        catch (Exception ex)
        {
            ImageServiceHelpers.LogError($"Hue sample failed: {ex.Message}");
            if (IsLocalHuePicking && request == _huePickRequest) ShowTransientStatus("Unable to sample hue");
            return;
        }
        using var current = AcquireHueBase();
        if (!IsLocalHuePicking || request != _huePickRequest || !ReferenceEquals(image, SelectedImage) ||
            SelectedLocal?.Id != local.Id || PreviewImage?.PixelSize != size || !ReferenceEquals(current?.Base, lease.Base) ||
            LocalMaskSettingsKey(CaptureLiveEditState(), SelectedLocal) != key) return;
        if (result.Hue is not { } hue) { ShowTransientStatus($"Pick Hue: {result.Rejection}"); return; }
        await ChangeLocalAsync("Pick Hue", () => local.Hue = (local.Hue ?? new()) with { Center = hue, Enabled = true });
    }

    private void NotifyLocalHueState()
    {
        if (IsLocalHuePicking && !CanEditLocalColor) CancelLocalHuePick();
        using var current = CanEditLocalColor ? AcquireHueBase() : null;
        CanPickLocalHue = current != null;
        _localHuePickUnavailableReason = IsMonochromeSource ? "Monochrome RAW — color controls unavailable"
            : CanPickLocalHue ? "" : "Pick Hue needs a matching loaded base image";
        if (IsLocalHuePicking && _huePickSettings is { } key && SelectedLocal is { } local)
        {
            if (PreviewImage?.PixelSize != _huePickSize || !ReferenceEquals(current?.Base, _huePickBase) ||
                key != LocalMaskSettingsKey(CaptureLiveEditState(), local))
            { ++_huePickRequest; _huePickSettings = null; _huePickBase = null; }
        }
        foreach (var name in new[] { nameof(IsLocalHueEnabled), nameof(CanEditLocalHue), nameof(CanPickLocalHue),
            nameof(LocalHuePickAvailability), nameof(LocalHueCenter), nameof(LocalHueWidth), nameof(LocalHueSoftness),
            nameof(IsSelectedLocalRangeRestricted) }) OnPropertyChanged(name);
    }
}
