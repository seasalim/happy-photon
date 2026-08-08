using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HappyPhoton.Models;
using HappyPhoton.Services;

namespace HappyPhoton.ViewModels;

public partial class MainWindowViewModel
{
    private static readonly IReadOnlyList<string> SelectableWhiteBalanceModes =
    [
        "As Shot",
        "Daylight",
        "Cloudy",
        "Shade",
        "Tungsten",
        "Fluorescent",
        "Flash",
        "Custom",
        "Picked"
    ];

    public IReadOnlyList<string> WhiteBalanceModeOptions =>
        SelectableWhiteBalanceModes;

    private WhiteBalanceSettings _liveWhiteBalance = new();
    private double _asShotKelvin = 6504;
    private double _asShotTint;

    [ObservableProperty]
    private string _selectedWhiteBalanceMode = "As Shot";

    [ObservableProperty]
    private double _whiteBalanceKelvinPosition =
        KelvinToPosition(6504);

    [ObservableProperty]
    private double _whiteBalanceTint;

    [ObservableProperty]
    private bool _isWhiteBalanceReady;

    [ObservableProperty]
    private bool _isWhiteBalancePicking;

    public string WhiteBalanceKelvinText =>
        $"{PositionToKelvin(WhiteBalanceKelvinPosition):0}K";

    public string WhiteBalanceTintText =>
        $"{WhiteBalanceTint:+0;-0;0}";

    public static double PositionToKelvin(double position) =>
        Math.Round(
            2000 * Math.Pow(6, Math.Clamp(position, 0, 1)) / 50) * 50;

    public static double KelvinToPosition(double kelvin) =>
        Math.Clamp(
            Math.Log(Math.Clamp(kelvin, 2000, 12000) / 2000) /
            Math.Log(6),
            0,
            1);

    partial void OnWhiteBalanceKelvinPositionChanged(double value)
    {
        OnPropertyChanged(nameof(WhiteBalanceKelvinText));
        ApplySliderWhiteBalance();
    }

    partial void OnWhiteBalanceTintChanged(double value)
    {
        OnPropertyChanged(nameof(WhiteBalanceTintText));
        ApplySliderWhiteBalance();
    }

    partial void OnSelectedWhiteBalanceModeChanged(string value)
    {
        if (_isLoadingImage || SelectedImage == null)
        {
            return;
        }

        var resolved = ResolveMode(value);
        if (resolved == null)
        {
            RestoreModeLabel();
            return;
        }

        _liveWhiteBalance = resolved;
        SetDisplayedWhiteBalance(
            resolved.Kelvin ?? _asShotKelvin,
            resolved.Tint ?? _asShotTint);
        UpdateCanReset();
        SchedulePreviewUpdate();
    }

    private void ApplySliderWhiteBalance()
    {
        if (_isLoadingImage || SelectedImage == null)
        {
            return;
        }

        _liveWhiteBalance = new WhiteBalanceSettings
        {
            Mode = WbMode.Custom,
            Kelvin = PositionToKelvin(WhiteBalanceKelvinPosition),
            Tint = Math.Round(WhiteBalanceTint)
        };
        SetModeLabel("Custom");
        UpdateCanReset();
        SchedulePreviewUpdate();
    }

    private WhiteBalanceSettings? ResolveMode(string mode) => mode switch
    {
        "As Shot" => new WhiteBalanceSettings(),
        "Daylight" => CreatePreset("Daylight", 5500, 10),
        "Cloudy" => CreatePreset("Cloudy", 6500, 10),
        "Shade" => CreatePreset("Shade", 7500, 10),
        "Tungsten" => CreatePreset("Tungsten", 2850, 0),
        "Fluorescent" => CreatePreset("Fluorescent", 3800, 21),
        "Flash" => CreatePreset("Flash", 5500, 0),
        "Custom" => new WhiteBalanceSettings
        {
            Mode = WbMode.Custom,
            Kelvin = PositionToKelvin(WhiteBalanceKelvinPosition),
            Tint = Math.Round(WhiteBalanceTint)
        },
        _ => null
    };

    private static WhiteBalanceSettings CreatePreset(
        string name,
        double kelvin,
        double tint) =>
        new()
        {
            Mode = WbMode.Preset,
            Preset = name,
            Kelvin = kelvin,
            Tint = tint
        };

    [RelayCommand(CanExecute = nameof(CanSampleWhiteBalance))]
    private async Task AutoWhiteBalanceAsync()
    {
        if (SelectedImage == null)
        {
            return;
        }

        var gains = await ImageService.GetAutoWhiteBalanceAsync(
            SelectedImage,
            CaptureLiveEditState());
        if (gains == null)
        {
            ShowTransientStatus("Auto white balance needs usable mid-tones");
            return;
        }

        await ApplyPickedWhiteBalanceAsync(gains);
    }

    [RelayCommand(CanExecute = nameof(CanSampleWhiteBalance))]
    private void ToggleWhiteBalancePicker()
    {
        IsWhiteBalancePicking = !IsWhiteBalancePicking;
        ShowTransientStatus(IsWhiteBalancePicking
            ? "Click a neutral area — Esc to cancel"
            : "White balance picker canceled");
    }

    public async Task ApplyWhiteBalancePickAsync(
        double normalizedX,
        double normalizedY)
    {
        if (!CanSampleWhiteBalance() || SelectedImage == null)
        {
            return;
        }

        var gains = await ImageService.PickWhiteBalanceAsync(
            SelectedImage,
            CaptureLiveEditState(),
            normalizedX,
            normalizedY);
        if (gains == null)
        {
            ShowTransientStatus("Pick a neutral mid-tone area");
            return;
        }

        IsWhiteBalancePicking = false;
        await ApplyPickedWhiteBalanceAsync(gains);
    }

    private async Task ApplyPickedWhiteBalanceAsync(double[] gains)
    {
        if (SelectedImage == null)
        {
            return;
        }

        PushUndoState();
        _liveWhiteBalance = new WhiteBalanceSettings
        {
            Mode = WbMode.Picked,
            Gains = gains.ToArray()
        };
        var display = WhiteBalanceModel.EstimateFromGains(gains);
        SetDisplayedWhiteBalance(display.kelvin, display.tint);
        SetModeLabel("Picked");
        await UpdatePreviewWithCurrentSliders();
        await AutoSaveAsync();
        UpdateCanReset();
    }

    private bool CanSampleWhiteBalance() =>
        IsWhiteBalanceReady && IsDevelopMode && !IsCropMode &&
        !IsFullScreenMode && CanEditSelectedImage;

    private void NotifyWhiteBalanceCommandState()
    {
        AutoWhiteBalanceCommand.NotifyCanExecuteChanged();
        ToggleWhiteBalancePickerCommand.NotifyCanExecuteChanged();
        if (!CanSampleWhiteBalance())
        {
            IsWhiteBalancePicking = false;
        }
    }

    partial void OnIsWhiteBalanceReadyChanged(bool value) =>
        NotifyWhiteBalanceCommandState();

    partial void OnIsCropModeChanged(bool value) =>
        NotifyWhiteBalanceCommandState();

    private void SaveWhiteBalanceTo(EditSettings target)
    {
        target.Wb = _liveWhiteBalance.Clone();
    }

    private void LoadWhiteBalanceFrom(EditSettings source)
    {
        _liveWhiteBalance = source.Wb?.Clone() ?? new WhiteBalanceSettings();
        var display = ResolveDisplay(_liveWhiteBalance);
        SetDisplayedWhiteBalance(display.kelvin, display.tint);
        RestoreModeLabel();
    }

    private (double kelvin, double tint) ResolveDisplay(
        WhiteBalanceSettings settings) =>
        settings.Mode switch
        {
            WbMode.AsShot => (_asShotKelvin, _asShotTint),
            WbMode.Custom or WbMode.Preset =>
                (settings.Kelvin ?? _asShotKelvin,
                 settings.Tint ?? _asShotTint),
            WbMode.Picked when settings.Gains is { Length: 3 } =>
                WhiteBalanceModel.EstimateFromGains(settings.Gains),
            _ => (_asShotKelvin, _asShotTint)
        };

    private void RestoreModeLabel()
    {
        var label = _liveWhiteBalance.Mode switch
        {
            WbMode.AsShot => "As Shot",
            WbMode.Custom => "Custom",
            WbMode.Preset => _liveWhiteBalance.Preset ?? "Custom",
            WbMode.Picked => "Picked",
            _ => "As Shot"
        };
        SetModeLabel(label);
    }

    private void SetDisplayedWhiteBalance(double kelvin, double tint)
    {
        var wasLoading = _isLoadingImage;
        _isLoadingImage = true;
        WhiteBalanceKelvinPosition = KelvinToPosition(kelvin);
        WhiteBalanceTint = Math.Round(tint);
        _isLoadingImage = wasLoading;
    }

    private void SetModeLabel(string label)
    {
        var wasLoading = _isLoadingImage;
        _isLoadingImage = true;
        SelectedWhiteBalanceMode = label;
        _isLoadingImage = wasLoading;
    }

    private async Task RefreshWhiteBalanceContextAsync(
        ImageFile imageFile,
        CancellationToken cancellationToken)
    {
        var context = await ImageService.GetWhiteBalanceContextAsync(
            imageFile,
            imageFile.EditSettings,
            cancellationToken);
        if (context == null || !ReferenceEquals(SelectedImage, imageFile))
        {
            return;
        }

        _asShotKelvin = context.AsShotKelvin;
        _asShotTint = context.AsShotTint;
        ReconcileHighlightReconstructionCapability(
            imageFile,
            context.IsRawSource);
        IsWhiteBalanceReady = true;
        if (_liveWhiteBalance.Mode == WbMode.AsShot)
        {
            SetDisplayedWhiteBalance(_asShotKelvin, _asShotTint);
        }
    }

    private void ResetWhiteBalanceUi()
    {
        _liveWhiteBalance = new WhiteBalanceSettings();
        _asShotKelvin = 6504;
        _asShotTint = 0;
        IsWhiteBalanceReady = false;
        IsWhiteBalancePicking = false;
        SetDisplayedWhiteBalance(_asShotKelvin, _asShotTint);
        SetModeLabel("As Shot");
    }

    private void PrepareWhiteBalanceUi(ImageFile imageFile)
    {
        _asShotKelvin = imageFile.IsRaw ? 5500 : 6504;
        _asShotTint = 0;
        IsWhiteBalanceReady = false;
        IsWhiteBalancePicking = false;
    }
}
