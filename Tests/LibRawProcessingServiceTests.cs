using HappyPhoton.Services;
using HappyPhoton.LibRaw.Interop;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class LibRawProcessingServiceTests
{
    [Fact]
    public void ExtractThumbnail_ReturnsDecodableBundledPreview()
    {
        var service = new LibRawProcessingService();
        var path = Path.Combine(
            GoldenTestPaths.AssetDirectory,
            "canon-eos-350d.cr2");

        var data = service.ExtractThumbnail(path);

        Assert.True(service.IsAvailable);
        Assert.NotNull(data);
        Assert.NotEmpty(data!.EncodedBytes);
        Assert.True(data.VisibleSourceWidth > 0);
        Assert.True(data.VisibleSourceHeight > 0);
        using var image = new MagickImage(data.EncodedBytes);
        Assert.True(image.Width > 0);
        Assert.True(image.Height > 0);
    }

    [Fact]
    public void CreateMetadata_ConvertsGpsReferencesAltitudeAndEquivalentFocalLength()
    {
        var source = Metadata(" Camera Co ", " Model One ", " Test Lens ",
            new(true, -33.5, 151.208333, -14.25f), timestamp: 1_781_400_900,
            iso: 100, focalLength35mm: 105);

        var metadata = LibRawProcessingService.CreateMetadata(
            source,
            Dimensions(6000, 4000));

        Assert.Equal(
            DateTimeOffset.FromUnixTimeSeconds(1_781_400_900).LocalDateTime,
            metadata.DateTaken);
        Assert.Equal("Camera Co", metadata.CameraMake);
        Assert.Equal("Model One", metadata.CameraModel);
        Assert.Equal("Test Lens", metadata.LensModel);
        Assert.Equal(-33.5, metadata.GpsLatitude);
        Assert.Equal(151.208333, metadata.GpsLongitude!.Value, 6);
        Assert.Equal(-14.25, metadata.GpsAltitude);
        Assert.Equal(105, metadata.FocalLengthIn35mmFilm);
    }

    [Fact]
    public void CreateMetadata_AltitudeOnlyGps_KeepsCoordinatesAbsent()
    {
        var metadata = LibRawProcessingService.CreateMetadata(
            Metadata(gps: new(true, null, null, 42)),
            Dimensions(6000, 4000));

        Assert.Null(metadata.GpsLatitude);
        Assert.Null(metadata.GpsLongitude);
        Assert.Equal(42, metadata.GpsAltitude);
    }

    [Fact]
    public void CreateMetadata_ZeroedGpsBlock_YieldsNoLocation()
    {
        var metadata = LibRawProcessingService.CreateMetadata(
            Metadata(gps: new(true, null, null, null)),
            Dimensions(6000, 4000));

        Assert.Null(metadata.GpsLatitude);
        Assert.Null(metadata.GpsLongitude);
        Assert.Null(metadata.GpsAltitude);
    }

    private static LibRawMetadata Metadata(
        string? make = null, string? model = null, string? lens = null,
        LibRawGpsFacts? gps = null, long? timestamp = null, float? iso = null,
        float? focalLength35mm = null) => new(
            make, model, make, model, lens, iso, null, null, null,
            focalLength35mm, timestamp, 1, gps ?? new(false, null, null, null));

    private static LibRawDimensions Dimensions(uint width, uint height) =>
        new(width, height, width, height, width, height, 1);
}
