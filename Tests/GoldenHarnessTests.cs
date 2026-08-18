using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class GoldenHarnessTests
{
    [Fact]
    public void PublishedSrgbMatrix_AgreesWithDerivationAndOracle() =>
        ColorScienceMatrixAssertions.AssertPublishedAndOracle(
            GoldenImageComparer.SrgbToXyzD65,
            "linear-srgb-d65",
            2.5e-4);

    [Fact]
    public void Marker_AcceptsVersionAndPending()
    {
        Assert.Equal("v0", GoldenBaselineMarker.Parse(" v0\r\n"));
        Assert.Equal("v12", GoldenBaselineMarker.Parse("v12"));
        Assert.Equal("pending", GoldenBaselineMarker.Parse("pending"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("V1")]
    [InlineData("v-1")]
    [InlineData("awaiting")]
    public void Marker_RejectsInvalidValue(string value)
    {
        Assert.Throws<InvalidOperationException>(() => GoldenBaselineMarker.Parse(value));
    }

    [Fact]
    public void Compare_IdenticalImages_HasZeroDelta()
    {
        using var expected = new MagickImage(MagickColors.CornflowerBlue, 4, 3);
        using var actual = (MagickImage)expected.Clone();

        var result = GoldenImageComparer.Compare(expected, actual);

        Assert.Equal(0, result.MeanDeltaE);
        Assert.Equal(0, result.P99DeltaE);
    }

    [Fact]
    public void Compare_BlackAndWhite_HasKnownDelta()
    {
        using var black = new MagickImage(MagickColors.Black, 1, 1);
        using var white = new MagickImage(MagickColors.White, 1, 1);

        var result = GoldenImageComparer.Compare(black, white);

        Assert.InRange(result.MeanDeltaE, 99.99, 100.01);
        Assert.InRange(result.P99DeltaE, 99.99, 100.01);
    }

    [Fact]
    public void Compare_P99UsesNearestRank()
    {
        using var expected = new MagickImage(MagickColors.Black, 100, 1);
        using var actual = new MagickImage(MagickColors.Black, 100, 1);
        using var white = new MagickImage(MagickColors.White, 2, 1);
        actual.Composite(white, 98, 0, CompositeOperator.Copy);
        using (var pixels = actual.GetPixelsUnsafe())
        {
            var whitePixels = Enumerable.Range(0, 100)
                .Count(x => pixels.GetPixel(x, 0).ToColor()!.R == Quantum.Max);
            Assert.Equal(2, whitePixels);
        }
        using (var pixels = actual.GetPixels())
        {
            var bytes = pixels.ToByteArray(PixelMapping.RGB);
            Assert.NotNull(bytes);
            Assert.Equal(6, bytes!.Count(value => value == byte.MaxValue));
            Assert.Equal(294, bytes.Count(value => value == 0));
        }

        var result = GoldenImageComparer.Compare(expected, actual);

        Assert.InRange(result.MeanDeltaE, 1.99, 2.01);
        Assert.InRange(result.P99DeltaE, 99.99, 100.01);
    }

    [Fact]
    public void Compare_DifferentDimensions_Fails()
    {
        using var expected = new MagickImage(MagickColors.Black, 2, 1);
        using var actual = new MagickImage(MagickColors.Black, 1, 2);

        Assert.Throws<InvalidOperationException>(
            () => GoldenImageComparer.Compare(expected, actual));
    }

    [Fact]
    public void TaggedSrgbAndDisplayP3References_AreEquivalent()
    {
        using var srgb = new MagickImage(
            Path.Combine(GoldenTestPaths.AssetDirectory, "srgb-reference.jpg"));
        using var displayP3 = new MagickImage(
            Path.Combine(GoldenTestPaths.AssetDirectory, "display-p3-reference.jpg"));

        var result = GoldenImageComparer.Compare(srgb, displayP3);

        Assert.True(result.MeanDeltaE <= 1.5,
            $"Tagged reference images differ: mean ΔE {result.MeanDeltaE:F3}, " +
            $"p99 ΔE {result.P99DeltaE:F3}.");
    }

    [Fact]
    public void AssetAndGoldenBudgets_AreWithinSpec()
    {
        var assets = Directory.EnumerateFiles(GoldenTestPaths.AssetDirectory)
            .Where(path => !Path.GetFileName(path).Equals("README.md",
                StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var assetBytes = assets.Sum(path => new FileInfo(path).Length);
        Assert.True(assetBytes <= 100L * 1024 * 1024,
            $"Test assets total {assetBytes / 1024d / 1024d:F2} MiB; " +
            "limit is 100 MiB.");
        Assert.All(assets, path =>
            Assert.True(new FileInfo(path).Length <= 30L * 1024 * 1024,
                $"{Path.GetFileName(path)} exceeds the 30 MiB per-file limit."));

        GoldenTestPaths.ReadActiveVersion();
        var goldenBytes = Directory.EnumerateFiles(
                GoldenTestPaths.GoldenDirectory, "*.png", SearchOption.AllDirectories)
            .Sum(path => new FileInfo(path).Length);
        Assert.True(goldenBytes <= 25L * 1024 * 1024,
            $"All goldens total {goldenBytes / 1024d / 1024d:F2} MB; limit is 25 MB.");
    }
}
