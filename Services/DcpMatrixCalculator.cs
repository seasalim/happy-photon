namespace HappyPhoton.Services;

internal sealed record DcpCharacterizationResult(
    double[,]? CameraToRec2020,
    DcpProfilePayload? Payload,
    DcpProfileErrorCode Status,
    string Token,
    string? Message)
{
    internal bool IsActive => CameraToRec2020 != null && Payload != null;
}

internal static class DcpMatrixCalculator
{
    private static readonly double[] D50 = [0.96422, 1.0, 0.82521];
    private static readonly double[] D65 = [0.95047, 1.0, 1.08883];

    internal static DcpCharacterizationResult Create(
        DcpProfileResolution resolution,
        DcpCameraData cameraData,
        RawCameraFactSnapshot facts,
        double asShotKelvin)
    {
        ArgumentNullException.ThrowIfNull(resolution);
        ArgumentNullException.ThrowIfNull(cameraData);
        ArgumentNullException.ThrowIfNull(facts);
        if (!resolution.IsActive || resolution.Profile == null)
        {
            return new DcpCharacterizationResult(
                null, null, resolution.Status, resolution.Token, resolution.Message);
        }
        if (facts.CamMul is not { Length: 3 } camMul ||
            camMul.Any(value => !double.IsFinite(value) || value <= 0))
        {
            return Reject(
                resolution,
                DcpProfileErrorCode.MissingWhiteBalance,
                "The profile cannot be applied because as-shot balancing facts are unavailable.");
        }

        try
        {
            var profile = resolution.Profile;
            var weight = GetInterpolationWeight(profile, asShotKelvin);
            var cc = GetCameraCalibration(cameraData, profile, weight);
            var ab = ChromaticAdaptation.CreateDiagonal(
                cameraData.AnalogBalance ?? [1.0, 1.0, 1.0]);
            var neutral = cameraData.AsShotNeutral ??
                NormalizeReciprocal(camMul);
            var balance = ChromaticAdaptation.CreateDiagonal(
                NormalizeToGreen(camMul));
            var balancedToRaw = Invert(balance);
            var abcc = ChromaticAdaptation.Multiply(ab, cc);
            double[,] rawToXyzD50;
            if (profile.ForwardMatrix1 != null)
            {
                var forward = Interpolate(
                    profile.ForwardMatrix1,
                    profile.ForwardMatrix2,
                    weight);
                var referenceNeutral = ChromaticAdaptation.Multiply(
                    Invert(abcc),
                    neutral);
                if (referenceNeutral.Any(value => value <= 0 || !double.IsFinite(value)))
                {
                    throw Unsupported("The profile produces an invalid reference neutral.");
                }
                var d = ChromaticAdaptation.CreateDiagonal(
                    referenceNeutral.Select(value => 1 / value).ToArray());
                rawToXyzD50 = ChromaticAdaptation.Multiply(
                    forward,
                    ChromaticAdaptation.Multiply(d, Invert(abcc)));
            }
            else
            {
                var color = Interpolate(
                    profile.ColorMatrix1,
                    profile.ColorMatrix2,
                    weight);
                var xyzToCamera = ChromaticAdaptation.Multiply(abcc, color);
                var cameraToXyz = Invert(xyzToCamera);
                var sourceWhite = ChromaticAdaptation.Multiply(
                    cameraToXyz,
                    neutral);
                NormalizeY(sourceWhite);
                var adaptation = ChromaticAdaptation.CreateBradfordMatrix(
                    sourceWhite,
                    D50);
                rawToXyzD50 = ChromaticAdaptation.Multiply(
                    adaptation,
                    cameraToXyz);
            }

            var d50ToD65 = ChromaticAdaptation.CreateBradfordMatrix(D50, D65);
            var d50ToWorking = ChromaticAdaptation.Multiply(
                RgbColorSpaceMatrices.XyzD65ToLinearRec2020DerivedExact,
                d50ToD65);
            NormalizeWhitePoint(d50ToWorking, D50);
            var cameraToRec2020 = ChromaticAdaptation.Multiply(
                d50ToWorking,
                ChromaticAdaptation.Multiply(rawToXyzD50, balancedToRaw));
            var map = profile.HueSatTable1 == null
                ? null
                : new DcpHueSatMap(
                    profile.HueDivisions,
                    profile.SaturationDivisions,
                    profile.ValueDivisions,
                    profile.EncodeValueAsSrgb,
                    profile.HueSatTable1,
                    profile.HueSatTable2,
                    weight);
            if (map != null)
            {
                map = map with
                {
                    RgbLut = profile.GetOrCreateRgbLut(
                        weight,
                        () => DcpHueSatRenderer.BuildRgbLut(map))
                };
            }
            return new DcpCharacterizationResult(
                cameraToRec2020,
                new DcpProfilePayload(resolution.Token, profile.Name, map),
                DcpProfileErrorCode.None,
                resolution.Token,
                null);
        }
        catch (DcpProfileException exception)
        {
            return Reject(resolution, exception.Code, exception.Message);
        }
        catch (ArgumentException exception)
        {
            return Reject(
                resolution,
                DcpProfileErrorCode.UnsupportedVariant,
                exception.Message);
        }
    }

    internal static double GetInterpolationWeight(
        DcpProfile profile,
        double asShotKelvin)
    {
        if (!profile.CalibrationIlluminant2.HasValue)
        {
            return 0;
        }
        if (!double.IsFinite(asShotKelvin) || asShotKelvin <= 0)
        {
            throw Unsupported("The as-shot white point is invalid.");
        }
        var first = GetIlluminantTemperature(profile.CalibrationIlluminant1);
        var second = GetIlluminantTemperature(
            profile.CalibrationIlluminant2.Value);
        var denominator = 1 / second - 1 / first;
        if (Math.Abs(denominator) < 1e-12)
        {
            throw Unsupported("The two calibration illuminants are indistinguishable.");
        }
        return Math.Clamp(
            (1 / asShotKelvin - 1 / first) / denominator,
            0,
            1);
    }

    internal static double GetIlluminantTemperature(int illuminant) =>
        illuminant switch
        {
            1 or 4 or 9 or 20 => 5500,
            2 => 4000,
            3 or 17 => 2850,
            10 or 21 => 6500,
            11 or 22 => 7500,
            12 => 5700,
            13 or 23 => 5000,
            14 => 4150,
            15 => 3450,
            16 => 3000,
            18 => 4874,
            19 => 6774,
            24 => 3200,
            _ => throw new DcpProfileException(
                DcpProfileErrorCode.UnknownIlluminant,
                $"Calibration illuminant {illuminant} is not supported.")
        };

    private static double[,] GetCameraCalibration(
        DcpCameraData cameraData,
        DcpProfile profile,
        double weight)
    {
        var first = cameraData.CameraCalibration1;
        var second = cameraData.CameraCalibration2;
        if (first == null && second == null)
        {
            return ChromaticAdaptation.Identity();
        }
        if (!string.Equals(
            cameraData.CalibrationSignature,
            profile.CalibrationSignature,
            StringComparison.Ordinal))
        {
            throw new DcpProfileException(
                DcpProfileErrorCode.SignatureMismatch,
                "Camera and profile calibration signatures do not match.");
        }
        return Interpolate(
            first ?? ChromaticAdaptation.Identity(),
            profile.CalibrationIlluminant2.HasValue
                ? second ?? ChromaticAdaptation.Identity()
                : null,
            weight);
    }

    private static double[,] Interpolate(
        double[,] first,
        double[,]? second,
        double weight)
    {
        if (second == null) return (double[,])first.Clone();
        var result = new double[3, 3];
        for (var row = 0; row < 3; row++)
        for (var column = 0; column < 3; column++)
        {
            result[row, column] = first[row, column] * (1 - weight) +
                second[row, column] * weight;
        }
        return result;
    }

    private static double[] NormalizeReciprocal(IReadOnlyList<double> values)
    {
        var result = values.Select(value => 1 / value).ToArray();
        var scale = result[1];
        for (var index = 0; index < result.Length; index++) result[index] /= scale;
        return result;
    }

    private static double[] NormalizeToGreen(IReadOnlyList<double> values)
    {
        var scale = values[1];
        return values.Select(value => value / scale).ToArray();
    }

    private static void NormalizeY(double[] xyz)
    {
        if (xyz.Length != 3 || xyz.Any(value => !double.IsFinite(value)) ||
            xyz[1] <= 1e-12)
        {
            throw Unsupported("The as-shot camera neutral has no valid white point.");
        }
        var y = xyz[1];
        for (var index = 0; index < xyz.Length; index++) xyz[index] /= y;
    }

    private static void NormalizeWhitePoint(double[,] matrix, double[] white)
    {
        var mapped = ChromaticAdaptation.Multiply(matrix, white);
        if (mapped.Any(value => !double.IsFinite(value) || value <= 1e-12))
        {
            throw Unsupported("The profile white point cannot be normalized.");
        }
        for (var row = 0; row < 3; row++)
        for (var column = 0; column < 3; column++)
        {
            matrix[row, column] /= mapped[row];
        }
    }

    internal static double[,] Invert(double[,] value)
    {
        if (value.GetLength(0) != 3 || value.GetLength(1) != 3)
        {
            throw Unsupported("Only three-channel DCP matrices are supported.");
        }
        var a = value[0, 0]; var b = value[0, 1]; var c = value[0, 2];
        var d = value[1, 0]; var e = value[1, 1]; var f = value[1, 2];
        var g = value[2, 0]; var h = value[2, 1]; var i = value[2, 2];
        var determinant = a * (e * i - f * h) - b * (d * i - f * g) +
            c * (d * h - e * g);
        if (!double.IsFinite(determinant) || Math.Abs(determinant) < 1e-12)
        {
            throw Unsupported("A DCP matrix is singular.");
        }
        var scale = 1 / determinant;
        var inverse = new[,]
        {
            { (e * i - f * h) * scale, (c * h - b * i) * scale, (b * f - c * e) * scale },
            { (f * g - d * i) * scale, (a * i - c * g) * scale, (c * d - a * f) * scale },
            { (d * h - e * g) * scale, (b * g - a * h) * scale, (a * e - b * d) * scale }
        };
        if (inverse.Cast<double>().Any(item => !double.IsFinite(item)))
        {
            throw Unsupported("A DCP matrix inverse is not finite.");
        }
        return inverse;
    }

    private static DcpCharacterizationResult Reject(
        DcpProfileResolution resolution,
        DcpProfileErrorCode status,
        string message) => new(
            null,
            null,
            status,
            $"{resolution.Token}:{status.ToString().ToLowerInvariant()}",
            message);

    private static DcpProfileException Unsupported(string message) => new(
        DcpProfileErrorCode.UnsupportedVariant,
        message);
}
