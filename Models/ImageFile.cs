using System.Collections.Frozen;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using HappyPhoton.Services;

namespace HappyPhoton.Models;

/// <summary>
/// Represents an image file with its metadata, thumbnail, and edit state.
/// </summary>
public partial class ImageFile : ObservableObject
{
    internal static readonly FrozenSet<string> RawExtensions =
        new[]
        {
            ".cr2", ".cr3", ".nef", ".nrw", ".arw",
            ".dng", ".raf", ".orf", ".rw2", ".pef"
        }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    internal static readonly FrozenSet<string> SupportedExtensions =
        RawExtensions.Concat(new[]
        {
            ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tif", ".tiff",
            ".webp", ".heic", ".heif"
        }).ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    public string FilePath { get; }
    public string FileName { get; }
    public string Extension { get; }
    internal SourceAvailability SourceAvailabilityHint { get; }

    [ObservableProperty]
    private Bitmap? _thumbnail;

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private bool _isActive;

    [ObservableProperty]
    private bool _hasEdits;

    [ObservableProperty]
    private ImageFlag _flag;

    [ObservableProperty]
    private int _rating;   // 0 = unrated, 1-5 stars

    [ObservableProperty]
    private ColorLabel _colorLabel;

    [ObservableProperty]
    private int _burstGroupOrdinal;   // 0 = none; 1-based

    [ObservableProperty]
    private int _burstIndex;          // 1-based within group

    [ObservableProperty]
    private int _burstSize;           // 0 = not in a burst

    [ObservableProperty]
    private bool _isBurstHighlighted;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _thumbnailDeferredForHydration;

    [ObservableProperty]
    private bool _thumbnailLoadFailed;

    [ObservableProperty]
    private bool _rawDecodeFailed;

    [ObservableProperty]
    private bool _sourceRequiresHydration;

    public bool ShowCloudPlaceholder =>
        SourceRequiresHydration && Thumbnail == null;

    public EditSettings EditSettings { get; set; } = new();

    /// <summary>
    /// The catalog database ID for this image. Set after loading from catalog.
    /// </summary>
    public long CatalogId { get; set; }
    public long AssessmentRevision { get; set; }
    public DateTime? AssessedUtc { get; set; }
    public AssessmentAxes PendingAssessmentAxes { get; set; }

    public bool HasVisibleLoadFailure =>
        ThumbnailLoadFailed || RawDecodeFailed;

    public string LoadFailureText => RawDecodeFailed
        ? "This RAW file could not be decoded. It may use an unsupported encoding such as Nikon HE."
        : "The thumbnail could not be loaded.";

    public int ThumbnailPixelWidth { get; private set; }
    public int ThumbnailPixelHeight { get; private set; }
    public long ThumbnailGeneration { get; private set; }
    public int ThumbnailUpgradeDeferredDimension { get; set; }
    public int ThumbnailUpgradeFailedDimension { get; set; }

    public long ThumbnailBytes =>
        (long)ThumbnailPixelWidth * ThumbnailPixelHeight * 4;

    public bool ThumbnailSatisfies(ThumbnailSizeRequest request) =>
        Thumbnail != null &&
        Math.Max(ThumbnailPixelWidth, ThumbnailPixelHeight) >=
        request.MinimumDimension;

    public bool IsRaw { get; }

    public bool IsPicked => Flag == ImageFlag.Picked;

    public bool IsRejected => Flag == ImageFlag.Rejected;

    public bool IsUnflagged => Flag == ImageFlag.Unflagged;

    public bool HasRating => Rating > 0;

    public bool HasColorLabel => ColorLabel != ColorLabel.None;

    public string RatingStars => new string('★', Math.Clamp(Rating, 0, 5));

    public bool HasBurstGroup => BurstSize > 0;

    public string BurstChipText => $"{BurstIndex}/{BurstSize}";

    public int BurstColorIndex => BurstGroupOrdinal <= 0 ? 0 : (BurstGroupOrdinal - 1) % 6;

    public ImageFile(string filePath)
        : this(filePath, SourceAvailability.Unknown)
    {
    }

    internal ImageFile(
        string filePath,
        SourceAvailability sourceAvailabilityHint)
    {
        FilePath = filePath;
        FileName = Path.GetFileName(filePath);
        Extension = Path.GetExtension(filePath);
        IsRaw = RawExtensions.Contains(Extension);
        SourceAvailabilityHint = sourceAvailabilityHint;
        SourceRequiresHydration =
            sourceAvailabilityHint == SourceAvailability.RequiresHydration;
    }

    internal Bitmap? SwapThumbnail(Bitmap? thumbnail)
    {
        if (ReferenceEquals(Thumbnail, thumbnail)) return null;
        var previous = Thumbnail;
        Thumbnail = thumbnail;
        return previous;
    }

    // Metadata properties
    [ObservableProperty] private long _fileSize;
    [ObservableProperty] private int _pixelWidth;
    [ObservableProperty] private int _pixelHeight;
    [ObservableProperty] private DateTime? _dateTaken;
    [ObservableProperty] private DateTime? _fileModifiedDate;
    [ObservableProperty] private string? _cameraMake;
    [ObservableProperty] private string? _cameraModel;
    [ObservableProperty] private double? _fNumber;
    [ObservableProperty] private string? _exposureTime;
    [ObservableProperty] private int? _iso;
    [ObservableProperty] private double? _focalLength;
    [ObservableProperty] private double? _focalLengthIn35mmFilm;
    [ObservableProperty] private double? _exposureBias;
    [ObservableProperty] private string? _lensModel;
    [ObservableProperty] private double? _gpsLatitude;
    [ObservableProperty] private double? _gpsLongitude;
    [ObservableProperty] private double? _gpsAltitude;
    [ObservableProperty] private bool _metadataLoaded;

    public void ApplyMetadata(ImageMetadata metadata)
    {
        FileSize = metadata.FileSize;
        PixelWidth = metadata.PixelWidth;
        PixelHeight = metadata.PixelHeight;
        DateTaken = metadata.DateTaken;
        FileModifiedDate = metadata.FileModifiedDate;
        CameraMake = metadata.CameraMake;
        CameraModel = metadata.CameraModel;
        FNumber = metadata.FNumber;
        ExposureTime = metadata.ExposureTime;
        Iso = metadata.Iso;
        FocalLength = metadata.FocalLength;
        FocalLengthIn35mmFilm = metadata.FocalLengthIn35mmFilm;
        ExposureBias = metadata.ExposureBias;
        LensModel = metadata.LensModel;
        GpsLatitude = metadata.GpsLatitude;
        GpsLongitude = metadata.GpsLongitude;
        GpsAltitude = metadata.GpsAltitude;
        MetadataLoaded = true;
    }

    // Computed metadata display properties
    public string FileSizeDisplay => FormatFileSize(FileSize);

    public string? FileDetailsDisplay
    {
        get
        {
            var parts = new List<string>();
            if (PixelWidth > 0 && PixelHeight > 0)
            {
                var megapixels = PixelWidth * (double)PixelHeight / 1_000_000;
                parts.Add($"{PixelWidth}×{PixelHeight}");
                parts.Add($"{megapixels:F1} MP");
            }
            if (FileSize > 0) parts.Add(FileSizeDisplay);
            return parts.Count > 0 ? string.Join(" · ", parts) : null;
        }
    }

    public DateTime? DisplayDate => DateTaken ?? FileModifiedDate;
    public bool HasCaptureDate => DateTaken.HasValue;
    public bool IsFileModifiedDateFallback =>
        !DateTaken.HasValue && FileModifiedDate.HasValue;

    internal static string FormatFileSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
        < 1024L * 1024 * 1024 => $"{bytes / (1024.0 * 1024):F1} MB",
        _ => $"{bytes / (1024.0 * 1024 * 1024):F2} GB"
    };

    // EXIF Make values are verbatim vendor strings; LibRaw normalizes them.
    // Map the common all-caps/suffixed forms so RAW and JPEG display match.
    private static readonly FrozenDictionary<string, string> CameraMakeAliases =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["FUJIFILM"] = "Fujifilm",
            ["NIKON"] = "Nikon",
            ["CANON"] = "Canon",
            ["SONY"] = "Sony",
            ["OLYMPUS"] = "Olympus",
            ["PANASONIC"] = "Panasonic",
            ["PENTAX"] = "Pentax",
            ["RICOH"] = "Ricoh",
            ["SAMSUNG"] = "Samsung",
            ["SIGMA"] = "Sigma",
            ["LEICA"] = "Leica",
            ["KODAK"] = "Kodak",
            ["MINOLTA"] = "Minolta",
            ["KONICA MINOLTA"] = "Konica Minolta"
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    private static readonly string[] CameraMakeSuffixes =
        ["CORPORATION", "COMPANY", "CO., LTD.", "CO.,LTD.", "LTD."];

    internal static string? NormalizeCameraMake(string? make)
    {
        if (string.IsNullOrWhiteSpace(make)) return null;
        var trimmed = make.Trim();
        foreach (var suffix in CameraMakeSuffixes)
        {
            if (trimmed.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                trimmed = trimmed[..^suffix.Length].TrimEnd();
            }
        }

        return CameraMakeAliases.TryGetValue(trimmed, out var alias)
            ? alias
            : trimmed;
    }

    public string? CameraDisplay
    {
        get
        {
            var make = NormalizeCameraMake(CameraMake);
            var model = CameraModel?.Trim();
            if (!string.IsNullOrEmpty(make) &&
                !string.IsNullOrEmpty(model) &&
                model.StartsWith(make, StringComparison.OrdinalIgnoreCase))
            {
                model = model[make.Length..].TrimStart();
            }

            var parts = new[] { make, model }
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .ToArray();
            return parts.Length > 0 ? string.Join(" ", parts) : null;
        }
    }

    public bool HasCameraMetadata =>
        !string.IsNullOrEmpty(CameraDisplay) ||
        !string.IsNullOrEmpty(LensModel) ||
        ExposureDisplay != null;

    public string? ExposureDisplay
    {
        get
        {
            var parts = new List<string>();
            if (FocalLength.HasValue) parts.Add($"{FocalLength.Value:F0}mm");
            if (FNumber.HasValue) parts.Add($"f/{FNumber:F1}");
            if (!string.IsNullOrEmpty(ExposureTime)) parts.Add($"{ExposureTime}s");
            if (Iso.HasValue) parts.Add($"ISO {Iso}");
            if (ExposureBias is { } bias && double.IsFinite(bias) && Math.Abs(bias) >= 0.05)
            {
                parts.Add($"{bias:+0.0;-0.0} EV");
            }
            return parts.Count > 0 ? string.Join("  ", parts) : null;
        }
    }

    public string? ExposureTooltip =>
        FocalLength is { } focalLength &&
        FocalLengthIn35mmFilm is { } equivalent
            ? $"{focalLength:F0}mm ({equivalent:F0}mm equiv)"
            : null;

    public bool HasGpsCoordinates =>
        GpsLatitude is >= -90 and <= 90 &&
        GpsLongitude is >= -180 and <= 180;

    public bool HasLocationMetadata =>
        HasGpsCoordinates || GpsAltitude is { } altitude && double.IsFinite(altitude);

    public string? GpsDisplay
    {
        get
        {
            if (!HasGpsCoordinates) return null;
            var latitude = GpsLatitude.GetValueOrDefault();
            var longitude = GpsLongitude.GetValueOrDefault();
            var latDir = latitude >= 0 ? "N" : "S";
            var lonDir = longitude >= 0 ? "E" : "W";
            return $"{Math.Abs(latitude):F4}° {latDir}, {Math.Abs(longitude):F4}° {lonDir}";
        }
    }

    public string? GpsAltitudeDisplay =>
        GpsAltitude is { } altitude && double.IsFinite(altitude)
        ? $"{altitude:F0} m altitude"
        : null;

    partial void OnFileSizeChanged(long value)
    {
        OnPropertyChanged(nameof(FileSizeDisplay));
        OnPropertyChanged(nameof(FileDetailsDisplay));
    }
    partial void OnPixelWidthChanged(int value) =>
        OnPropertyChanged(nameof(FileDetailsDisplay));
    partial void OnPixelHeightChanged(int value) =>
        OnPropertyChanged(nameof(FileDetailsDisplay));
    partial void OnDateTakenChanged(DateTime? value)
    {
        OnPropertyChanged(nameof(DisplayDate));
        OnPropertyChanged(nameof(HasCaptureDate));
        OnPropertyChanged(nameof(IsFileModifiedDateFallback));
    }
    partial void OnFileModifiedDateChanged(DateTime? value)
    {
        OnPropertyChanged(nameof(DisplayDate));
        OnPropertyChanged(nameof(IsFileModifiedDateFallback));
    }
    partial void OnThumbnailChanged(Bitmap? value)
    {
        ThumbnailPixelWidth = value?.PixelSize.Width ?? 0;
        ThumbnailPixelHeight = value?.PixelSize.Height ?? 0;
        ThumbnailGeneration++;
        OnPropertyChanged(nameof(ShowCloudPlaceholder));
    }
    partial void OnSourceRequiresHydrationChanged(bool value) =>
        OnPropertyChanged(nameof(ShowCloudPlaceholder));
    partial void OnThumbnailLoadFailedChanged(bool value) =>
        NotifyLoadFailureChanged();
    partial void OnRawDecodeFailedChanged(bool value) =>
        NotifyLoadFailureChanged();
    partial void OnCameraMakeChanged(string? value) => NotifyCameraDisplayChanged();
    partial void OnCameraModelChanged(string? value) => NotifyCameraDisplayChanged();
    partial void OnLensModelChanged(string? value) =>
        OnPropertyChanged(nameof(HasCameraMetadata));
    partial void OnFNumberChanged(double? value) => NotifyExposureDisplayChanged();
    partial void OnExposureTimeChanged(string? value) => NotifyExposureDisplayChanged();
    partial void OnIsoChanged(int? value) => NotifyExposureDisplayChanged();
    partial void OnFocalLengthChanged(double? value)
    {
        NotifyExposureDisplayChanged();
        OnPropertyChanged(nameof(ExposureTooltip));
    }
    partial void OnFocalLengthIn35mmFilmChanged(double? value) =>
        OnPropertyChanged(nameof(ExposureTooltip));
    partial void OnExposureBiasChanged(double? value) => NotifyExposureDisplayChanged();
    partial void OnGpsLatitudeChanged(double? value) => NotifyLocationDisplayChanged();
    partial void OnGpsLongitudeChanged(double? value) => NotifyLocationDisplayChanged();
    partial void OnGpsAltitudeChanged(double? value)
    {
        OnPropertyChanged(nameof(GpsAltitudeDisplay));
        OnPropertyChanged(nameof(HasLocationMetadata));
    }

    private void NotifyCameraDisplayChanged()
    {
        OnPropertyChanged(nameof(CameraDisplay));
        OnPropertyChanged(nameof(HasCameraMetadata));
    }

    private void NotifyExposureDisplayChanged()
    {
        OnPropertyChanged(nameof(ExposureDisplay));
        OnPropertyChanged(nameof(HasCameraMetadata));
    }

    private void NotifyLocationDisplayChanged()
    {
        OnPropertyChanged(nameof(HasGpsCoordinates));
        OnPropertyChanged(nameof(GpsDisplay));
        OnPropertyChanged(nameof(HasLocationMetadata));
    }

    private void NotifyLoadFailureChanged()
    {
        OnPropertyChanged(nameof(HasVisibleLoadFailure));
        OnPropertyChanged(nameof(LoadFailureText));
    }
    partial void OnFlagChanged(ImageFlag value)
    {
        OnPropertyChanged(nameof(IsPicked));
        OnPropertyChanged(nameof(IsRejected));
        OnPropertyChanged(nameof(IsUnflagged));
    }

    partial void OnRatingChanged(int value)
    {
        OnPropertyChanged(nameof(HasRating));
        OnPropertyChanged(nameof(RatingStars));
    }

    partial void OnColorLabelChanged(ColorLabel value) =>
        OnPropertyChanged(nameof(HasColorLabel));

    partial void OnBurstSizeChanged(int value)
    {
        OnPropertyChanged(nameof(HasBurstGroup));
        OnPropertyChanged(nameof(BurstChipText));
    }

    partial void OnBurstIndexChanged(int value) => OnPropertyChanged(nameof(BurstChipText));

    partial void OnBurstGroupOrdinalChanged(int value) => OnPropertyChanged(nameof(BurstColorIndex));
}
