using HappyPhoton.Models;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class ExportSettingsVariantTests
{
    [Fact]
    public void Defaults_HiResOnly_JpegExtension()
    {
        var settings = new ExportSettings();

        var variant = Assert.Single(settings.GetActiveVariants());
        Assert.Equal("hi-res", variant.Name);
        Assert.Null(variant.MaxDimension);
        Assert.Equal(".jpg", settings.FileExtension);
        Assert.Equal("photo.jpg", settings.GetOutputFileName("photo.CR2"));
    }

    [Fact]
    public void AllThreeChecked_OrderedOriginalFirstThenDescending()
    {
        var settings = new ExportSettings
        {
            ExportHiRes = true, ExportWeb = true, ExportSmall = true,
            WebMaxSize = 3000, SmallMaxSize = 500
        };

        var variants = settings.GetActiveVariants();

        Assert.Equal(3, variants.Count);
        Assert.Equal(("hi-res", (int?)null), (variants[0].Name, variants[0].MaxDimension));
        Assert.Equal(("web", (int?)3000), (variants[1].Name, variants[1].MaxDimension));
        Assert.Equal(("small", (int?)500), (variants[2].Name, variants[2].MaxDimension));
    }

    [Fact]
    public void NothingChecked_GuardReturnsHiRes()
    {
        var settings = new ExportSettings { ExportHiRes = false };

        var variant = Assert.Single(settings.GetActiveVariants());
        Assert.Equal("hi-res", variant.Name);
        Assert.Null(variant.MaxDimension);
    }

    [Fact]
    public void FileExtensionFollowsFormat()
    {
        var settings = new ExportSettings { NamingPattern = "{name}_edited" };

        settings.Format = ExportFormat.Png;
        Assert.Equal("photo_edited.png", settings.GetOutputFileName("photo.jpg"));

        settings.Format = ExportFormat.Webp;
        Assert.Equal("photo_edited.webp", settings.GetOutputFileName("photo.jpg"));
    }

    [Fact]
    public void OutputPath_FlatVsSubfoldered()
    {
        var outputFolder = Path.Combine(Path.GetTempPath(), "out");
        var settings = new ExportSettings { OutputFolder = outputFolder };
        var web = new ExportVariant("web", 2048);

        Assert.Equal(Path.Combine(outputFolder, "photo.jpg"),
            settings.GetOutputPath("photo.jpg", web, useSubfolders: false));
        Assert.Equal(Path.Combine(outputFolder, "web", "photo.jpg"),
            settings.GetOutputPath("photo.jpg", web, useSubfolders: true));
    }

    [Fact]
    public void SizesAreClamped()
    {
        var settings = new ExportSettings { ExportHiRes = false, ExportWeb = true, WebMaxSize = 4 };

        var variant = Assert.Single(settings.GetActiveVariants());
        Assert.Equal(16, variant.MaxDimension);
    }
}
