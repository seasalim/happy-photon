using HappyPhoton.Models;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class ImageFileMetadataDisplayTests
{
    private static ImageFile CreateImage() => new(
        Path.Combine(Path.GetTempPath(), "photos", "a.jpg"));

    [Fact]
    public void ExposureDisplay_AllFields_FocalLengthFirst()
    {
        var image = CreateImage();
        image.FocalLength = 24;
        image.FNumber = 2.8;
        image.ExposureTime = "1/250";
        image.Iso = 100;

        Assert.Equal("24mm  f/2.8  1/250s  ISO 100", image.ExposureDisplay);
    }

    [Fact]
    public void ExposureDisplay_FocalLengthOnly()
    {
        var image = CreateImage();
        image.FocalLength = 24;

        Assert.Equal("24mm", image.ExposureDisplay);
    }

    [Fact]
    public void ExposureDisplay_NoFields_IsNull()
    {
        Assert.Null(CreateImage().ExposureDisplay);
    }

    [Fact]
    public void ExposureDisplay_FractionalFocalLength_Rounds()
    {
        var image = CreateImage();
        image.FocalLength = 23.7;

        Assert.Equal("24mm", image.ExposureDisplay);
    }

    [Theory]
    [InlineData(0.7, "+0.7 EV")]
    [InlineData(-0.3, "-0.3 EV")]
    public void ExposureDisplay_NonzeroBias_AppendsSignedEv(
        double bias,
        string expected)
    {
        var image = CreateImage();
        image.Iso = 100;
        image.ExposureBias = bias;

        Assert.Equal($"ISO 100  {expected}", image.ExposureDisplay);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(0.01)]
    [InlineData(-0.04)]
    public void ExposureDisplay_ZeroOrTinyBias_IsHidden(double bias)
    {
        var image = CreateImage();
        image.Iso = 100;
        image.ExposureBias = bias;

        Assert.Equal("ISO 100", image.ExposureDisplay);
    }

    [Fact]
    public void EquivalentFocalLength_AppearsOnlyInExposureTooltip()
    {
        var image = CreateImage();
        image.FocalLength = 70;
        image.FocalLengthIn35mmFilm = 105;

        Assert.Equal("70mm", image.ExposureDisplay);
        Assert.Equal("70mm · 105mm equiv · 1.5× crop", image.ExposureTooltip);
    }

    [Fact]
    public void FileDetailsDisplay_IncludesMegapixels()
    {
        var image = CreateImage();
        image.FileSize = 28_4 * 1024 * 1024 / 10;
        image.PixelWidth = 6000;
        image.PixelHeight = 4000;

        Assert.Equal(
            "6000×4000 · 24.0 MP · 28.4 MB",
            image.FileDetailsDisplay);
    }

    [Fact]
    public void GridToolTip_UsesOnlyAlreadyLoadedCatalogMetadata()
    {
        var image = CreateImage();
        Assert.Equal("a.jpg", image.GridToolTip);

        image.ApplyMetadata(new ImageMetadata
        {
            PixelWidth = 6000,
            PixelHeight = 4000,
            DateTaken = new DateTime(2026, 8, 30, 14, 15, 0)
        });

        Assert.Equal(
            $"a.jpg{Environment.NewLine}6000×4000 pixels{Environment.NewLine}" +
            "Aug 30, 2026 · 2:15 PM",
            image.GridToolTip);
    }

    [Fact]
    public void FileModifiedFallback_DoesNotChangeCaptureTimeSemantics()
    {
        var modified = new DateTime(2026, 8, 14, 12, 30, 0);
        var image = CreateImage();
        image.ApplyMetadata(new ImageMetadata { FileModifiedDate = modified });

        Assert.Null(image.DateTaken);
        Assert.Equal(modified, image.DisplayDate);
        Assert.True(image.IsFileModifiedDateFallback);
        var grouping = BurstGroupingService.ComputeGroups(
            new[] { (image.FilePath, image.DateTaken) });
        Assert.Empty(grouping.Groups);
        Assert.Equal(1, grouping.ImagesWithoutTimestamp);
    }

    [Fact]
    public void CaptureConditions_DefaultFrame_ShowsNothing()
    {
        var image = CreateImage();
        image.FlashValue = 0x10;     // no flash, compulsory suppression
        image.MeteringMode = 5;      // pattern (default)
        image.WhiteBalanceMode = 0;  // auto

        Assert.Null(image.CaptureConditionsDisplay);
    }

    [Fact]
    public void CaptureConditions_NoteworthyFrame_ListsExceptions()
    {
        var image = CreateImage();
        image.FlashValue = 0x9;      // fired, compulsory mode
        image.MeteringMode = 3;      // spot
        image.WhiteBalanceMode = 1;  // manual

        Assert.Equal(
            "Flash fired · Spot metering · Manual WB",
            image.CaptureConditionsDisplay);
        Assert.True(image.HasCameraMetadata);
    }

    [Theory]
    [InlineData(1, "Average metering")]
    [InlineData(2, "Center-weighted metering")]
    [InlineData(6, "Partial metering")]
    public void CaptureConditions_NonDefaultMetering_IsNamed(
        int mode,
        string expected)
    {
        var image = CreateImage();
        image.MeteringMode = mode;

        Assert.Equal(expected, image.CaptureConditionsDisplay);
    }

    [Theory]
    [InlineData("FUJIFILM", "X-T5", "Fujifilm X-T5")]
    [InlineData("NIKON CORPORATION", "NIKON D70", "Nikon D70")]
    [InlineData("Canon", "Canon EOS 350D", "Canon EOS 350D")]
    [InlineData("DJI", "FC3582", "DJI FC3582")]
    public void CameraDisplay_NormalizesMakeAndDropsRepeatedMakeInModel(
        string make,
        string model,
        string expected)
    {
        var image = CreateImage();
        image.CameraMake = make;
        image.CameraModel = model;

        Assert.Equal(expected, image.CameraDisplay);
    }

    [Fact]
    public void CameraAndLocationVisibility_IncludeIndependentRows()
    {
        var exposureOnly = CreateImage();
        exposureOnly.Iso = 200;
        var altitudeOnly = CreateImage();
        altitudeOnly.GpsAltitude = -12;

        Assert.True(exposureOnly.HasCameraMetadata);
        Assert.Null(exposureOnly.CameraDisplay);
        Assert.True(altitudeOnly.HasLocationMetadata);
        Assert.False(altitudeOnly.HasGpsCoordinates);
        Assert.Equal("-12 m altitude", altitudeOnly.GpsAltitudeDisplay);
    }

    [Fact]
    public void FocalLengthChange_NotifiesExposureDisplay()
    {
        var image = CreateImage();
        var changed = new List<string?>();
        image.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        image.FocalLength = 35;

        Assert.Contains(nameof(ImageFile.ExposureDisplay), changed);
    }

    [Fact]
    public void LensModelChange_Notifies()
    {
        var image = CreateImage();
        var changed = new List<string?>();
        image.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        image.LensModel = "RF 24-70mm F2.8 L IS USM";

        Assert.Contains(nameof(ImageFile.LensModel), changed);
        Assert.Equal("RF 24-70mm F2.8 L IS USM", image.LensModel);
    }
}
