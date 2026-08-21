using System.Runtime.CompilerServices;

namespace HappyPhoton.Services;

internal static partial class OklabColor
{
    private static OklabRgb ProjectCartesianToRec2020Gamut(
        double lightness,
        double a,
        double b)
    {
        var ray = OklabRay.Create(lightness, a, b);
        var candidate = ray.Evaluate(1);
        if (IsInGamut(candidate))
        {
            return candidate;
        }
        if (ProjectRay(ray, candidate, maximum: 1) is { } projection)
        {
            return projection.Rgb;
        }

        var chroma = Math.Sqrt(a * a + b * b);
        var hue = Math.Atan2(b, a);
        if (hue < 0) hue += Math.Tau;
        return ProjectByBisection(
            new Oklch(lightness, chroma, hue)).LinearRec2020;
    }

    internal static OklabGamutResult ProjectToRec2020Gamut(Oklch color)
    {
        var candidate = ToLinearRec2020(color);
        if (IsInGamut(candidate))
        {
            return new OklabGamutResult(candidate, color, false);
        }

        var ray = OklabRay.Create(color);
        var projection = ProjectRay(ray, candidate, maximum: 1);
        if (projection == null)
        {
            return ProjectByBisection(color);
        }
        return new OklabGamutResult(
            projection.Value.Rgb,
            color with
            {
                Chroma = color.Chroma * projection.Value.Position
            },
            true);
    }

    private static RayProjection? ProjectRay(
        OklabRay ray,
        OklabRgb candidate,
        double maximum)
    {
        Span<double> boundaries = stackalloc double[6];
        var count = 0;
        TryAddBoundary(boundaries, ref count,
            ray.Red, candidate.Red, maximum, upper: false);
        TryAddBoundary(boundaries, ref count,
            ray.Green, candidate.Green, maximum, upper: false);
        TryAddBoundary(boundaries, ref count,
            ray.Blue, candidate.Blue, maximum, upper: false);
        TryAddBoundary(boundaries, ref count,
            ray.Red, candidate.Red, maximum, upper: true);
        TryAddBoundary(boundaries, ref count,
            ray.Green, candidate.Green, maximum, upper: true);
        TryAddBoundary(boundaries, ref count,
            ray.Blue, candidate.Blue, maximum, upper: true);
        if (count == 0)
        {
            return null;
        }

        var position = boundaries[0];
        for (var index = 1; index < count; index++)
        {
            position = Math.Min(position, boundaries[index]);
        }
        var projected = ray.Evaluate(position);
        var retreat = Math.Max(1e-12, maximum * 1e-12);
        for (var attempt = 0; attempt < 4 && !IsInGamut(projected); attempt++)
        {
            position = Math.Max(0, position - retreat);
            retreat *= 4;
            projected = ray.Evaluate(position);
        }
        return IsInGamut(projected)
            ? new RayProjection(projected, position)
            : null;
    }

    private static void TryAddBoundary(
        Span<double> boundaries,
        ref int count,
        Cubic polynomial,
        double target,
        double maximum,
        bool upper)
    {
        if (upper ? target <= 1 : target >= 0)
        {
            return;
        }
        var boundary = upper ? polynomial.OneMinus() : polynomial;
        if (boundary.TryFindBoundary(maximum, out var root))
        {
            boundaries[count++] = root;
        }
    }

    private static OklabGamutResult ProjectByBisection(Oklch color)
    {
        var low = 0.0;
        var high = color.Chroma;
        var projected = ToLinearRec2020(color with { Chroma = 0 });
        for (var iteration = 0;
            iteration < ProjectionFallbackIterations;
            iteration++)
        {
            var middle = (low + high) * 0.5;
            var middleRgb = ToLinearRec2020(color with { Chroma = middle });
            if (IsInGamut(middleRgb))
            {
                low = middle;
                projected = middleRgb;
            }
            else
            {
                high = middle;
            }
        }
        return new OklabGamutResult(
            projected,
            color with { Chroma = low },
            true);
    }

    private readonly record struct RayProjection(
        OklabRgb Rgb,
        double Position);

    private readonly record struct OklabRay(
        Cubic Red,
        Cubic Green,
        Cubic Blue)
    {
        internal static OklabRay Create(Oklch color) => Create(
            color.Lightness,
            color.Chroma * Math.Cos(color.HueRadians),
            color.Chroma * Math.Sin(color.HueRadians));

        internal static OklabRay Create(
            double lightness,
            double a,
            double b)
        {
            var l = Cubic.ExpandCube(
                Llightness * lightness,
                La * a + LbInverse * b);
            var m = Cubic.ExpandCube(
                Mlightness * lightness,
                Ma * a + MbInverse * b);
            var s = Cubic.ExpandCube(
                Slightness * lightness,
                Sa * a + SbInverse * b);
            return new OklabRay(
                Cubic.Combine(Lr, l, Mr, m, Sr, s),
                Cubic.Combine(Lg, l, Mg, m, Sg, s),
                Cubic.Combine(Lb, l, Mb, m, Sb, s));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal OklabRgb Evaluate(double position) => new(
            Red.Evaluate(position),
            Green.Evaluate(position),
            Blue.Evaluate(position));
    }

    private readonly record struct Cubic(
        double C0,
        double C1,
        double C2,
        double C3)
    {
        internal static Cubic ExpandCube(double origin, double slope) => new(
            origin * origin * origin,
            3 * origin * origin * slope,
            3 * origin * slope * slope,
            slope * slope * slope);

        internal static Cubic Combine(
            double firstScale,
            Cubic first,
            double secondScale,
            Cubic second,
            double thirdScale,
            Cubic third) => new(
            firstScale * first.C0 + secondScale * second.C0 +
                thirdScale * third.C0,
            firstScale * first.C1 + secondScale * second.C1 +
                thirdScale * third.C1,
            firstScale * first.C2 + secondScale * second.C2 +
                thirdScale * third.C2,
            firstScale * first.C3 + secondScale * second.C3 +
                thirdScale * third.C3);

        internal Cubic OneMinus() => new(1 - C0, -C1, -C2, -C3);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal double Evaluate(double value) =>
            ((C3 * value + C2) * value + C1) * value + C0;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private double Derivative(double value) =>
            (3 * C3 * value + 2 * C2) * value + C1;

        internal bool TryFindBoundary(double maximum, out double root)
        {
            var low = 0.0;
            var high = maximum;
            var lowValue = Evaluate(low);
            var highValue = Evaluate(high);
            if (lowValue < 0 || highValue >= 0)
            {
                root = 0;
                return false;
            }
            for (var iteration = 0; iteration < 2; iteration++)
            {
                var middle = (low + high) * 0.5;
                if (Evaluate(middle) >= 0) low = middle;
                else high = middle;
            }

            var value = (low + high) * 0.5;
            for (var iteration = 0; iteration < 5; iteration++)
            {
                var evaluated = Evaluate(value);
                if (evaluated >= 0) low = value;
                else high = value;
                var derivative = Derivative(value);
                var next = derivative == 0
                    ? (low + high) * 0.5
                    : value - evaluated / derivative;
                value = next > low && next < high
                    ? next
                    : (low + high) * 0.5;
            }

            var finalValue = Evaluate(value);
            if (finalValue >= 0)
            {
                low = value;
                lowValue = finalValue;
                highValue = Evaluate(high);
            }
            else
            {
                high = value;
                highValue = finalValue;
                lowValue = Evaluate(low);
            }
            root = low + (high - low) *
                lowValue / (lowValue - highValue);
            return double.IsFinite(root) && root is >= 0 && root <= maximum;
        }
    }
}
