using HappyPhoton.Models;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class CurveDataTests
{
    private const double SlopeTolerance = 1e-6;

    private const double MinimumGap = 0.001;

    public static TheoryData<double[]> ReportedCurves => new()
    {
        new[] { 0, 0, 0.5, 0.6, 1, 1 },
        new[] { 0, 0, 0.15, 0.25, 1, 1 },
        new[] { 0, 0, 0.25, 0.2, 0.75, 0.8, 1, 1 },
        new[] { 0, 0, 0.25, 0.2, 0.35, 0.4, 0.75, 0.8, 1, 1 }
    };

    [Theory]
    [MemberData(nameof(ReportedCurves))]
    public void ReportedCurvesHaveNoCornerAtInteriorPoints(double[] coordinates)
    {
        AssertInteriorSlopesAgree(CreateCurve(coordinates));
    }

    [Fact]
    public void RandomUnevenCurvesHaveNoCornerAtInteriorPoints()
    {
        var random = new Random(20261008);

        for (var sample = 0; sample < 400; sample++)
        {
            var interior = random.Next(1, 6);
            var coordinates = new List<double> { 0, 0 };
            var x = 0.0;

            for (var index = 0; index < interior; index++)
            {
                // Gaps reach the 0.001 that dragging keeps between neighbours.
                var room = 1 - x - MinimumGap * (interior - index + 1);
                var gap = random.Next(4) == 0 ? MinimumGap : random.NextDouble() * room / (interior - index);
                x += MinimumGap + gap;
                coordinates.Add(x);
                coordinates.Add(0.05 + random.NextDouble() * 0.9);
            }

            coordinates.Add(1);
            coordinates.Add(1);
            AssertInteriorSlopesAgree(CreateCurve(coordinates.ToArray()));
        }
    }

    [Fact]
    public void EvenlySpacedCurvesKeepTheirTables()
    {
        var random = new Random(1379);

        for (var segments = 1; segments <= 12; segments++)
        {
            for (var sample = 0; sample < 50; sample++)
            {
                var coordinates = new List<double>();

                for (var index = 0; index <= segments; index++)
                {
                    coordinates.Add(index / (double)segments);
                    coordinates.Add((sample + index) % 2 == 0
                        ? random.Next(256) / 255.0
                        : random.NextDouble());
                }

                var curve = CreateCurve(coordinates.ToArray());

                Assert.Equal(OldTable(curve.Points), curve.LookupTable);
            }
        }
    }

    [Fact]
    public void EvenlySpacedThirdsKeepByteBoundaryTable()
    {
        var curve = CreateCurve([0, 0, 1.0 / 3, 1.0 / 255, 2.0 / 3, 2.0 / 255, 1, 1]);
        var expected = OldTable(curve.Points);

        Assert.Equal((byte)1, expected[170]);
        Assert.Equal(expected, curve.LookupTable);
    }

    [Fact]
    public void BuiltInLookCurvesKeepTheirTables()
    {
        var compared = 0;

        foreach (var look in new PresetService().BuiltInPresets)
        {
            var settings = look.Settings;

            foreach (var curve in new[] { settings.Curve, settings.CurveRed, settings.CurveGreen, settings.CurveBlue })
            {
                if (curve == null || curve.IsIdentity()) continue;

                Assert.Equal(OldTable(curve.Points), curve.Clone().LookupTable);
                compared++;
            }
        }

        Assert.Equal(48, compared);
    }

    private static CurveData CreateCurve(double[] coordinates)
    {
        var curve = new CurveData();
        curve.Points.Clear();

        for (var index = 0; index < coordinates.Length; index += 2)
        {
            curve.Points.Add(new CurvePoint(coordinates[index], coordinates[index + 1]));
        }

        curve.BuildLookupTable();

        return curve;
    }

    private static void AssertInteriorSlopesAgree(CurveData curve)
    {
        var points = curve.Points;

        for (var index = 1; index < points.Count - 1; index++)
        {
            var gap = Math.Min(points[index].X - points[index - 1].X, points[index + 1].X - points[index].X);
            var step = gap * 1e-3;
            var left = OneSidedSlope(curve, points[index].X, -step);
            var right = OneSidedSlope(curve, points[index].X, step);

            Assert.True(
                Math.Abs(left - right) <= SlopeTolerance,
                $"x = {points[index].X}: left slope {left}, right slope {right}");
        }
    }

    // Four-point one-sided difference, exact for a cubic, so only rounding remains.
    private static double OneSidedSlope(CurveData curve, double x, double step)
    {
        var f0 = curve.GetValueAt(x);
        var f1 = curve.GetValueAt(x + step);
        var f2 = curve.GetValueAt(x + 2 * step);
        var f3 = curve.GetValueAt(x + 3 * step);

        return (-11 * f0 + 18 * f1 - 9 * f2 + 2 * f3) / (6 * step);
    }

    private static byte[] OldTable(IReadOnlyList<CurvePoint> points)
    {
        var table = new byte[256];

        for (var index = 0; index < 256; index++)
        {
            table[index] = (byte)Math.Clamp((int)(OldValueAt(points, index / 255.0) * 255), 0, 255);
        }

        return table;
    }

    // The uniform-t Catmull-Rom evaluator CurveData used before tangents were measured in x.
    private static double OldValueAt(IReadOnlyList<CurvePoint> points, double x)
    {
        var segmentIndex = 0;

        for (var i = 0; i < points.Count - 1; i++)
        {
            if (x >= points[i].X && x <= points[i + 1].X)
            {
                segmentIndex = i;
                break;
            }

            if (i == points.Count - 2) segmentIndex = i;
        }

        var p1 = points[segmentIndex];
        var p2 = points[segmentIndex + 1];

        if (Math.Abs(p2.X - p1.X) < 0.0001) return p1.Y;

        var p0 = segmentIndex > 0 ? points[segmentIndex - 1] : new CurvePoint(p1.X - (p2.X - p1.X), p1.Y - (p2.Y - p1.Y));
        var p3 = segmentIndex < points.Count - 2 ? points[segmentIndex + 2] : new CurvePoint(p2.X + (p2.X - p1.X), p2.Y + (p2.Y - p1.Y));
        var t = (x - p1.X) / (p2.X - p1.X);
        var t2 = t * t;
        var t3 = t2 * t;
        var y = 0.5 * (
            (2 * p1.Y) +
            (-p0.Y + p2.Y) * t +
            (2 * p0.Y - 5 * p1.Y + 4 * p2.Y - p3.Y) * t2 +
            (-p0.Y + 3 * p1.Y - 3 * p2.Y + p3.Y) * t3
        );

        return Math.Clamp(y, 0, 1);
    }
}
