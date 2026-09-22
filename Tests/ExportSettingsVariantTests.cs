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
        Assert.Equal("photo.jpg", Path.GetFileName(Resolve(settings, "photo.CR2")));
        Assert.Equal(OutputSharpeningMode.Screen, settings.OutputSharpening);
        Assert.Equal(OutputColorSpace.Srgb, settings.OutputColorSpace);
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
    public void NothingChecked_ReturnsNoVariants()
    {
        var settings = new ExportSettings { ExportHiRes = false };

        Assert.Empty(settings.GetActiveVariants());
    }

    [Fact]
    public void FileExtensionFollowsFormat()
    {
        var settings = new ExportSettings { NamingPattern = "{name}_edited" };

        settings.Format = ExportFormat.Png;
        Assert.Equal("photo_edited.png", Path.GetFileName(Resolve(settings, "photo.jpg")));

        settings.Format = ExportFormat.Webp;
        Assert.Equal("photo_edited.webp", Path.GetFileName(Resolve(settings, "photo.jpg")));

        settings.Format = ExportFormat.Tiff;
        Assert.Equal("photo_edited.tif", Path.GetFileName(Resolve(settings, "photo.jpg")));
    }

    [Fact]
    public void OutputPath_FlatVsSubfoldered()
    {
        var outputFolder = Path.Combine(Path.GetTempPath(), "out");
        var settings = new ExportSettings { OutputFolder = outputFolder };
        var web = new ExportVariant("web", 2048);

        Assert.Equal(Path.Combine(outputFolder, "photo.jpg"),
            Resolve(settings, "photo.jpg", web, false));
        Assert.Equal(Path.Combine(outputFolder, "web", "photo.jpg"),
            Resolve(settings, "photo.jpg", web, true));
    }

    [Fact]
    public void InvalidSizesAreRejectedWithoutClamping()
    {
        var settings = new ExportSettings { ExportHiRes = false, ExportWeb = true, WebMaxSize = 4 };

        var variant = Assert.Single(settings.GetActiveVariants());
        Assert.Equal(4, variant.MaxDimension);
        Assert.Contains("long edge", settings.ValidationReason);
        Assert.Throws<InvalidOperationException>(() => settings.CreateJob([]));
    }

    [Fact]
    public void Job_DetectsTwoRecipesResolvingToSamePath()
    {
        var folder = Path.Combine(Path.GetTempPath(), "out");
        var capture = new ImageFile(Path.Combine(folder, "photo.raw"));
        var job = new ExportSettings { OutputFolder = folder }.CreateJob(
            [capture],
            [new ExportVariant("a", null), new ExportVariant("b", 100)],
            useSubfolders: false);

        var collision = Assert.Single(job.PathCollisions);
        Assert.Equal(2, collision.Targets.Count);
        Assert.All(collision.Targets, target =>
            Assert.Equal(Path.Combine(folder, "photo.jpg"), target.ResolvedPath));
    }

    [Fact]
    public void Job_DetectsTwoCapturesWithSameBasename()
    {
        var folder = Path.Combine(Path.GetTempPath(), "out");
        var job = new ExportSettings { OutputFolder = folder }.CreateJob(
            [
                new ImageFile(Path.Combine(folder, "one", "photo.raw")),
                new ImageFile(Path.Combine(folder, "two", "photo.raw"))
            ]);

        var collision = Assert.Single(job.PathCollisions);
        Assert.Equal(2, collision.Targets.Count);
        Assert.Equal(2, collision.Targets.Select(target => target.Capture).Distinct().Count());
    }
    private static string Resolve(ExportSettings settings, string name,
        ExportVariant? variant = null, bool subfolders = false) => ExportJob.ResolvePath(
            name, variant ?? new ExportVariant("hi-res", null),
            settings.SnapshotOutput() with { OutputFolder = string.IsNullOrEmpty(settings.OutputFolder) ? Path.GetTempPath() : settings.OutputFolder },
            subfolders, "20260921", string.Empty);
}
