using HappyPhoton.LibRaw.Interop;

namespace HappyPhoton.Services;

internal sealed record RawCameraFactSnapshot(
    double[]? CamMul,
    double[,]? CamToSrgb,
    double[]? PreMul,
    double[,]? CameraFromXyz,
    bool IsIdentityCameraTransform)
{
    internal static RawCameraFactSnapshot Empty { get; } =
        new(null, null, null, null, false);

    internal static RawCameraFactSnapshot Copy(LibRawCameraFacts? facts)
    {
        if (facts == null) return Empty;
        var multipliers = facts.Multipliers;
        var matrix = facts.CameraToSrgb;
        var availableColumns = Math.Min(
            4,
            Math.Min(multipliers.Length, matrix.GetLength(1)));
        if (matrix.GetLength(0) < 3)
        {
            return Empty;
        }

        var channelCount = HasUsableChannel(multipliers, matrix, 3)
            ? 4
            : Math.Min(3, availableColumns);
        if (channelCount < 3)
        {
            return Empty;
        }

        var camMul = new double[channelCount];
        var camToSrgb = new double[3, channelCount];
        for (var channel = 0; channel < channelCount; channel++)
        {
            var multiplier = multipliers[channel];
            if (!float.IsFinite(multiplier) || multiplier <= 0)
            {
                return Empty;
            }

            camMul[channel] = multiplier;
            for (var row = 0; row < 3; row++)
            {
                var value = matrix[row, channel];
                if (!float.IsFinite(value))
                {
                    return Empty;
                }

                camToSrgb[row, channel] = value;
            }
        }

        var preMul = CopyVector(facts.PreMultipliers, channelCount, positive: true);
        var cameraFromXyz = CopyMatrix(facts.CameraFromXyz, channelCount, 3);
        var isIdentity = IsIdentityTransform(camToSrgb);
        return new RawCameraFactSnapshot(
            camMul,
            isIdentity ? null : camToSrgb,
            preMul,
            cameraFromXyz,
            isIdentity);
    }

    internal static bool IsIdentityTransform(double[,] matrix)
    {
        ArgumentNullException.ThrowIfNull(matrix);
        if (matrix.GetLength(0) != 3 ||
            matrix.GetLength(1) is not (3 or 4))
        {
            return false;
        }

        for (var row = 0; row < 3; row++)
        for (var column = 0; column < matrix.GetLength(1); column++)
        {
            var expected = column < 3 && row == column ? 1.0 : 0.0;
            if (Math.Abs(matrix[row, column] - expected) > 1e-6)
            {
                return false;
            }
        }

        return true;
    }

    private static double[]? CopyVector(
        IReadOnlyList<float>? values,
        int count,
        bool positive)
    {
        if (values == null || values.Count != count)
        {
            return null;
        }

        var result = new double[count];
        for (var index = 0; index < count; index++)
        {
            var value = values[index];
            if (!float.IsFinite(value) || positive && value <= 0)
            {
                return null;
            }

            result[index] = value;
        }

        return result;
    }

    private static double[,]? CopyMatrix(
        float[,]? values,
        int rows,
        int columns)
    {
        if (values == null || values.GetLength(0) != rows ||
            values.GetLength(1) != columns)
        {
            return null;
        }

        var result = new double[rows, columns];
        for (var row = 0; row < rows; row++)
        for (var column = 0; column < columns; column++)
        {
            var value = values[row, column];
            if (!float.IsFinite(value))
            {
                return null;
            }

            result[row, column] = value;
        }

        return result;
    }

    private static bool HasUsableChannel(
        IReadOnlyList<float> multipliers,
        float[,] matrix,
        int channel)
    {
        if (multipliers.Count <= channel ||
            matrix.GetLength(1) <= channel ||
            !float.IsFinite(multipliers[channel]) ||
            multipliers[channel] <= 0)
        {
            return false;
        }

        for (var row = 0; row < Math.Min(3, matrix.GetLength(0)); row++)
        {
            if (float.IsFinite(matrix[row, channel]) &&
                Math.Abs(matrix[row, channel]) > float.Epsilon)
            {
                return true;
            }
        }

        return false;
    }
}
