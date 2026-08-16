using ImageMagick;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class ImageStatsServiceTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine(
        Path.GetTempPath(), $"HappyPhotonStatsTests_{Guid.NewGuid():N}");

    public ImageStatsServiceTests() => Directory.CreateDirectory(_tempDirectory);

    [Theory]
    [InlineData("source.dng")]
    [InlineData("source.NEF")]
    [InlineData("source.raf")]
    public void RawSourcePath_IsRejectedBeforeMagickDecode(string fileName)
    {
        var path = Path.Combine(_tempDirectory, fileName);
        File.WriteAllText(path, "not an image");

        var error = Assert.Throws<NotSupportedException>(() =>
            new ImageStatsService().Compute(path));

        Assert.Contains("LibRaw", error.Message);
    }

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

    [Fact]
    public void CloudPathIsBlocked_WhileCallerOwnedBytesRemainReadable()
    {
        var path = WriteImage("cloud.jpg", _ => { });
        var bytes = File.ReadAllBytes(path);
        var service = new ImageStatsService(
            new TestSourceAvailabilityService(
                SourceAvailability.RequiresHydration));

        Assert.Throws<SourceReadDeferredException>(() => service.Compute(path));
        Assert.True(service.Compute(bytes).MeanLuminance > 0);
    }

    [Fact]
    public void PromotedCache_IsNormalizedToCanonicalLongEdge()
    {
        using var original = new MagickImage("pattern:checkerboard", 900, 600);
        original.GaussianBlur(0, 1);
        var smallPath = Path.Combine(_tempDirectory, "normalized-150.jpg");
        var largePath = Path.Combine(_tempDirectory, "normalized-512.jpg");
        WriteVariant(original, smallPath, 150);
        WriteVariant(original, largePath, 512);
        var service = new ImageStatsService();

        var small = service.Compute(smallPath);
        var large = service.Compute(largePath);
        var relativeSharpnessDifference = Math.Abs(
            small.Sharpness - large.Sharpness) /
            Math.Max(small.Sharpness, large.Sharpness);

        Assert.InRange(relativeSharpnessDifference, 0, 0.35);
        Assert.InRange(Math.Abs(
            small.MeanLuminance - large.MeanLuminance), 0, 2);
    }

    private static void WriteVariant(
        MagickImage source,
        string path,
        int longEdge)
    {
        using var variant = new MagickImage(source);
        var scale = longEdge / (double)Math.Max(variant.Width, variant.Height);
        variant.FilterType = FilterType.Lanczos;
        variant.Resize(
            (uint)Math.Round(variant.Width * scale),
            (uint)Math.Round(variant.Height * scale));
        variant.Quality = 85;
        variant.Write(path, MagickFormat.Jpeg);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
            Directory.Delete(_tempDirectory, recursive: true);
    }
}
