namespace HappyPhoton.Models;

/// <summary>
/// Metadata extracted from RAW files via LibRaw.
/// </summary>
public class RawMetadata
{
    public string? CameraMake { get; set; }
    public string? CameraModel { get; set; }
    public DateTime? DateTaken { get; set; }
    public double? FNumber { get; set; }
    public double? ExposureTime { get; set; }
    public int? Iso { get; set; }
    public double? FocalLength { get; set; }
    public double? FocalLengthIn35mmFilm { get; set; }
    public double? ExposureBias { get; set; }
    public string? LensModel { get; set; }
    public double? GpsLatitude { get; set; }
    public double? GpsLongitude { get; set; }
    public double? GpsAltitude { get; set; }
    public int PixelWidth { get; set; }
    public int PixelHeight { get; set; }
}
