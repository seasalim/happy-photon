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
    public string? LensModel { get; set; }
    public int PixelWidth { get; set; }
    public int PixelHeight { get; set; }
}
