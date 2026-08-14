using HappyPhoton.Services;
using ImageMagick;
using Sdcb.LibRaw.Natives;
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
        var imageParams = new LibRawImageParams
        {
            Make = " Camera Co ",
            Model = " Model One "
        };
        var other = new LibRawImageOtherParams
        {
            IsoSpeed = 100,
            Timestamp = 1_781_400_900,
            ParsedGPS = new LibRawGPS
            {
                GPSParsed = 1,
                LatitudeDegrees = 33,
                LatitudeMinutes = 30,
                LatitudeSeconds = 0,
                LatitudeReference = (byte)'S',
                LongitudeDegrees = 151,
                LongitudeMinutes = 12,
                LongitudeSeconds = 30,
                LongitudeReference = (byte)'E',
                Altitude = 14.25f,
                AltitudeReference = 1
            }
        };
        var lens = new LibRawLensInfo
        {
            Lens = " Test Lens ",
            FocalLengthIn35mmFormat = 105
        };

        var metadata = LibRawProcessingService.CreateMetadata(
            imageParams,
            other,
            lens,
            6000,
            4000);

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
        var other = new LibRawImageOtherParams
        {
            ParsedGPS = new LibRawGPS { GPSParsed = 1, Altitude = 42 }
        };

        var metadata = LibRawProcessingService.CreateMetadata(
            new LibRawImageParams(),
            other,
            new LibRawLensInfo(),
            6000,
            4000);

        Assert.Null(metadata.GpsLatitude);
        Assert.Null(metadata.GpsLongitude);
        Assert.Equal(42, metadata.GpsAltitude);
    }

    [Fact]
    public void CreateMetadata_ZeroedGpsBlock_YieldsNoLocation()
    {
        var other = new LibRawImageOtherParams
        {
            ParsedGPS = new LibRawGPS { GPSParsed = 1 }
        };

        var metadata = LibRawProcessingService.CreateMetadata(
            new LibRawImageParams(),
            other,
            new LibRawLensInfo(),
            6000,
            4000);

        Assert.Null(metadata.GpsLatitude);
        Assert.Null(metadata.GpsLongitude);
        Assert.Null(metadata.GpsAltitude);
    }
}
