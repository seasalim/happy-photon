using HappyPhoton.Models;
using HappyPhoton.Services;
using CommunityToolkit.Mvvm.Input;

namespace HappyPhoton.ViewModels;

public partial class MainWindowViewModel
{
    private HistogramData? _rawHistogram;
    private bool _isRawHistogramPreferred;

    public HistogramData? RawHistogram => _rawHistogram;

    public bool IsRawHistogramPreferred
    {
        get => _isRawHistogramPreferred;
        private set
        {
            if (SetProperty(ref _isRawHistogramPreferred, value))
            {
                NotifyRawHistogramState();
            }
        }
    }

    public bool IsRawHistogramAvailable => RawHistogram != null;
    public bool IsRawHistogramEffective =>
        IsRawHistogramPreferred && IsRawHistogramAvailable;
    public HistogramData? EffectiveHistogram =>
        IsRawHistogramEffective ? RawHistogram : Histogram;
    public string HistogramTitle =>
        IsRawHistogramEffective ? "RAW HISTOGRAM" : "HISTOGRAM";
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

    [RelayCommand]
    private void ToggleRawHistogram()
    {
        if (IsRawHistogramAvailable)
            IsRawHistogramPreferred = !IsRawHistogramPreferred;
    }

    partial void OnHistogramChanged(HistogramData? value) =>
        OnPropertyChanged(nameof(EffectiveHistogram));

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
            () => UpdateScheduledHistogramAsync(selectedImage, ct));
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

            SetRawHistogram(ImageService.TryGetRawHistogram(
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
                () => ImageService.CalculateLibraryHistogram(histogramSource),
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
        NotifyRawHistogramState();
    }

    private void NotifyRawHistogramState()
    {
        OnPropertyChanged(nameof(IsRawHistogramAvailable));
        OnPropertyChanged(nameof(IsRawHistogramEffective));
        OnPropertyChanged(nameof(EffectiveHistogram));
        OnPropertyChanged(nameof(HistogramTitle));
        OnPropertyChanged(nameof(RawHistogramHint));
    }
}
