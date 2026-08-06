using ImageMagick;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class ImageStatsServiceTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine(
        Path.GetTempPath(), $"HappyPhotonStatsTests_{Guid.NewGuid():N}");

    public ImageStatsServiceTests() => Directory.CreateDirectory(_tempDirectory);

    private string WriteImage(string name, Action<MagickImage> mutate)
    {
        using var image = new MagickImage(MagickColors.Gray, 256, 256);
        mutate(image);
        var path = Path.Combine(_tempDirectory, name);
        image.Write(path);
        return path;
    }

    private string WriteCheckerboard(string name, bool blurred)
    {
        using var image = new MagickImage("pattern:checkerboard", 256, 256);
        if (blurred) image.GaussianBlur(0, 6);
        var path = Path.Combine(_tempDirectory, name);
        image.Write(path);
        return path;
    }

    [Fact]
    public void SharpImage_ScoresHigherThanBlurredCopy()
    {
        var sharp = WriteCheckerboard("sharp.jpg", blurred: false);
        var blurred = WriteCheckerboard("blurred.jpg", blurred: true);

        var service = new ImageStatsService();
        Assert.True(service.Compute(sharp).Sharpness > service.Compute(blurred).Sharpness);
    }

    [Fact]
    public void WhiteImage_ReportsClippedHighlights()
    {
        var path = WriteImage(
            "white.jpg", image => image.Colorize(MagickColors.White, new Percentage(100)));

        var stats = new ImageStatsService().Compute(path);

        Assert.True(stats.ClippedHighlightsPct > 95);
        Assert.True(stats.ClippedShadowsPct < 5);
    }

    [Fact]
    public void BlackImage_ReportsClippedShadows()
    {
        var path = WriteImage(
            "black.jpg", image => image.Colorize(MagickColors.Black, new Percentage(100)));

        var stats = new ImageStatsService().Compute(path);

        Assert.True(stats.ClippedShadowsPct > 95);
        Assert.True(stats.ClippedHighlightsPct < 5);
    }

    [Fact]
    public void MidGray_HasNoClippingAndMidLuminance()
    {
        var path = WriteImage("gray.jpg", _ => { });

        var stats = new ImageStatsService().Compute(path);

        Assert.True(stats.ClippedHighlightsPct < 1);
        Assert.True(stats.ClippedShadowsPct < 1);
        Assert.InRange(stats.MeanLuminance, 100, 156);
    }

    [Fact]
    public void EncodedImageData_ProducesTheSameStatsAsAFile()
    {
        var path = WriteCheckerboard("stats.jpg", blurred: false);
        var service = new ImageStatsService();

        var fromFile = service.Compute(path);
        var fromData = service.Compute(File.ReadAllBytes(path));

        Assert.Equal(fromFile.Sharpness, fromData.Sharpness, precision: 10);
        Assert.Equal(fromFile.ClippedHighlightsPct, fromData.ClippedHighlightsPct, precision: 10);
        Assert.Equal(fromFile.ClippedShadowsPct, fromData.ClippedShadowsPct, precision: 10);
        Assert.Equal(fromFile.MeanLuminance, fromData.MeanLuminance, precision: 10);
    }

    [Fact]
    public void MissingImage_ThrowsFileNotFound()
    {
        var path = Path.Combine(_tempDirectory, "missing.jpg");

        Assert.Throws<FileNotFoundException>(() => new ImageStatsService().Compute(path));
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
            Directory.Delete(_tempDirectory, recursive: true);
    }
}
