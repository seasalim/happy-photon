using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HappyPhoton.Models;
using HappyPhoton.Services;

namespace HappyPhoton.ViewModels;

public partial class MainWindowViewModel
{
    private HistogramData? _rawHistogram;
    private ScopeView _selectedScope;

    public IReadOnlyList<ScopeOption> ScopeOptions { get; } =
    [
        new(ScopeView.Histogram, "HISTOGRAM"),
        new(ScopeView.Waveform, "WAVEFORM"),
        new(
            ScopeView.RawHistogram,
            "RAW HISTOGRAM",
            isEnabled: false,
            hint: "Select a RAW photograph.")
    ];

    public HistogramData? RawHistogram => _rawHistogram;

    public ScopeView SelectedScope
    {
        get => _selectedScope;
        set
        {
            if (!Enum.IsDefined(value))
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }
            if (SetProperty(ref _selectedScope, value))
            {
                NotifyScopeState();
            }
        }
    }

    public ScopeView EffectiveScope =>
        SelectedScope == ScopeView.RawHistogram && !IsRawHistogramAvailable
            ? ScopeView.Histogram
            : SelectedScope;

    [RelayCommand]
    private void SelectScope(ScopeView scope)
    {
        SelectedScope = scope;
        // Clicking the already-active icon still toggles the button's local
        // IsChecked; notify unconditionally so the binding re-asserts it.
        NotifyScopeState();
    }

    public bool IsRawHistogramAvailable => RawHistogram != null;
    public bool IsHistogramScopeActive =>
        EffectiveScope == ScopeView.Histogram;
    public bool IsWaveformScopeActive =>
        EffectiveScope == ScopeView.Waveform;
    public bool IsRawHistogramScopeActive =>
        EffectiveScope == ScopeView.RawHistogram;
    public bool IsHistogramScopeEffective =>
        EffectiveScope != ScopeView.Waveform;
    public bool IsWaveformScopeEffective =>
        EffectiveScope == ScopeView.Waveform;
    public HistogramData? EffectiveHistogram =>
        EffectiveScope == ScopeView.RawHistogram ? RawHistogram : Histogram;
    public WaveformData? EffectiveWaveform => Histogram?.Waveform;
    public string EffectiveScopeTitle =>
        ScopeOptions[(int)EffectiveScope].DisplayName;

    public string RawHistogramHint
    {
        get
        {
            if (!IsDevelopMode && !IsFullScreenMode)
                return "RAW histogram is available in Develop.";
            if (SelectedImage == null) return "Select a RAW photograph.";
            if (SelectedImage.SourceRequiresHydration)
                return "Download the online-only original to inspect sensor data.";
            if (!SelectedImage.IsRaw)
                return "Display-referred source — no sensor data.";
            if (Volatile.Read(ref _activeBaseRefreshRequestId) != 0)
                return "RAW sensor data is unavailable while the replacement base loads.";
            return IsRawHistogramAvailable
                ? "Show the pre-white-balance sensor histogram."
                : "This RAW mosaic layout is unsupported.";
        }
    }

    partial void OnHistogramChanged(HistogramData? value)
    {
        OnPropertyChanged(nameof(EffectiveHistogram));
        OnPropertyChanged(nameof(EffectiveWaveform));
    }

    private void ScheduleHistogramUpdate()
    {
        var selectedImage = SelectedImage;
        if (selectedImage == null)
        {
            SetRawHistogram(null);
            return;
        }
        if (!IsDevelopMode && !IsFullScreenMode &&
            selectedImage.Thumbnail == null)
        {
            _histogramDebounce?.Cancel();
            SetRawHistogram(null);
            Histogram = null;
            return;
        }
        if ((IsDevelopMode || IsFullScreenMode) &&
            selectedImage.SourceRequiresHydration)
        {
            SetRawHistogram(null);
            Histogram = null;
            return;
        }
        if (!IsDevelopMode && !IsFullScreenMode)
            SetRawHistogram(null);
        var debounce = ReplaceDebounce(ref _histogramDebounce);
        var ct = debounce.Token;
        _ = DebouncedAction.RunAsync(
            "histogram update",
            TimeSpan.FromMilliseconds(300),
            ct,
            () => UpdateScheduledHistogramAsync(selectedImage, ct),
            timeProvider: _timeProvider);
    }

    private async Task UpdateScheduledHistogramAsync(
        ImageFile imageFile,
        CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested ||
            !ReferenceEquals(SelectedImage, imageFile))
        {
            return;
        }

        if (IsDevelopMode || IsFullScreenMode)
        {
            if (imageFile.SourceRequiresHydration)
            {
                return;
            }

            SetRawHistogram(ImageService.Previews.TryGetRawHistogram(
                imageFile,
                BaseDecodeSettings.From(imageFile.EditSettings)));

            await UpdatePreviewWithCurrentSliders(
                skipHistogram: false,
                cancellationToken);
            return;
        }

        if (imageFile.Thumbnail != null)
        {
            var generation = imageFile.ThumbnailGeneration;
            // Own the pixels before leaving the UI thread so retirement never waits.
            using var histogramSource = BitmapConversionService.CloneBitmap(
                imageFile.Thumbnail);
            var histogram = await Task.Run(
                () => ImageService.Histograms.CalculateLibraryHistogram(
                    histogramSource),
                cancellationToken);
            if (!cancellationToken.IsCancellationRequested &&
                ReferenceEquals(SelectedImage, imageFile) &&
                !IsDevelopMode && !IsFullScreenMode &&
                imageFile.ThumbnailGeneration == generation)
            {
                Histogram = histogram;
            }
        }
    }

    private void SetRawHistogram(HistogramData? value)
    {
        if (!ReferenceEquals(_rawHistogram, value))
        {
            _rawHistogram = value;
            OnPropertyChanged(nameof(RawHistogram));
        }
        NotifyScopeState();
    }

    private void NotifyRawHistogramState() => NotifyScopeState();

    private void NotifyScopeState()
    {
        var rawOption = ScopeOptions[(int)ScopeView.RawHistogram];
        rawOption.IsEnabled = IsRawHistogramAvailable;
        rawOption.Hint = RawHistogramHint;
        OnPropertyChanged(nameof(IsRawHistogramAvailable));
        OnPropertyChanged(nameof(EffectiveScope));
        OnPropertyChanged(nameof(IsHistogramScopeActive));
        OnPropertyChanged(nameof(IsWaveformScopeActive));
        OnPropertyChanged(nameof(IsRawHistogramScopeActive));
        OnPropertyChanged(nameof(IsHistogramScopeEffective));
        OnPropertyChanged(nameof(IsWaveformScopeEffective));
        OnPropertyChanged(nameof(EffectiveHistogram));
        OnPropertyChanged(nameof(EffectiveWaveform));
        OnPropertyChanged(nameof(EffectiveScopeTitle));
        OnPropertyChanged(nameof(RawHistogramHint));
    }
}

public sealed class ScopeOption : ObservableObject
{
    private bool _isEnabled = true;
    private string? _hint;

    public ScopeOption(
        ScopeView scope,
        string displayName,
        bool isEnabled = true,
        string? hint = null)
    {
        Scope = scope;
        DisplayName = displayName;
        _isEnabled = isEnabled;
        _hint = hint;
    }

    public ScopeView Scope { get; }
    public string DisplayName { get; }

    public bool IsEnabled
    {
        get => _isEnabled;
        internal set => SetProperty(ref _isEnabled, value);
    }

    public string? Hint
    {
        get => _hint;
        internal set => SetProperty(ref _hint, value);
    }
}
