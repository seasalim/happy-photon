namespace HappyPhoton.Services;

public static class WhiteBalanceModel
{
    public const double MinimumKelvin = 2000;
    public const double MaximumKelvin = 12000;
    public const double MinimumTint = -100;
    public const double MaximumTint = 100;

    private const double TintUvStep = 0.00025;
    private const double RawFallbackKelvin = 5500;

    public static double[,] CreateMatrix(
        double kelvin,
        double tint,
        double asShotKelvin,
        double asShotTint)
    {
        ValidateFinite(kelvin, nameof(kelvin));
        ValidateFinite(tint, nameof(tint));
        ValidateFinite(asShotKelvin, nameof(asShotKelvin));
        ValidateFinite(asShotTint, nameof(asShotTint));
        if (kelvin == asShotKelvin && tint == asShotTint)
        {
            return ChromaticAdaptation.Identity();
        }

        var sourceWhite = GetWhitePointXyz(kelvin, tint);
        var destinationWhite = GetWhitePointXyz(
            asShotKelvin,
            asShotTint);
        return ChromaticAdaptation.CreateLinearSrgbMatrix(
            sourceWhite,
            destinationWhite);
    }

    public static double[,] CreateGainMatrix(double[] gains)
    {
        ValidatePositiveTriple(gains, nameof(gains));
        return ChromaticAdaptation.CreateDiagonal(gains);
    }

    public static double[] GetWhitePointXyz(double kelvin, double tint)
    {
        var (u, v) = GetWhitePointUv(kelvin, tint);
        var denominator = 2 * u - 8 * v + 4;
        var x = 3 * u / denominator;
        var y = 2 * v / denominator;
        return [x / y, 1, (1 - x - y) / y];
    }

    public static (double U, double V) GetWhitePointUv(
        double kelvin,
        double tint)
    {
        ValidateFinite(kelvin, nameof(kelvin));
        ValidateFinite(tint, nameof(tint));
        var (x, y) = GetLocusXy(Math.Clamp(
            kelvin,
            MinimumKelvin,
            MaximumKelvin));
        var denominator = -2 * x + 12 * y + 3;
        var u = 4 * x / denominator;
        var v = 6 * y / denominator;
        return (
            u,
            v + Math.Clamp(tint, MinimumTint, MaximumTint) * TintUvStep);
    }

    public static (double kelvin, double tint) EstimateKelvinTintFromUv(
        double u,
        double v)
    {
        ValidateFinite(u, nameof(u));
        ValidateFinite(v, nameof(v));
        var minimumUv = GetWhitePointUv(MinimumKelvin, 0);
        var maximumUv = GetWhitePointUv(MaximumKelvin, 0);
        double kelvin;
        if (u >= minimumUv.U)
        {
            kelvin = MinimumKelvin;
        }
        else if (u <= maximumUv.U)
        {
            kelvin = MaximumKelvin;
        }
        else
        {
            var lower = MinimumKelvin;
            var upper = MaximumKelvin;
            for (var iteration = 0; iteration < 60; iteration++)
            {
                var middle = (lower + upper) / 2;
                if (GetWhitePointUv(middle, 0).U > u)
                {
                    lower = middle;
                }
                else
                {
                    upper = middle;
                }
            }

            kelvin = (lower + upper) / 2;
        }

        var locusV = GetWhitePointUv(kelvin, 0).V;
        var tint = Math.Clamp(
            (v - locusV) / TintUvStep,
            MinimumTint,
            MaximumTint);
        return (kelvin, tint);
    }

    public static (double kelvin, double tint) EstimateAsShot(
        double[]? camMul,
        double[,]? camToSrgb,
        double[]? preMul)
    {
        if (!IsPositiveCameraVector(camMul) ||
            !IsPositiveCameraVector(preMul) ||
            preMul!.Length != camMul!.Length ||
            !IsMatchingCameraMatrix(camToSrgb, camMul.Length))
        {
            return (RawFallbackKelvin, 0);
        }

        var cameraNeutral = Normalize(Enumerable.Range(0, camMul.Length)
            .Select(channel => preMul[channel] / camMul[channel])
            .ToArray());
        var srgbNeutral = ProjectCameraNeutral(camToSrgb!, cameraNeutral);
        return EstimateFromLinearSrgbWhite(srgbNeutral);
    }

    public static (double kelvin, double tint) EstimateFromGains(
        double[] gains)
    {
        ValidatePositiveTriple(gains, nameof(gains));
        var white = Normalize(
            [1 / gains[0], 1 / gains[1], 1 / gains[2]]);
        return EstimateFromLinearSrgbWhite(white);
    }

    private static (double kelvin, double tint) EstimateFromLinearSrgbWhite(
        double[] white)
    {
        var xyz = ChromaticAdaptation.LinearSrgbToXyz(white);
        var denominator = xyz[0] + 15 * xyz[1] + 3 * xyz[2];
        if (!double.IsFinite(denominator) || Math.Abs(denominator) < 1e-12)
        {
            return (RawFallbackKelvin, 0);
        }

        var u = 4 * xyz[0] / denominator;
        var v = 6 * xyz[1] / denominator;
        if (!double.IsFinite(u) || !double.IsFinite(v))
        {
            return (RawFallbackKelvin, 0);
        }

        return EstimateKelvinTintFromUv(u, v);
    }

    private static (double X, double Y) GetLocusXy(double kelvin)
    {
        if (kelvin <= 4000)
        {
            return GetPlanckianXy(kelvin);
        }

        if (kelvin >= 4500)
        {
            return GetDaylightXy(kelvin);
        }

        var planckian = GetPlanckianXy(kelvin);
        var daylight = GetDaylightXy(kelvin);
        var position = (kelvin - 4000) / 500;
        var weight = position * position * (3 - 2 * position);
        return (
            planckian.X + (daylight.X - planckian.X) * weight,
            planckian.Y + (daylight.Y - planckian.Y) * weight);
    }

    private static (double X, double Y) GetPlanckianXy(double kelvin)
    {
        var squared = kelvin * kelvin;
        var u = (0.860117757 + 1.54118254e-4 * kelvin +
                 1.28641212e-7 * squared) /
                (1 + 8.42420235e-4 * kelvin +
                 7.08145163e-7 * squared);
        var v = (0.317398726 + 4.22806245e-5 * kelvin +
                 4.20481691e-8 * squared) /
                (1 - 2.89741816e-5 * kelvin +
                 1.61456053e-7 * squared);
        var denominator = 2 * u - 8 * v + 4;
        return (3 * u / denominator, 2 * v / denominator);
    }

    private static (double X, double Y) GetDaylightXy(double kelvin)
    {
        var squared = kelvin * kelvin;
        var cubed = squared * kelvin;
        var x = kelvin <= 7000
            ? -4.6070e9 / cubed + 2.9678e6 / squared +
              0.09911e3 / kelvin + 0.244063
            : -2.0064e9 / cubed + 1.9018e6 / squared +
              0.24748e3 / kelvin + 0.237040;
        return (x, -3 * x * x + 2.870 * x - 0.275);
    }

    private static double[] Normalize(double[] values)
    {
        var sum = values.Sum();
        if (!double.IsFinite(sum) || Math.Abs(sum) < 1e-12)
        {
            return [1.0 / 3, 1.0 / 3, 1.0 / 3];
        }

        return values.Select(value => value / sum).ToArray();
    }

    private static void ValidatePositiveTriple(double[] values, string name)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (!IsPositiveTriple(values))
        {
            throw new ArgumentException(
                "Expected three finite positive values.",
                name);
        }
    }

    private static bool IsPositiveTriple(double[]? values) =>
        values is { Length: 3 } &&
        values.All(value => double.IsFinite(value) && value > 0);

    private static bool IsPositiveCameraVector(double[]? values) =>
        values is { Length: 3 or 4 } &&
        values.All(value => double.IsFinite(value) && value > 0);

    private static bool IsMatchingCameraMatrix(
        double[,]? matrix,
        int channelCount)
    {
        if (matrix == null ||
            matrix.GetLength(0) != 3 ||
            matrix.GetLength(1) != channelCount)
        {
            return false;
        }

        foreach (var value in matrix)
        {
            if (!double.IsFinite(value))
            {
                return false;
            }
        }

        return true;
    }

    private static double[] ProjectCameraNeutral(
        double[,] cameraToSrgb,
        double[] cameraNeutral)
    {
        var result = new double[3];
        for (var row = 0; row < 3; row++)
        {
            for (var column = 0; column < cameraNeutral.Length; column++)
            {
                result[row] += cameraToSrgb[row, column] * cameraNeutral[column];
            }
        }

        return result;
    }

    private static void ValidateFinite(double value, string name)
    {
        if (!double.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(
                name,
                "Expected a finite value.");
        }
    }
}
