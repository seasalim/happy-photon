using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class ImageComparisonMetricsTests
{
    [Fact]
    public void Planes_UsePinnedDisplaySrgbFormulas()
    {
        using var image = new MagickImage(MagickColors.Red, 1, 1);

        var planes = ImageComparisonMetrics.ReadPlanes(image);

        Assert.Equal(54.213, planes.Luma[0], 6);
        Assert.Equal(-54.213, planes.Cb[0], 6);
        Assert.Equal(200.787, planes.Cr[0], 6);
    }

    [Fact]
    public void FlatWindow_SelectsLowestDeviationThenTopLeft()
    {
        var values = new double[6 * 5];
        for (var y = 0; y < 5; y++)
        {
            for (var x = 0; x < 6; x++)
            {
                values[y * 6 + x] = (x + y) % 2 == 0 ? 60 : 140;
            }
        }
        SetBlock(values, 6, 2, 1, 2, 100);
        var planes = Planes(6, 5, values);

        var window = ImageComparisonMetrics.FindFlatWellLitWindow(planes, 2);

        Assert.Equal(new ComparisonWindow(2, 1, 2, 2, 100, 0), window);
    }

    [Fact]
    public void CoarseFraction_DistinguishesFineAndCoarseVariation()
    {
        const int size = 64;
        var fine = new double[size * size];
        var coarse = new double[size * size];
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                fine[y * size + x] = (x + y) % 2 == 0 ? 90 : 110;
                coarse[y * size + x] = x < size / 2 ? 90 : 110;
            }
        }
        var window = new ComparisonWindow(0, 0, size, size, 100, 10);

        var fineMetrics = ImageComparisonMetrics.Measure(
            Planes(size, size, fine), window).Luma;
        var coarseMetrics = ImageComparisonMetrics.Measure(
            Planes(size, size, coarse), window).Luma;

        Assert.NotNull(fineMetrics.CoarseFraction);
        Assert.NotNull(coarseMetrics.CoarseFraction);
        Assert.True(fineMetrics.CoarseFraction < 0.15);
        Assert.True(coarseMetrics.CoarseFraction > 0.8);
    }

    [Fact]
    public void CoarseFraction_UsesClampToEdgeAndDefinesFlatAsUndefined()
    {
        var edge = ImageComparisonMetrics.Measure(
            Planes(2, 1, [0, 9]),
            new ComparisonWindow(0, 0, 2, 1, 4.5, 4.5)).Luma;
        var flat = ImageComparisonMetrics.Measure(
            Planes(2, 1, [100, 100]),
            new ComparisonWindow(0, 0, 2, 1, 100, 0)).Luma;

        Assert.Equal(4.5, edge.TotalStandardDeviation, 10);
        Assert.Equal(0.5, edge.BlurSurvivingStandardDeviation, 10);
        Assert.Equal(1.0 / 9, edge.CoarseFraction!.Value, 10);
        Assert.Null(flat.CoarseFraction);
    }

    [Fact]
    public void Acutance_UsesCentralDifferencesOverInteriorPixelsOnly()
    {
        var luma = new double[]
        {
            0, 2, 4,
            0, 2, 4,
            0, 2, 4
        };

        var acutance = ImageComparisonMetrics.Acutance(Planes(3, 3, luma));

        Assert.Equal(2, acutance, 10);
    }

    [Fact]
    public void Bisection_ConvergesWithinPinnedExposureRange()
    {
        var result = ImageComparisonMetrics.BisectExposure(
            exposure => 100 + exposure * 20,
            targetMedian: 110);

        Assert.True(result.Converged);
        Assert.InRange(Math.Abs(result.Exposure - 0.5), 0, 0.02);
        Assert.InRange(Math.Abs(result.MedianLuma - 110), 0,
            ImageComparisonMetrics.MedianTolerance);
    }

    [Theory]
    [InlineData(20, (int)ExposureBisectionStatus.TargetBelowRange)]
    [InlineData(180, (int)ExposureBisectionStatus.TargetAboveRange)]
    public void Bisection_ReportsUnreachableTarget(
        double target,
        int expectedStatus)
    {
        var result = ImageComparisonMetrics.BisectExposure(
            exposure => 100 + exposure * 20,
            target);

        Assert.Equal((ExposureBisectionStatus)expectedStatus, result.Status);
        Assert.False(result.Converged);
    }

    [Fact]
    public void Bisection_ReportsIterationLimitForDiscontinuousResponse()
    {
        var result = ImageComparisonMetrics.BisectExposure(
            exposure => exposure < 0 ? 0 : 100,
            targetMedian: 50);

        Assert.Equal(ExposureBisectionStatus.IterationLimit, result.Status);
        Assert.False(result.Converged);
        Assert.Equal(14, result.Evaluations);
    }

    [Fact]
    public void Canonicalization_AppliesExifOrientationBeforeMeasurement()
    {
        using var source = new MagickImage(MagickColors.CornflowerBlue, 2, 3)
        {
            Orientation = OrientationType.RightTop
        };

        using var canonical = ImageComparisonMetrics.CanonicalizeReference(source);

        Assert.Equal(6, canonical.AppliedOrientation);
        Assert.Equal(3U, canonical.Image.Width);
        Assert.Equal(2U, canonical.Image.Height);
    }

    [Fact]
    public void Canonicalization_NormalizesTaggedNonSrgbPixels()
    {
        using var expected = new MagickImage(new MagickColor("#7a4cc2"), 3, 2);
        using var tagged = (MagickImage)expected.Clone();
        var displayP3 = new ColorProfile(Path.Combine(
            GoldenTestPaths.AssetDirectory,
            "DisplayP3-v4.icc"));
        tagged.TransformColorSpace(ColorProfiles.SRGB, displayP3);

        using var canonical = ImageComparisonMetrics.CanonicalizeReference(tagged);
        var expectedBytes = Bytes(expected);
        var actualBytes = Bytes(canonical.Image);

        Assert.False(canonical.AssumedSrgb);
        Assert.Equal(expectedBytes.Length, actualBytes.Length);
        Assert.All(expectedBytes.Zip(actualBytes), pair =>
            Assert.InRange(Math.Abs(pair.First - pair.Second), 0, 2));
    }

    [Fact]
    public void Canonicalization_ReportsUntaggedSrgbAssumption()
    {
        using var source = new MagickImage(MagickColors.Gray, 2, 2);
        source.RemoveProfile("icc");

        using var canonical = ImageComparisonMetrics.CanonicalizeReference(source);

        Assert.True(canonical.AssumedSrgb);
    }

    [Fact]
    public void ReferenceResolution_EnvironmentDirectoryWinsAndEnumeratesTools()
    {
        using var directory = new TemporaryDirectory();
        var committed = Directory.CreateDirectory(
            Path.Combine(directory.Path, "committed")).FullName;
        var overridden = Directory.CreateDirectory(
            Path.Combine(directory.Path, "override")).FullName;
        File.WriteAllBytes(Path.Combine(committed, "fixture.darktable.png"), [0]);
        File.WriteAllBytes(Path.Combine(overridden, "fixture.rawtherapee.tif"), [0]);
        File.WriteAllBytes(Path.Combine(overridden, "fixture.darktable.png"), [0]);
        File.WriteAllBytes(Path.Combine(overridden, "different.rawtherapee.png"), [0]);

        var result = ReferenceComparisonResolver.Resolve(
            "fixture.raf",
            committed,
            name => name == ReferenceComparisonResolver.ReferenceDirectoryEnvironmentVariable
                ? overridden
                : null);

        Assert.True(result.UsedEnvironmentOverride);
        Assert.Equal(Path.GetFullPath(overridden), result.Directory);
        Assert.Equal(["darktable", "rawtherapee"],
            result.Candidates.Select(candidate => candidate.Tool));
    }

    [Fact]
    public void ReferenceResolution_MissingReferenceExplainsSkipConvention()
    {
        using var directory = new TemporaryDirectory();
        var result = ReferenceComparisonResolver.Resolve(
            "fixture.raf",
            directory.Path,
            _ => null);

        var message = ReferenceComparisonResolver.MissingReferenceMessage(
            "fixture.raf",
            result);

        Assert.Empty(result.Candidates);
        Assert.Contains("fixture.<tool>.<ext>", message);
        Assert.Contains("HAPPY_PHOTON_COMPARE_REFERENCE_DIR", message);
    }

    [Fact]
    public void CommonResize_RefusesUpscaling()
    {
        using var image = new MagickImage(MagickColors.Black, 100, 50);

        var error = Assert.Throws<InvalidOperationException>(
            () => ImageComparisonMetrics.ResizeToCommonSize(image));

        Assert.Contains("never upscaled", error.Message);
    }

    private static ComparisonPlanes Planes(
        int width,
        int height,
        double[] luma) =>
        new(width, height, luma, new double[luma.Length], new double[luma.Length]);

    private static void SetBlock(
        double[] values,
        int width,
        int x,
        int y,
        int size,
        double value)
    {
        for (var row = 0; row < size; row++)
        {
            for (var column = 0; column < size; column++)
            {
                values[(y + row) * width + x + column] = value;
            }
        }
    }

    private static byte[] Bytes(MagickImage image)
    {
        using var pixels = image.GetPixels();
        return pixels.ToByteArray(PixelMapping.RGB)!;
    }
}
