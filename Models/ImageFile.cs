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
            ".cr2", ".cr3", ".nef", ".nrw", ".arw", ".srf", ".sr2",
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
    private bool _sourceRequiresHydration;

    public bool ShowCloudPlaceholder =>
        SourceRequiresHydration && Thumbnail == null;

    public EditSettings EditSettings { get; set; } = new();

    /// <summary>
    /// The catalog database ID for this image. Set after loading from catalog.
    /// </summary>
    public long CatalogId { get; set; }

    public bool ThumbnailLoadFailed { get; set; }

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
    [ObservableProperty] private string? _cameraMake;
    [ObservableProperty] private string? _cameraModel;
    [ObservableProperty] private double? _fNumber;
    [ObservableProperty] private string? _exposureTime;
    [ObservableProperty] private int? _iso;
    [ObservableProperty] private double? _focalLength;
    [ObservableProperty] private string? _lensModel;
    [ObservableProperty] private double? _gpsLatitude;
    [ObservableProperty] private double? _gpsLongitude;
    [ObservableProperty] private bool _metadataLoaded;

    public void ApplyMetadata(ImageMetadata metadata)
    {
        FileSize = metadata.FileSize;
        PixelWidth = metadata.PixelWidth;
        PixelHeight = metadata.PixelHeight;
        DateTaken = metadata.DateTaken;
        CameraMake = metadata.CameraMake;
        CameraModel = metadata.CameraModel;
        FNumber = metadata.FNumber;
        ExposureTime = metadata.ExposureTime;
        Iso = metadata.Iso;
        FocalLength = metadata.FocalLength;
        LensModel = metadata.LensModel;
        GpsLatitude = metadata.GpsLatitude;
        GpsLongitude = metadata.GpsLongitude;
        MetadataLoaded = true;
    }

    // Computed metadata display properties
    public string FileSizeDisplay => FileSize switch
    {
        < 1024 => $"{FileSize} B",
        < 1024 * 1024 => $"{FileSize / 1024.0:F1} KB",
        < 1024 * 1024 * 1024 => $"{FileSize / (1024.0 * 1024.0):F1} MB",
        _ => $"{FileSize / (1024.0 * 1024.0 * 1024.0):F2} GB"
    };

    public string? CameraDisplay => string.IsNullOrEmpty(CameraModel) ? CameraMake : CameraMake + " " + CameraModel;

    public string? ExposureDisplay
    {
        get
        {
            var parts = new List<string>();
            if (FocalLength.HasValue) parts.Add($"{FocalLength.Value:F0}mm");
            if (FNumber.HasValue) parts.Add($"f/{FNumber:F1}");
            if (!string.IsNullOrEmpty(ExposureTime)) parts.Add($"{ExposureTime}s");
            if (Iso.HasValue) parts.Add($"ISO {Iso}");
            return parts.Count > 0 ? string.Join("  ", parts) : null;
        }
    }

    public string? GpsDisplay
    {
        get
        {
            if (!GpsLatitude.HasValue || !GpsLongitude.HasValue) return null;
            var latDir = GpsLatitude >= 0 ? "N" : "S";
            var lonDir = GpsLongitude >= 0 ? "E" : "W";
            return $"{Math.Abs(GpsLatitude.Value):F4}° {latDir}, {Math.Abs(GpsLongitude.Value):F4}° {lonDir}";
        }
    }

    partial void OnFileSizeChanged(long value) => OnPropertyChanged(nameof(FileSizeDisplay));
    partial void OnThumbnailChanged(Bitmap? value)
    {
        ThumbnailPixelWidth = value?.PixelSize.Width ?? 0;
        ThumbnailPixelHeight = value?.PixelSize.Height ?? 0;
        ThumbnailGeneration++;
        OnPropertyChanged(nameof(ShowCloudPlaceholder));
    }
    partial void OnSourceRequiresHydrationChanged(bool value) =>
        OnPropertyChanged(nameof(ShowCloudPlaceholder));
    partial void OnCameraMakeChanged(string? value) => OnPropertyChanged(nameof(CameraDisplay));
    partial void OnCameraModelChanged(string? value) => OnPropertyChanged(nameof(CameraDisplay));
    partial void OnFNumberChanged(double? value) => OnPropertyChanged(nameof(ExposureDisplay));
    partial void OnExposureTimeChanged(string? value) => OnPropertyChanged(nameof(ExposureDisplay));
    partial void OnIsoChanged(int? value) => OnPropertyChanged(nameof(ExposureDisplay));
    partial void OnFocalLengthChanged(double? value) => OnPropertyChanged(nameof(ExposureDisplay));
    partial void OnGpsLatitudeChanged(double? value) => OnPropertyChanged(nameof(GpsDisplay));
    partial void OnGpsLongitudeChanged(double? value) => OnPropertyChanged(nameof(GpsDisplay));
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

    partial void OnBurstSizeChanged(int value)
    {
        OnPropertyChanged(nameof(HasBurstGroup));
        OnPropertyChanged(nameof(BurstChipText));
    }

    partial void OnBurstIndexChanged(int value) => OnPropertyChanged(nameof(BurstChipText));

    partial void OnBurstGroupOrdinalChanged(int value) => OnPropertyChanged(nameof(BurstColorIndex));
}
