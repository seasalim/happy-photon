using HappyPhoton.Models;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public sealed partial class LensCorrectionProcessorTests
{
    private const double FujiNativePixelsPerTableRadiusUnit = 1.9;

    [Fact]
    public void OutputSizeMapsTrimWithinDefaultCropBeforeOrientationAndResize()
    {
        var prescription = new LensPrescription(
            LensPrescriptionSource.DngOpcode,
            null,
            [], [],
            new LensFrameWindow(0.1, 0.2, 0.9, 0.8),
            new LensFrameWindow(0.2, 0.3, 0.8, 0.7));

        Assert.Equal((40, 60), LensCorrectionProcessor.GetOutputSize(
            80, 60, orientation: 6, maxDimension: null, prescription));
        Assert.Equal((20, 30), LensCorrectionProcessor.GetOutputSize(
            80, 60, orientation: 6, maxDimension: 30, prescription));
    }

    [Fact]
    public void SyntheticDistortionAndCaOracleInvertsWithinQuarterPixelAt1600()
    {
        const int size = 1600;
        double[] radial = [-0.10, -0.06, -0.02];
        var source = InjectRadialCoordinateField(size, radial);
        var planes = radial.Select(value =>
            new LensWarpCoefficients(1, value, 0, 0, 0, 0)).ToArray();
        var prescription = new LensPrescription(
            LensPrescriptionSource.DngOpcode,
            null,
            [new LensWarp(planes, 0.5, 0.5)],
            [], LensFrameWindow.Full, LensFrameWindow.Full);

        using var image = LensCorrectionProcessor.ImportCorrected(
            source, size, size, size, size, 1,
            CameraRgbCharacterization.Passthrough,
            prescription, BaseDecodeSettings.Default, CancellationToken.None);
        using var pixels = image.GetPixelsUnsafe();
        var values = pixels.ToShortArray(ImageMagick.PixelMapping.RGB)!;
        var maximumStraightness = 0.0;
        var maximumChannelSeparation = 0.0;
        for (var y = 53; y < size; y += 149)
        for (var x = 47; x < size; x += 97)
        {
            var offset = (y * size + x) * 3;
            var expected = (x + 0.5) / size;
            for (var channel = 0; channel < 3; channel++)
            {
                var recovered = values[offset + channel] / (double)ushort.MaxValue;
                maximumStraightness = Math.Max(
                    maximumStraightness, Math.Abs(recovered - expected) * size);
            }
            maximumChannelSeparation = Math.Max(maximumChannelSeparation,
                (values.Skip(offset).Take(3).Max() -
                 values.Skip(offset).Take(3).Min()) /
                (double)ushort.MaxValue * size);
        }

        Assert.True(maximumStraightness <= 0.25,
            $"Straight-line residual was {maximumStraightness:F3} px.");
        Assert.True(maximumChannelSeparation <= 0.25,
            $"Channel separation was {maximumChannelSeparation:F3} px.");
    }

    [Fact]
    public void SyntheticKnotTableOracleInvertsWithinQuarterPixelAt1600()
    {
        const int size = 1600;
        var maximum = Math.Sqrt(2) * (size - 1) * 0.5;
        var distortion = new LensRadialTable(
            maximum / (FujiNativePixelsPerTableRadiusUnit * 3),
            [0, 0.5, 1], [0, -3, -8],
            FujiNativePixelsPerTableRadiusUnit, 1.0 / 45);
        var ca = new LensChromaticAberrationTable(
            maximum / (FujiNativePixelsPerTableRadiusUnit * 3),
            [0, 0.5, 1], [0, 0.0002, 0.0004],
            [0, -0.00015, -0.0003],
            FujiNativePixelsPerTableRadiusUnit);
        var source = InjectTableCoordinateField(size, distortion, ca);
        var prescription = new LensPrescription(
            LensPrescriptionSource.FujifilmMakerNote,
            null, [], [], LensFrameWindow.Full, LensFrameWindow.Full,
            TableWarps: [new LensTableWarp(distortion, ca)]);

        using var image = LensCorrectionProcessor.ImportCorrected(
            source, size, size, size, size, 1,
            CameraRgbCharacterization.Passthrough,
            prescription, BaseDecodeSettings.Default, CancellationToken.None);
        using var pixels = image.GetPixelsUnsafe();
        var values = pixels.ToShortArray(ImageMagick.PixelMapping.RGB)!;
        var maximumResidual = 0.0;
        for (var y = 53; y < size; y += 149)
        for (var x = 47; x < size; x += 97)
        for (var channel = 0; channel < 3; channel++)
        {
            var recovered = values[(y * size + x) * 3 + channel] /
                (double)ushort.MaxValue;
            maximumResidual = Math.Max(maximumResidual,
                Math.Abs(recovered - (x + 0.5) / size) * size);
        }

        Assert.True(maximumResidual <= 0.25,
            $"Knot-table residual was {maximumResidual:F3} px.");
    }

    [Fact]
    public void SyntheticTableVignettingRestoresGainWithinOnePercent()
    {
        const int size = 401;
        const ushort target = 24000;
        var maximum = Math.Sqrt(2) * (size - 1) * 0.5;
        var table = new LensRadialTable(
            maximum / (FujiNativePixelsPerTableRadiusUnit * 3),
            [0, 0.5, 1], [100, 80, 50],
            FujiNativePixelsPerTableRadiusUnit);
        var values = new ushort[size * size * 3];
        for (var y = 0; y < size; y++)
        for (var x = 0; x < size; x++)
        {
            var radius = Math.Sqrt(
                Math.Pow(x - (size - 1) * 0.5, 2) +
                Math.Pow(y - (size - 1) * 0.5, 2)) / maximum;
            var transmission = radius <= 0.5
                ? 100 + (80 - 100) * radius / 0.5
                : 80 + (50 - 80) * (radius - 0.5) / 0.5;
            var encoded = (ushort)Math.Round(target * transmission / 100);
            var offset = (y * size + x) * 3;
            values[offset] = values[offset + 1] = values[offset + 2] = encoded;
        }
        var prescription = new LensPrescription(
            LensPrescriptionSource.FujifilmMakerNote,
            null, [], [], LensFrameWindow.Full, LensFrameWindow.Full,
            TableVignettes: [new LensTableVignette(table)]);

        using var image = LensCorrectionProcessor.ImportCorrected(
            ToBytes(values), size, size, size, size, 1,
            CameraRgbCharacterization.Passthrough,
            prescription, BaseDecodeSettings.Default with { Vignetting = true },
            CancellationToken.None);
        using var pixels = image.GetPixelsUnsafe();
        var corrected = pixels.ToShortArray(ImageMagick.PixelMapping.RGB)!;
        var center = ((size / 2 * size) + size / 2) * 3;

        Assert.InRange(corrected[0] / (double)corrected[center], 0.99, 1.01);
    }

    [Fact]
    public void VignettingGainRunsBeforeCharacterizationInOneSamplingPass()
    {
        const int size = 101;
        var source = SolidRgb(size, size, 10000, 20000, 30000);
        var prescription = new LensPrescription(
            LensPrescriptionSource.DngOpcode,
            null,
            [],
            [new LensVignette(1, 0, 0, 0, 0, 0.5, 0.5)],
            LensFrameWindow.Full,
            LensFrameWindow.Full);
        var settings = new BaseDecodeSettings(
            HlReconstructionMode.Clip,
            FbddMode.Off,
            Distortion: false,
            ChromaticAberration: false,
            Vignetting: true);
        var passes = 0;
        LensCorrectionProcessor.SamplingPassStarted = () => passes++;
        try
        {
            using var image = LensCorrectionProcessor.ImportCorrected(
                source, size, size, size, size, 1,
                CameraRgbCharacterization.Passthrough,
                prescription, settings, CancellationToken.None);
            using var pixels = image.GetPixelsUnsafe();
            var values = pixels.ToShortArray(ImageMagick.PixelMapping.RGB)!;
            var center = ((size / 2 * size) + size / 2) * 3;
            var corner = 0;

            Assert.Equal(1, passes);
            Assert.InRange(values[center], 9999, 10001);
            Assert.InRange(values[center + 1], 19999, 20001);
            Assert.InRange(values[corner], 19800, 20200);
            Assert.InRange(values[corner + 1], 39600, 40400);
            Assert.InRange(values[corner + 2], 59400, 60600);
        }
        finally
        {
            LensCorrectionProcessor.SamplingPassStarted = null;
        }
    }

    [Fact]
    public void SyntheticVignettingOracleRestoresCornerCenterRatioWithinOnePercent()
    {
        const int size = 401;
        const ushort target = 24000;
        var values = new ushort[size * size * 3];
        for (var y = 0; y < size; y++)
        for (var x = 0; x < size; x++)
        {
            var dx = ((x + 0.5) / size - 0.5) / 0.7071067811865476;
            var dy = ((y + 0.5) / size - 0.5) / 0.7071067811865476;
            var encoded = (ushort)Math.Round(target / (1 + dx * dx + dy * dy));
            var offset = (y * size + x) * 3;
            values[offset] = values[offset + 1] = values[offset + 2] = encoded;
        }
        var prescription = new LensPrescription(
            LensPrescriptionSource.DngOpcode,
            null,
            [], [new LensVignette(1, 0, 0, 0, 0, 0.5, 0.5)],
            LensFrameWindow.Full, LensFrameWindow.Full);
        var settings = BaseDecodeSettings.Default with { Vignetting = true };

        using var image = LensCorrectionProcessor.ImportCorrected(
            ToBytes(values), size, size, size, size, 1,
            CameraRgbCharacterization.Passthrough,
            prescription, settings, CancellationToken.None);
        using var pixels = image.GetPixelsUnsafe();
        var corrected = pixels.ToShortArray(ImageMagick.PixelMapping.RGB)!;
        var center = ((size / 2 * size) + size / 2) * 3;
        var ratio = corrected[0] / (double)corrected[center];

        Assert.InRange(ratio, 0.99, 1.01);
    }

    [Fact]
    public void LensfunVignettingRestoresGainAtPostDistortionSamples()
    {
        const int size = 401;
        const ushort target = 24000;
        var values = new ushort[size * size * 3];
        for (var y = 0; y < size; y++)
        for (var x = 0; x < size; x++)
        {
            var dx = (x + 0.5) / size - 0.5;
            var dy = (y + 0.5) / size - 0.5;
            // Corner-normalized pa radius: r=1 at the frame corner, so the
            // square frame's r-squared is 2*(dx^2+dy^2) in logical units.
            var radiusSquared = 2 * (dx * dx + dy * dy);
            var encoded = (ushort)Math.Round(
                target * (1 - 0.4 * radiusSquared));
            var offset = (y * size + x) * 3;
            values[offset] = values[offset + 1] = values[offset + 2] = encoded;
        }
        var prescription = new LensPrescription(
            LensPrescriptionSource.Lensfun,
            "Synthetic Lensfun Lens",
            [], [], LensFrameWindow.Full, LensFrameWindow.Full,
            LensfunDistortion: new LensfunDistortion(
                LensfunDistortionModel.Poly3, [0.4], 1, 0.5, 0.5),
            LensfunVignette: new LensfunVignette(
                -0.4, 0, 0, 1, 0.5, 0.5));
        var settings = BaseDecodeSettings.Default with
        {
            ChromaticAberration = false,
            Vignetting = true
        };

        using var image = LensCorrectionProcessor.ImportCorrected(
            ToBytes(values), size, size, size, size, 1,
            CameraRgbCharacterization.Passthrough,
            prescription, settings, CancellationToken.None);
        using var pixels = image.GetPixelsUnsafe();
        var corrected = pixels.ToShortArray(ImageMagick.PixelMapping.RGB)!;
        var maximumRelativeError = 0.0;
        for (var y = 50; y < size; y += 75)
        for (var x = 50; x < size; x += 75)
        {
            var actual = corrected[(y * size + x) * 3];
            maximumRelativeError = Math.Max(
                maximumRelativeError,
                Math.Abs(actual - target) / (double)target);
        }

        Assert.True(maximumRelativeError <= 0.005,
            $"Post-distortion gain residual was {maximumRelativeError:P2}.");
    }

    [Fact]
    public void IdentityWarpAndResizeSampleTheCameraPlanesDirectly()
    {
        const int sourceSize = 80;
        const int outputSize = 40;
        var source = GradientRgb(sourceSize, sourceSize);
        var identity = new LensWarpCoefficients(1, 0, 0, 0, 0, 0);
        var prescription = new LensPrescription(
            LensPrescriptionSource.DngOpcode,
            null,
            [new LensWarp([identity, identity, identity], 0.5, 0.5)],
            [], LensFrameWindow.Full, LensFrameWindow.Full);

        using var image = LensCorrectionProcessor.ImportCorrected(
            source, sourceSize, sourceSize, outputSize, outputSize, 1,
            CameraRgbCharacterization.Passthrough,
            prescription, BaseDecodeSettings.Default, CancellationToken.None);
        using var pixels = image.GetPixelsUnsafe();
        var values = pixels.ToShortArray(ImageMagick.PixelMapping.RGB)!;
        var middle = ((outputSize / 2 * outputSize) + outputSize / 2) * 3;

        Assert.Equal((uint)outputSize, image.Width);
        Assert.InRange(values[middle], 32200, 34400);
        Assert.Equal(values[middle], values[middle + 1]);
        Assert.Equal(values[middle], values[middle + 2]);
    }

    [Fact]
    public void OrientationSixRotatesUnswappedCameraBufferOnce()
    {
        const int sourceWidth = 80;
        const int sourceHeight = 60;
        var source = GradientRgb(sourceWidth, sourceHeight);
        var identity = new LensWarpCoefficients(1, 0, 0, 0, 0, 0);
        var prescription = new LensPrescription(
            LensPrescriptionSource.DngOpcode,
            null,
            [new LensWarp([identity], 0.5, 0.5)],
            [], LensFrameWindow.Full, LensFrameWindow.Full);
        var output = LensCorrectionProcessor.GetOutputSize(
            sourceWidth, sourceHeight, orientation: 6, maxDimension: null, prescription);

        using var image = LensCorrectionProcessor.ImportCorrected(
            source, sourceWidth, sourceHeight, output.Width, output.Height, 6,
            CameraRgbCharacterization.Passthrough,
            prescription, BaseDecodeSettings.Default, CancellationToken.None);
        using var pixels = image.GetPixelsUnsafe();
        var values = pixels.ToShortArray(ImageMagick.PixelMapping.RGB)!;
        var top = ((output.Width / 2) * 3);
        var bottom = (((output.Height - 1) * output.Width + output.Width / 2) * 3);

        Assert.Equal((60, 80), output);
        Assert.True(values[top] < 1000, $"Top value was {values[top]}.");
        Assert.True(values[bottom] > 64000, $"Bottom value was {values[bottom]}.");
    }

    [Fact]
    public void CoverScaleRejectsWarpThatCannotFitAtFourTimesZoom()
    {
        const int size = 101;
        var extreme = new LensWarpCoefficients(0, 0, 0, 0, 8, 8);
        var prescription = new LensPrescription(
            LensPrescriptionSource.DngOpcode,
            null,
            [new LensWarp([extreme], 0.5, 0.5)],
            [], LensFrameWindow.Full, LensFrameWindow.Full);

        Assert.Throws<InvalidOperationException>(() =>
            LensCorrectionProcessor.ImportCorrected(
                SolidRgb(size, size, 1, 2, 3), size, size, size, size, 1,
                CameraRgbCharacterization.Passthrough,
                prescription, BaseDecodeSettings.Default, CancellationToken.None));
    }

    [Fact]
    public void CoverScaleKeepsEveryMappedBorderCoordinateInBounds()
    {
        const int sourceWidth = 83;
        const int sourceHeight = 61;
        var planes = new[] { 0.15, 0.25, 0.35 }
            .Select(value => new LensWarpCoefficients(1, value, 0, 0, 0, 0))
            .ToArray();
        var prescription = new LensPrescription(
            LensPrescriptionSource.DngOpcode,
            null,
            [new LensWarp(planes, 0.5, 0.5)],
            [], LensFrameWindow.Full, LensFrameWindow.Full);
        var output = LensCorrectionProcessor.GetOutputSize(
            sourceWidth, sourceHeight, orientation: 6, maxDimension: null, prescription);
        var zoom = LensCorrectionProcessor.FindCoverScale(
            sourceWidth, sourceHeight, 6,
            prescription, BaseDecodeSettings.Default);
        var plan = new LensCorrectionPlan(
            sourceWidth, sourceHeight, output.Width, output.Height, 6,
            prescription, BaseDecodeSettings.Default, zoom);

        for (var x = 0; x < output.Width; x++)
        {
            AssertMapped(x, 0);
            AssertMapped(x, output.Height - 1);
        }
        for (var y = 1; y < output.Height - 1; y++)
        {
            AssertMapped(0, y);
            AssertMapped(output.Width - 1, y);
        }

        void AssertMapped(int x, int y)
        {
            var logical = plan.GetLogicalPoint(x, y);
            for (var channel = 0; channel < 3; channel++)
            {
                var point = plan.Map(logical, channel);
                Assert.InRange(point.X, 0, sourceWidth - 1);
                Assert.InRange(point.Y, 0, sourceHeight - 1);
            }
        }
    }

    [Fact]
    public void CorrectionUsesNativeFrameAtBothDecodeScales()
    {
        const int fullWidth = 401;
        const int fullHeight = 301;
        var reference = new LensCorrectionReferenceFrame(
            fullWidth, fullHeight, fullWidth, fullHeight);
        var dng = new LensPrescription(
            LensPrescriptionSource.DngOpcode,
            null,
            [new LensWarp(
                [new LensWarpCoefficients(1, 0.2, 0, 0, 0, 0)],
                0.5,
                0.5)],
            [], LensFrameWindow.Full, LensFrameWindow.Full);
        var maximum = Math.Sqrt(
            Math.Pow((fullWidth - 1) * 0.5, 2) +
            Math.Pow((fullHeight - 1) * 0.5, 2));
        var table = new LensRadialTable(
            maximum / (FujiNativePixelsPerTableRadiusUnit * 2),
            [0, 1], [0, -9],
            FujiNativePixelsPerTableRadiusUnit, 1.0 / 45);
        var raf = new LensPrescription(
            LensPrescriptionSource.FujifilmMakerNote,
            null, [], [], LensFrameWindow.Full, LensFrameWindow.Full,
            TableWarps: [new LensTableWarp(table, null)]);

        AssertInvariant(dng);
        AssertInvariant(raf);

        var anchorPlan = new LensCorrectionPlan(
            fullWidth, fullHeight, fullWidth, fullHeight,
            1, raf, BaseDecodeSettings.Default, zoom: 1, reference);
        var anchor = anchorPlan.MapShared(anchorPlan.GetLogicalPoint(300, 150));
        Assert.Equal(292.0199501246883, anchor.X, 9);
        Assert.Equal(150, anchor.Y, 12);

        void AssertInvariant(LensPrescription prescription)
        {
            var half = (Width: 201, Height: 151);
            var fullOutput = LensCorrectionProcessor.GetOutputSize(
                fullWidth, fullHeight, 1, 320, prescription, reference);
            var halfOutput = LensCorrectionProcessor.GetOutputSize(
                half.Width, half.Height, 1, 320, prescription, reference);
            var fullZoom = LensCorrectionProcessor.FindCoverScale(
                fullWidth, fullHeight, 1,
                prescription, BaseDecodeSettings.Default, reference);
            var halfZoom = LensCorrectionProcessor.FindCoverScale(
                half.Width, half.Height, 1,
                prescription, BaseDecodeSettings.Default, reference);
            Assert.Equal(fullZoom, halfZoom, 12);

            var fullPlan = new LensCorrectionPlan(
                fullWidth, fullHeight, fullOutput.Width, fullOutput.Height,
                1, prescription, BaseDecodeSettings.Default, fullZoom, reference);
            var halfPlan = new LensCorrectionPlan(
                half.Width, half.Height, halfOutput.Width, halfOutput.Height,
                1, prescription, BaseDecodeSettings.Default, halfZoom, reference);
            AssertSamePoint(-0.5, -0.5, -0.5, -0.5);
            AssertSamePoint(
                fullOutput.Width - 0.5, fullOutput.Height - 0.5,
                halfOutput.Width - 0.5, halfOutput.Height - 0.5);

            void AssertSamePoint(
                double fullX, double fullY, double halfX, double halfY)
            {
                var fullLogical = fullPlan.GetLogicalPoint(fullX, fullY);
                var halfLogical = halfPlan.GetLogicalPoint(halfX, halfY);
                Assert.Equal(fullLogical.X, halfLogical.X, 12);
                Assert.Equal(fullLogical.Y, halfLogical.Y, 12);
                var fullMapped = fullPlan.MapShared(fullLogical);
                var halfMapped = halfPlan.MapShared(halfLogical);
                Assert.Equal(
                    (fullMapped.X + 0.5) / fullWidth,
                    (halfMapped.X + 0.5) / half.Width,
                    12);
                Assert.Equal(
                    (fullMapped.Y + 0.5) / fullHeight,
                    (halfMapped.Y + 0.5) / half.Height,
                    12);
            }
        }
    }

}
