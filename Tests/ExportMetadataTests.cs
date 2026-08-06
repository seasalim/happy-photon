using HappyPhoton.Models;
using HappyPhoton.Services;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class ExportMetadataTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine(
        Path.GetTempPath(),
        $"HappyPhotonMetadataTests_{Guid.NewGuid():N}");

    public ExportMetadataTests() =>
        Directory.CreateDirectory(_tempDirectory);

    [Fact]
    public void Apply_CopiesExifAndNormalizesRebuiltPixels()
    {
        var sourcePath = Path.Combine(_tempDirectory, "source.jpg");
        using (var source = new MagickImage(MagickColors.Orange, 40, 30))
        {
            var profile = new ExifProfile();
            profile.SetValue(ExifTag.Make, "Camera Co");
            profile.SetValue(
                ExifTag.DateTimeOriginal,
                "2026:02:03 04:05:06");
            profile.SetValue(ExifTag.Orientation, (ushort)6);
            profile.SetValue(ExifTag.PixelXDimension, new Number(40));
            profile.SetValue(ExifTag.PixelYDimension, new Number(30));
            profile.SetValue(ExifTag.JPEGInterchangeFormat, 128u);
            profile.SetValue(ExifTag.JPEGInterchangeFormatLength, 64u);
            source.SetProfile(profile);
            source.Write(sourcePath);
        }

        using var destination = new MagickImage(
            MagickColors.Blue,
            20,
            15);
        new ExportMetadataService("Happy Photon 9.8.7").Apply(
            new ImageFile(sourcePath),
            destination,
            stripLocationData: false);

        var result = destination.GetExifProfile();
        Assert.NotNull(result);
        Assert.Equal(
            "Camera Co",
            result!.GetValue(ExifTag.Make)?.Value);
        Assert.Equal(
            "2026:02:03 04:05:06",
            result.GetValue(ExifTag.DateTimeOriginal)?.Value);
        Assert.Equal(
            (ushort)1,
            result.GetValue(ExifTag.Orientation)?.Value);
        Assert.Equal(
            "Happy Photon 9.8.7",
            result.GetValue(ExifTag.Software)?.Value);
        Assert.Null(result.GetValue(ExifTag.PixelXDimension));
        Assert.Null(result.GetValue(ExifTag.PixelYDimension));
        Assert.Null(result.GetValue(ExifTag.JPEGInterchangeFormat));
        Assert.Null(result.GetValue(ExifTag.JPEGInterchangeFormatLength));
        Assert.Equal(0u, result.ThumbnailLength);
    }

    [Fact]
    public void Apply_MissingExifSynthesizesRawCaptureMetadata()
    {
        var source = new ImageFile(Path.Combine(
            _tempDirectory,
            "missing.dng"))
        {
            CameraMake = "Raw Camera Co",
            CameraModel = "R1",
            DateTaken = new DateTime(2025, 11, 12, 13, 14, 15),
            Iso = 640,
            FNumber = 2.8,
            ExposureTime = "1/125",
            FocalLength = 50,
            LensModel = "Prime 50"
        };
        using var destination = new MagickImage(
            MagickColors.Blue,
            20,
            15);

        new ExportMetadataService("Happy Photon 9.8.7").Apply(
            source,
            destination,
            stripLocationData: false);

        var result = destination.GetExifProfile();
        Assert.NotNull(result);
        Assert.Equal(
            "Raw Camera Co",
            result!.GetValue(ExifTag.Make)?.Value);
        Assert.Equal("R1", result.GetValue(ExifTag.Model)?.Value);
        Assert.Equal(
            "2025:11:12 13:14:15",
            result.GetValue(ExifTag.DateTimeOriginal)?.Value);
        Assert.Equal(
            (ushort)640,
            Assert.Single(
                result.GetValue(ExifTag.ISOSpeedRatings)!.Value));
        Assert.Equal(
            2.8,
            result.GetValue(ExifTag.FNumber)!.Value.ToDouble(),
            3);
        Assert.Equal(
            1.0 / 125,
            result.GetValue(ExifTag.ExposureTime)!.Value.ToDouble(),
            6);
        Assert.Equal(
            50,
            result.GetValue(ExifTag.FocalLength)!.Value.ToDouble(),
            3);
        Assert.Equal(
            "Prime 50",
            result.GetValue(ExifTag.LensModel)?.Value);
    }

    [Fact]
    public void Apply_PartialExifSupplementsMissingCaptureMetadata()
    {
        var sourcePath = Path.Combine(_tempDirectory, "partial.jpg");
        using (var image = new MagickImage(MagickColors.Orange, 40, 30))
        {
            var profile = new ExifProfile();
            profile.SetValue(ExifTag.Model, "Source Model");
            image.SetProfile(profile);
            image.Write(sourcePath);
        }

        var source = new ImageFile(sourcePath)
        {
            CameraMake = "Catalog Make",
            CameraModel = "Catalog Model",
            Iso = 320
        };
        using var destination = new MagickImage(
            MagickColors.Blue,
            20,
            15);

        new ExportMetadataService("Happy Photon 9.8.7").Apply(
            source,
            destination,
            stripLocationData: false);

        var result = destination.GetExifProfile();
        Assert.NotNull(result);
        Assert.Equal(
            "Catalog Make",
            result!.GetValue(ExifTag.Make)?.Value);
        Assert.Equal(
            "Source Model",
            result.GetValue(ExifTag.Model)?.Value);
        Assert.Equal(
            (ushort)320,
            Assert.Single(
                result.GetValue(ExifTag.ISOSpeedRatings)!.Value));
    }

    [SkippableFact]
    public void Apply_ActualRawCarriesCaptureMetadata()
    {
        var source = new ImageFile(Path.Combine(
            GoldenTestPaths.AssetDirectory,
            "canon-eos-350d.cr2"));
        IRawProcessingService rawService = new LibRawProcessingService();
        Assert.True(rawService.IsAvailable);
        source.ApplyMetadata(MetadataService.ExtractMetadata(
            source,
            rawService));
        using var destination = new MagickImage(
            MagickColors.Blue,
            20,
            15);

        new ExportMetadataService("Happy Photon 9.8.7").Apply(
            source,
            destination,
            stripLocationData: false);

        var result = destination.GetExifProfile();
        Assert.NotNull(result);
        Assert.False(string.IsNullOrWhiteSpace(
            result!.GetValue(ExifTag.Make)?.Value));
        Assert.False(string.IsNullOrWhiteSpace(
            result.GetValue(ExifTag.Model)?.Value));
        Assert.NotNull(result.GetValue(ExifTag.DateTimeOriginal));
        Assert.NotNull(result.GetValue(ExifTag.ISOSpeedRatings));
        Assert.NotNull(result.GetValue(ExifTag.FNumber));
        Assert.NotNull(result.GetValue(ExifTag.ExposureTime));
    }

    [Theory]
    [InlineData(ExportFormat.Jpeg, ".jpg")]
    [InlineData(ExportFormat.Png, ".png")]
    [InlineData(ExportFormat.Webp, ".webp")]
    public void Encode_AllFormatsKeepNormalizedExif(
        ExportFormat format,
        string extension)
    {
        var source = new ImageFile(Path.Combine(
            _tempDirectory,
            "missing.dng"))
        {
            CameraMake = "Raw Camera Co"
        };
        using var destination = new MagickImage(
            MagickColors.Blue,
            20,
            15);
        new ExportMetadataService("Happy Photon 9.8.7").Apply(
            source,
            destination,
            stripLocationData: false);
        var outputPath = Path.Combine(
            _tempDirectory,
            $"metadata{extension}");

        ExportEncoder.Write(
            destination,
            new ExportSettings { Format = format },
            outputPath);

        using var result = new MagickImage(outputPath);
        var profile = result.GetExifProfile();
        Assert.NotNull(profile);
        Assert.Equal(
            "Raw Camera Co",
            profile!.GetValue(ExifTag.Make)?.Value);
        Assert.Equal(
            (ushort)1,
            profile.GetValue(ExifTag.Orientation)?.Value);
        Assert.Equal(
            "Happy Photon 9.8.7",
            profile.GetValue(ExifTag.Software)?.Value);
        Assert.NotNull(result.GetColorProfile());
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void Apply_StripLocationControlsWholeGpsIfd(
        bool stripLocationData,
        bool expectGps)
    {
        var sourcePath = Path.Combine(
            GoldenTestPaths.AssetDirectory,
            "srgb-exif-gps-orientation-6.jpg");
        using var destination = new MagickImage(
            MagickColors.Blue,
            20,
            15);

        new ExportMetadataService("Happy Photon 9.8.7").Apply(
            new ImageFile(sourcePath),
            destination,
            stripLocationData);

        var result = destination.GetExifProfile();
        Assert.NotNull(result);
        Assert.Equal(
            expectGps,
            result!.Values.Any(value => value.Tag.Ifd == ExifIfds.Gps));
        Assert.Equal(
            expectGps,
            result.GetValue(ExifTag.GPSLatitude) != null);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }
}
