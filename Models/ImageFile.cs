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
        SourceRequiresHydration = sourceAvailabilityHint.IsOnlineOnly();
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
    [ObservableProperty] private int? _meteringMode;
    [ObservableProperty] private int? _whiteBalanceMode;
    [ObservableProperty] private int? _flashValue;
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
        MeteringMode = metadata.MeteringMode;
        WhiteBalanceMode = metadata.WhiteBalanceMode;
        FlashValue = metadata.FlashValue;
        LensModel = metadata.LensModel;
        GpsLatitude = metadata.GpsLatitude;
        GpsLongitude = metadata.GpsLongitude;
        GpsAltitude = metadata.GpsAltitude;
        MetadataLoaded = true;
    }
}
