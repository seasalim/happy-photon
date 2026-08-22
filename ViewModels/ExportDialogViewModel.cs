using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using HappyPhoton.Models;
using HappyPhoton.Services;

namespace HappyPhoton.ViewModels;

public enum ExportSizePreset
{
    HiRes,
    Web,
    Small
}

public enum ExportDialogMode
{
    Standard,
    TourPreview
}

public sealed record ExportFormatOption(string Label, ExportFormat Format);
public sealed record OutputColorSpaceOption(
    string Label,
    OutputColorSpace OutputColorSpace);

public sealed partial class ExportDialogViewModel : ObservableObject, IDisposable
{
    private const string CustomNamingOption = "Custom…";
    private static readonly string[] StandardNamingOptions =
    [
        "{name}",
        "{name}_edited",
        "{name}_{date}"
    ];

    private ExportSizePreset _selectedSize;
    private ExportFormatOption _selectedFormatOption;
    private OutputColorSpaceOption _selectedOutputColorSpaceOption;
    private string _selectedNamingOption;

    [ObservableProperty]
    private bool _isExporting;

    [ObservableProperty]
    private int _progressValue;

    [ObservableProperty]
    private int _progressMaximum = 1;

    [ObservableProperty]
    private string _progressText = string.Empty;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private int _onlineOnlyCount;

    [ObservableProperty]
    private long _onlineOnlyLogicalBytes;

    public ExportDialogViewModel(
        ExportSettings settings,
        int imageCount,
        ExportDialogMode mode = ExportDialogMode.Standard)
    {
        Settings = settings;
        ImageCount = imageCount;
        Mode = mode;
        FormatOptions =
        [
            new("JPEG", ExportFormat.Jpeg),
            new("PNG", ExportFormat.Png),
            new("WebP", ExportFormat.Webp),
            new("TIFF (16-bit)", ExportFormat.Tiff)
        ];
        OutputColorSpaceOptions =
        [
            new("sRGB", OutputColorSpace.Srgb),
            new("Display P3", OutputColorSpace.DisplayP3)
        ];
        NamingOptions = [.. StandardNamingOptions, CustomNamingOption];
        _selectedFormatOption = FormatOptions.First(option => option.Format == settings.Format);
        _selectedOutputColorSpaceOption = OutputColorSpaceOptions.First(
            option => option.OutputColorSpace == settings.OutputColorSpace);
        _selectedNamingOption = StandardNamingOptions.Contains(settings.NamingPattern)
            ? settings.NamingPattern
            : CustomNamingOption;
        _selectedSize = GetInitialSize(settings);
        ApplySelectedSize();
        Settings.PropertyChanged += OnSettingsPropertyChanged;
    }

    public ExportSettings Settings { get; }
    public int ImageCount { get; }
    public ExportDialogMode Mode { get; }
    public IReadOnlyList<ExportFormatOption> FormatOptions { get; }
    public IReadOnlyList<OutputColorSpaceOption> OutputColorSpaceOptions { get; }
    public IReadOnlyList<string> NamingOptions { get; }
    public bool HasImages => ImageCount > 0;
    public bool HasNoImages => !HasImages;
    public bool IsTourPreview => Mode == ExportDialogMode.TourPreview;
    public bool ShowConfiguration => HasImages || IsTourPreview;
    public bool ShowEmptyState => HasNoImages && !IsTourPreview;
    public bool ShowFooterOptions => ShowConfiguration;
    public bool IsIdle => !IsExporting;
    public bool ShowIdleImageActions => HasImages && IsIdle;
    public bool ShowPrimaryAction => ShowIdleImageActions || (IsTourPreview && IsIdle);
    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);
    public bool HasOnlineOnlyImages => OnlineOnlyCount > 0;
    public string OnlineOnlyMessage =>
        $"Exporting will download {OnlineOnlyCount} online-only " +
        $"original{(OnlineOnlyCount == 1 ? string.Empty : "s")} " +
        $"(approximately {FormatLogicalSize(OnlineOnlyLogicalBytes)}).";
    public bool IsQualityAvailable =>
        Settings.Format is not ExportFormat.Png and not ExportFormat.Tiff;
    public bool IsLosslessFormat => !IsQualityAvailable;
    public bool IsOutputSharpeningOff
    {
        get => Settings.OutputSharpening == OutputSharpeningMode.Off;
        set
        {
            if (value) Settings.OutputSharpening = OutputSharpeningMode.Off;
        }
    }
    public bool IsOutputSharpeningScreen
    {
        get => Settings.OutputSharpening == OutputSharpeningMode.Screen;
        set
        {
            if (value) Settings.OutputSharpening = OutputSharpeningMode.Screen;
        }
    }
    public bool IsOutputSharpeningPrint
    {
        get => Settings.OutputSharpening == OutputSharpeningMode.Print;
        set
        {
            if (value) Settings.OutputSharpening = OutputSharpeningMode.Print;
        }
    }
    public bool IsCustomNaming => SelectedNamingOption == CustomNamingOption;
    public bool CanExport => HasImages && IsIdle &&
        !string.IsNullOrWhiteSpace(Settings.OutputFolder);
    public bool CanPrimaryAction => IsTourPreview ? IsIdle : CanExport;
    public string PrimaryActionText => IsTourPreview ? "Return to Library" : "Export";
    public string HeaderText =>
        $"Export {ImageCount} Image{(ImageCount == 1 ? string.Empty : "s")}";
    public string PreviewFileName => Settings.GetOutputFileName("example_photo.jpg");

    public void UpdateHydrationScope(ExportHydrationScope scope)
    {
        OnlineOnlyCount = scope.FileCount;
        OnlineOnlyLogicalBytes = scope.LogicalBytes;
    }

    public ExportFormatOption SelectedFormatOption
    {
        get => _selectedFormatOption;
        set
        {
            if (SetProperty(ref _selectedFormatOption, value))
            {
                Settings.Format = value.Format;
            }
        }
    }

    public OutputColorSpaceOption SelectedOutputColorSpaceOption
    {
        get => _selectedOutputColorSpaceOption;
        set
        {
            if (SetProperty(ref _selectedOutputColorSpaceOption, value))
            {
                Settings.OutputColorSpace = value.OutputColorSpace;
            }
        }
    }

    public string SelectedNamingOption
    {
        get => _selectedNamingOption;
        set
        {
            if (!SetProperty(ref _selectedNamingOption, value)) return;

            if (value != CustomNamingOption)
            {
                Settings.NamingPattern = value;
            }

            OnPropertyChanged(nameof(IsCustomNaming));
        }
    }

    public ExportSizePreset SelectedSize
    {
        get => _selectedSize;
        set
        {
            if (!SetProperty(ref _selectedSize, value)) return;
            ApplySelectedSize();
            NotifySizePropertiesChanged();
        }
    }

    public bool IsHiResSelected
    {
        get => SelectedSize == ExportSizePreset.HiRes;
        set { if (value) SelectedSize = ExportSizePreset.HiRes; }
    }

    public bool IsWebSelected
    {
        get => SelectedSize == ExportSizePreset.Web;
        set { if (value) SelectedSize = ExportSizePreset.Web; }
    }

    public bool IsSmallSelected
    {
        get => SelectedSize == ExportSizePreset.Small;
        set { if (value) SelectedSize = ExportSizePreset.Small; }
    }

    public ExportVariant SelectedVariant => SelectedSize switch
    {
        ExportSizePreset.Web => new("web", Math.Clamp(Settings.WebMaxSize, 16, 65536)),
        ExportSizePreset.Small => new("small", Math.Clamp(Settings.SmallMaxSize, 16, 65536)),
        _ => new("hi-res", null)
    };

    public void BeginExport()
    {
        ErrorMessage = string.Empty;
        ProgressValue = 0;
        ProgressMaximum = Math.Max(1, ImageCount);
        ProgressText = "Preparing export…";
        IsExporting = true;
    }

    public void UpdateProgress(int current, int total, string currentFile)
    {
        ProgressMaximum = Math.Max(1, total);
        ProgressValue = current;
        var position = Math.Min(current + 1, total);
        ProgressText = currentFile == "Complete"
            ? $"{total}/{total} — Complete"
            : $"Exporting {position}/{total} — {currentFile}";
    }

    public void EndExport()
    {
        IsExporting = false;
        ProgressValue = 0;
        ProgressText = string.Empty;
    }

    public void ShowError(string message)
    {
        EndExport();
        ErrorMessage = message;
    }

    public void ShowPartialExport(ExportBatchResult result)
    {
        EndExport();
        var failedCount = result.FailedImages.Count;
        var total = result.ExportedCount + failedCount;
        var failedLabel = failedCount == 1
            ? "1 image was"
            : $"{failedCount} images were";
        var failedPaths = string.Join(
            Environment.NewLine,
            result.FailedImages.Select(image => $"• {image.FilePath}"));
        ErrorMessage =
            $"Exported {result.ExportedCount} of {total} images. " +
            $"{failedLabel} not exported:{Environment.NewLine}{failedPaths}";
    }

    public void ShowExportWarnings(ExportBatchResult result)
    {
        EndExport();
        var details = string.Join(
            Environment.NewLine,
            result.Warnings.Select(warning =>
                $"• {warning.Image.FileName}: {warning.Message}"));
        ErrorMessage =
            $"Exported {result.ExportedCount} images using built-in camera " +
            $"characterization where selected profiles were unavailable:" +
            $"{Environment.NewLine}{details}";
    }

    public void Dispose() => Settings.PropertyChanged -= OnSettingsPropertyChanged;

    partial void OnIsExportingChanged(bool value)
    {
        OnPropertyChanged(nameof(IsIdle));
        OnPropertyChanged(nameof(ShowIdleImageActions));
        OnPropertyChanged(nameof(ShowPrimaryAction));
        OnPropertyChanged(nameof(CanExport));
        OnPropertyChanged(nameof(CanPrimaryAction));
    }

    partial void OnErrorMessageChanged(string value) =>
        OnPropertyChanged(nameof(HasError));

    partial void OnOnlineOnlyCountChanged(int value)
    {
        OnPropertyChanged(nameof(HasOnlineOnlyImages));
        OnPropertyChanged(nameof(OnlineOnlyMessage));
    }

    partial void OnOnlineOnlyLogicalBytesChanged(long value) =>
        OnPropertyChanged(nameof(OnlineOnlyMessage));

    internal static string FormatLogicalSize(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var value = Math.Max(0, bytes);
        var unit = 0;
        var display = (double)value;
        while (display >= 1024 && unit < units.Length - 1)
        {
            display /= 1024;
            unit++;
        }

        var format = unit == 0 || display >= 10 ? "0" : "0.0";
        return $"{display.ToString(format, CultureInfo.InvariantCulture)} {units[unit]}";
    }

    private static ExportSizePreset GetInitialSize(ExportSettings settings)
    {
        if (settings.ExportWeb && !settings.ExportHiRes) return ExportSizePreset.Web;
        if (settings.ExportSmall && !settings.ExportHiRes) return ExportSizePreset.Small;
        return ExportSizePreset.HiRes;
    }

    private void ApplySelectedSize()
    {
        Settings.ExportHiRes = SelectedSize == ExportSizePreset.HiRes;
        Settings.ExportWeb = SelectedSize == ExportSizePreset.Web;
        Settings.ExportSmall = SelectedSize == ExportSizePreset.Small;
    }

    private void NotifySizePropertiesChanged()
    {
        OnPropertyChanged(nameof(IsHiResSelected));
        OnPropertyChanged(nameof(IsWebSelected));
        OnPropertyChanged(nameof(IsSmallSelected));
        OnPropertyChanged(nameof(SelectedVariant));
        OnPropertyChanged(nameof(PreviewFileName));
    }

    private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(ExportSettings.Format))
        {
            OnPropertyChanged(nameof(IsQualityAvailable));
            OnPropertyChanged(nameof(IsLosslessFormat));
        }

        if (args.PropertyName == nameof(ExportSettings.OutputSharpening))
        {
            OnPropertyChanged(nameof(IsOutputSharpeningOff));
            OnPropertyChanged(nameof(IsOutputSharpeningScreen));
            OnPropertyChanged(nameof(IsOutputSharpeningPrint));
        }

        if (args.PropertyName is nameof(ExportSettings.Format) or
            nameof(ExportSettings.NamingPattern) or
            nameof(ExportSettings.WebMaxSize) or
            nameof(ExportSettings.SmallMaxSize))
        {
            OnPropertyChanged(nameof(PreviewFileName));
            OnPropertyChanged(nameof(SelectedVariant));
        }

        if (args.PropertyName == nameof(ExportSettings.OutputFolder))
        {
            OnPropertyChanged(nameof(CanExport));
            OnPropertyChanged(nameof(CanPrimaryAction));
        }
    }
}
