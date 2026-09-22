using System.Reflection;
using HappyPhoton.Models;
using HappyPhoton.Services;
using Xunit;
using static HappyPhoton.Tests.LocalsRangeOracle;

namespace HappyPhoton.Tests;

public sealed partial class LocalsContractPrototypeTests
{
    [Theory]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    [InlineData(true, false, true)]
    [InlineData(false, false, false)]
    public void RangeOracle_ProductionLocalsInputRecoversDefinitionalBasis(bool raw, bool dcp, bool monochrome)
    {
        var random = new Random(261);
        var values = Enumerable.Range(0, 1024 * 3).Select(_ => (ushort)random.Next(65536)).ToArray();
        if (monochrome)
            for (var i = 0; i < values.Length; i += 3) values[i + 1] = values[i + 2] = values[i];
        using var basis = RenderPipelineTestSupport.CreateBase(values, raw, isMonochrome: monochrome);
        foreach (var mode in new[] { WbMode.AsShot, WbMode.Custom, WbMode.Picked })
        {
            var settings = new EditSettings
            {
                Wb = new() { Mode = mode, Kelvin = 8500, Tint = 30, Gains = [1.8, 1, .6] },
                Locals = [new() { Exposure = 2, Temperature = 50, Cu = 2 }]
            };
            var neutralColor = new EditSettings
            {
                Wb = settings.Wb,
                Locals = [new() { Exposure = 2, Cu = 2 }]
            };
            using var image = RenderGeometry.Apply(basis.Pixels, settings, out var trace);
            var map = dcp ? HueSatMap() : null;
            DcpHueSatRenderer.Apply(image, map);
            var source = RenderPipelineTestSupport.ReadPixels(image);
            if (dcp) Assert.NotEqual(values, source);
            var wb = RenderChromaticStage.CreateWhiteBalanceMatrix(basis.Info, settings);
            var locals = RenderLocals.Create(settings, trace, 1024, 1, info: basis.Info)!;
            var scalarLocals = RenderLocals.Create(neutralColor, trace, 1024, 1, info: basis.Info)!;
            Assert.NotNull(locals); Assert.NotNull(scalarLocals);
            Assert.Equal(!monochrome, locals.HasColor); Assert.False(scalarLocals.HasColor);
            var crossing = new AgxCrossing(Raw(), wb, map, locals: locals);
            var scalarCrossing = new AgxCrossing(Raw(), wb, map, locals: scalarLocals);
            var normalized = RenderChromaticStage.CreateNormalizedMatrix(basis.Info, settings);
            // Classification must prepare WB/Fold even when the scalar kernel has no
            // _localWhiteBalance. Its _input already includes the forbidden AgX inset.
            var rawMatrix = RangeWhiteBalance(wb, crossing.Fold);
            var scalarMatrix = RangeWhiteBalance(wb, scalarCrossing.Fold);
            if (raw)
            {
                Assert.Equal(crossing.Fold, scalarCrossing.Fold);
                if (locals.HasColor)
                    Assert.Equal(rawMatrix, RangeCrossingMatrix(crossing, "_localWhiteBalance"));
                else
                    Assert.Equal(default, RangeCrossingMatrix(crossing, "_localWhiteBalance"));
                Assert.Equal(default, RangeCrossingMatrix(scalarCrossing, "_localWhiteBalance"));
            }
            var standardTransform = typeof(ToneLutApplicator)
                .GetMethod("Transform", BindingFlags.Static | BindingFlags.NonPublic)!
                .CreateDelegate<Func<double[,], int, double, double, double, double>>();
            double maxL = 0, maxC = 0, maxH = 0;
            for (var i = 0; i < source.Length; i += 3)
            {
                var v = new Rgb(source[i] / 65535d, source[i + 1] / 65535d, source[i + 2] / 65535d);
                var expectedRgb = Matrix(wb, v);
                var expected = Classify(expectedRgb.R, expectedRgb.G, expectedRgb.B);
                // RAW uses the pinned WB/Fold basis; standard invokes its production Transform.
                var stage = raw ? RangeTransform(rawMatrix, v) :
                    new Rgb(standardTransform(normalized.Matrix, 0, v.R, v.G, v.B),
                        standardTransform(normalized.Matrix, 1, v.R, v.G, v.B),
                        standardTransform(normalized.Matrix, 2, v.R, v.G, v.B));
                var restored = stage * (raw ? crossing.Fold : normalized.Fold);
                var actual = Classify(restored.R, restored.G, restored.B);
                if (raw)
                {
                    var scalarRgb = RangeTransform(scalarMatrix, v) * scalarCrossing.Fold;
                    Assert.Equal(actual, Classify(scalarRgb.R, scalarRgb.G, scalarRgb.B));
                }
                maxL = Math.Max(maxL, Math.Abs(expected.L - actual.L));
                maxC = Math.Max(maxC, Math.Abs(expected.C - actual.C));
                if (expected.C > 1e-6) maxH = Math.Max(maxH, Distance(expected.Hue, actual.Hue) * Math.PI / 180);
            }
            Assert.InRange(maxL, 0, 1e-9); Assert.InRange(maxC, 0, 1e-9); Assert.InRange(maxH, 0, 1e-7);
            output.WriteLine($"Range basis RAW={raw} DCP={dcp} mono={monochrome} WB={mode}: " +
                $"max L={maxL:R} C={maxC:R} hue_rad={maxH:R}");
            if (raw)
            {
                // Synthetic chromatic probe of both matrices, including monochrome setup:
                // real monochrome pixels above are equal-channel, which the inset preserves
                // and therefore cannot distinguish the forbidden source from WB/Fold.
                var probe = new Rgb(.8, .2, .05);
                var expectedRgb = Matrix(wb, probe);
                var expected = Classify(expectedRgb.R, expectedRgb.G, expectedRgb.B);
                foreach (var candidate in new[] { crossing, scalarCrossing })
                {
                    var wrongRgb = RangeTransform(RangeCrossingMatrix(candidate, "_input"), probe) * candidate.Fold;
                    var wrong = Classify(wrongRgb.R, wrongRgb.G, wrongRgb.B);
                    var deltaL = Math.Abs(expected.L - wrong.L);
                    var deltaC = Math.Abs(expected.C - wrong.C);
                    var deltaH = Distance(expected.Hue, wrong.Hue) * Math.PI / 180;
                    Assert.True(deltaL > 1e-9 || deltaC > 1e-9 || expected.C > 1e-6 && deltaH > 1e-7,
                        "The inset-containing _input must fail the definitional basis bound.");
                    output.WriteLine($"Forbidden inset probe: L={deltaL:R} C={deltaC:R} hue_rad={deltaH:R}");
                }
            }
        }
        Assert.Equal(values, RenderPipelineTestSupport.ReadPixels(basis.Pixels));
    }

    private static AgxCrossing.Matrix3x3 RangeWhiteBalance(double[,] wb, double fold)
    {
        var matrix = (double[,])wb.Clone();
        for (var row = 0; row < 3; row++) for (var col = 0; col < 3; col++) matrix[row, col] /= fold;
        return new(matrix);
    }

    private static AgxCrossing.Matrix3x3 RangeCrossingMatrix(AgxCrossing crossing, string field) =>
        (AgxCrossing.Matrix3x3)typeof(AgxCrossing)
            .GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(crossing)!;

    private static Rgb RangeTransform(AgxCrossing.Matrix3x3 matrix, Rgb v) =>
        new(matrix.Row0(v.R, v.G, v.B), matrix.Row1(v.R, v.G, v.B), matrix.Row2(v.R, v.G, v.B));
}
