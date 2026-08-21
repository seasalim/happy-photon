using System.Collections.Frozen;
using Avalonia.Media.Imaging;

namespace HappyPhoton.Models;

public partial class ImageFile
{
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
        ExposureDisplay != null ||
        CaptureConditionsDisplay != null;

    // Exceptions only: the line stays absent on default frames (no flash,
    // pattern metering, auto white balance) so it reads as a flag, not a form.
    public string? CaptureConditionsDisplay
    {
        get
        {
            var parts = new List<string>();
            if (FlashValue is { } flash && (flash & 0x1) != 0)
            {
                parts.Add("Flash fired");
            }
            if (MeteringModeName is { } metering)
            {
                parts.Add($"{metering} metering");
            }
            if (WhiteBalanceMode == 1)
            {
                parts.Add("Manual WB");
            }
            return parts.Count > 0 ? string.Join(" · ", parts) : null;
        }
    }

    private string? MeteringModeName => MeteringMode switch
    {
        1 => "Average",
        2 => "Center-weighted",
        3 => "Spot",
        4 => "Multi-spot",
        6 => "Partial",
        _ => null   // 0 unknown, 5 pattern (the default), 255 other
    };

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

    public string? ExposureTooltip
    {
        get
        {
            if (FocalLength is not { } focalLength ||
                FocalLengthIn35mmFilm is not { } equivalent)
            {
                return null;
            }

            var tooltip = $"{focalLength:F0}mm · {equivalent:F0}mm equiv";
            if (focalLength > 0 && equivalent > 0)
            {
                tooltip += $" · {equivalent / focalLength:0.#}× crop";
            }

            return tooltip;
        }
    }

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
    partial void OnMeteringModeChanged(int? value) => NotifyConditionsDisplayChanged();
    partial void OnWhiteBalanceModeChanged(int? value) => NotifyConditionsDisplayChanged();
    partial void OnFlashValueChanged(int? value) => NotifyConditionsDisplayChanged();
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

    private void NotifyConditionsDisplayChanged()
    {
        OnPropertyChanged(nameof(CaptureConditionsDisplay));
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
