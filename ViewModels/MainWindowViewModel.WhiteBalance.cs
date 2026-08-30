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
        SchedulePreviewUpdate(historyLabel: $"White balance: {value}");
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
    private Task AutoWhiteBalanceAsync()
    {
        var commit = ApplyAutoWhiteBalanceAsync();
        TrackHistoryCommit(commit);
        return commit;
    }

    private async Task ApplyAutoWhiteBalanceAsync()
    {
        if (SelectedImage == null)
        {
            return;
        }

        var image = SelectedImage;
        var generation = Volatile.Read(ref _latestPreviewOutcomeGeneration);
        var settings = CaptureLiveEditState();
        var sample = await ImageService.Previews.GetAutoWhiteBalanceSampleAsync(
            image,
            settings);
        if (sample == null)
        {
            if (IsCurrentWhiteBalanceRequest(image, generation, settings))
            {
                ShowTransientStatus("Auto white balance needs usable mid-tones");
            }
            return;
        }

        if (!await IsCurrentWhiteBalanceSampleAsync(
                image,
                generation,
                settings,
                sample.BaseToken))
        {
            return;
        }
        var surfaceGeneration = RequestEditedRender();
        await ApplyPickedWhiteBalanceAsync(
            sample.Gains, surfaceGeneration, "Auto white balance");
    }

    [RelayCommand(CanExecute = nameof(CanSampleWhiteBalance))]
    private void ToggleWhiteBalancePicker()
    {
        IsWhiteBalancePicking = !IsWhiteBalancePicking;
        ShowTransientStatus(IsWhiteBalancePicking
            ? "Click a neutral area — Esc to cancel"
            : "White balance picker canceled");
    }

    public Task ApplyWhiteBalancePickAsync(
        double normalizedX,
        double normalizedY)
    {
        var commit = ApplyWhiteBalancePickCoreAsync(normalizedX, normalizedY);
        TrackHistoryCommit(commit);
        return commit;
    }

    private async Task ApplyWhiteBalancePickCoreAsync(
        double normalizedX,
        double normalizedY)
    {
        if (!CanSampleWhiteBalance() || SelectedImage == null)
        {
            return;
        }

        var image = SelectedImage;
        var generation = Volatile.Read(ref _latestPreviewOutcomeGeneration);
        var settings = CaptureLiveEditState();
        var sample = await ImageService.Previews.PickWhiteBalanceSampleAsync(
            image,
            settings,
            normalizedX,
            normalizedY);
        if (sample == null)
        {
            if (IsCurrentWhiteBalanceRequest(image, generation, settings))
            {
                ShowTransientStatus("Pick a neutral mid-tone area");
            }
            return;
        }

        if (!await IsCurrentWhiteBalanceSampleAsync(
                image,
                generation,
                settings,
                sample.BaseToken))
        {
            return;
        }
        var surfaceGeneration = RequestEditedRender();
        IsWhiteBalancePicking = false;
        await ApplyPickedWhiteBalanceAsync(
            sample.Gains, surfaceGeneration, "White balance pick");
    }

    private async Task<bool> IsCurrentWhiteBalanceSampleAsync(
        ImageFile image,
        long generation,
        EditSettings settings,
        object baseToken)
    {
        if (!IsCurrentWhiteBalanceRequest(image, generation, settings))
        {
            return false;
        }
        var baseCurrent = await ImageService.Previews.IsWhiteBalanceBaseCurrentAsync(
            image,
            settings,
            baseToken);
        return baseCurrent &&
            IsCurrentWhiteBalanceRequest(image, generation, settings);
    }

    private bool IsCurrentWhiteBalanceRequest(
        ImageFile image,
        long generation,
        EditSettings settings) =>
        ReferenceEquals(SelectedImage, image) &&
        generation == Volatile.Read(ref _latestPreviewOutcomeGeneration) &&
        string.Equals(
            RenderSettingsHash.Compute(CaptureLiveEditState()),
            RenderSettingsHash.Compute(settings),
            StringComparison.Ordinal);

    private async Task ApplyPickedWhiteBalanceAsync(
        double[] gains,
        long generation,
        string historyLabel)
    {
        if (SelectedImage == null)
        {
            return;
        }

        var before = CaptureLiveEditState();
        _liveWhiteBalance = new WhiteBalanceSettings
        {
            Mode = WbMode.Picked,
            Gains = gains.ToArray()
        };
        var display = WhiteBalanceModel.EstimateFromGains(gains);
        SetDisplayedWhiteBalance(display.kelvin, display.tint);
        SetModeLabel("Picked");
        if (await UpdatePreviewWithCurrentSliders(generation: generation))
        {
            await AutoSaveAsync(historyLabel, before);
        }
        UpdateCanReset();
    }

    private bool CanSampleWhiteBalance() =>
        IsColorEditingEnabled && IsWhiteBalanceReady && IsDevelopMode && !IsCropMode &&
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

    partial void OnIsCropModeChanged(bool value)
    {
        if (value) CloseBeforeAfterSplit();
        ToggleBeforeAfterSplitCommand.NotifyCanExecuteChanged();
        NotifyWhiteBalanceCommandState();
        if (value)
        {
            ClearAlignmentGrid();
        }
    }

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

    private void ApplyWhiteBalanceContext(
        double asShotKelvin,
        double asShotTint,
        bool ready)
    {
        _asShotKelvin = asShotKelvin;
        _asShotTint = asShotTint;
        IsWhiteBalanceReady = ready;
        if (_liveWhiteBalance.Mode == WbMode.AsShot)
        {
            SetDisplayedWhiteBalance(_asShotKelvin, _asShotTint);
        }
    }

    private void ResetWhiteBalanceUi()
    {
        _liveWhiteBalance = new WhiteBalanceSettings();
        IsWhiteBalancePicking = false;
        SetDisplayedWhiteBalance(_asShotKelvin, _asShotTint);
        SetModeLabel("As Shot");
    }
}
