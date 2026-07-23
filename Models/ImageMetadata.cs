namespace HappyPhoton.Models;

public sealed record ImageMetadata
{
    public long FileSize { get; init; }
    public int PixelWidth { get; init; }
    public int PixelHeight { get; init; }
    public DateTime? DateTaken { get; init; }
    public string? CameraMake { get; init; }
    public string? CameraModel { get; init; }
    public double? FNumber { get; init; }
    public string? ExposureTime { get; init; }
    public int? Iso { get; init; }
    public double? FocalLength { get; init; }
    public string? LensModel { get; init; }
    public double? GpsLatitude { get; init; }
    public double? GpsLongitude { get; init; }
}
