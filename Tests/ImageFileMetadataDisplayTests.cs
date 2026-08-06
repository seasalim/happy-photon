using HappyPhoton.Models;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class ImageFileMetadataDisplayTests
{
    private static ImageFile CreateImage() => new(@"C:\photos\a.jpg");

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
