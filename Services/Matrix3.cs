namespace HappyPhoton.Services;

internal readonly record struct Matrix3(
    double M11, double M12, double M13,
    double M21, double M22, double M23,
    double M31, double M32, double M33)
{
    internal Matrix3(
        (double X, double Y, double Z) red,
        (double X, double Y, double Z) green,
        (double X, double Y, double Z) blue)
        : this(red.X, green.X, blue.X, red.Y, green.Y, blue.Y, red.Z, green.Z, blue.Z)
    {
    }

    internal static Matrix3 Identity { get; } = new(
        1, 0, 0,
        0, 1, 0,
        0, 0, 1);

    internal static Matrix3 SrgbToXyzD50 { get; } = new(
        0.4360412474, 0.3851128708, 0.1430458706,
        0.2224845560, 0.7169051226, 0.0606103658,
        0.0139201696, 0.0970671986, 0.7139125730);

    internal static Matrix3 DisplayP3ToXyzD50 { get; } = new(
        0.5151186967, 0.2919777887, 0.1571035032,
        0.2411891856, 0.6922440942, 0.0665667646,
       -0.0010504729, 0.0418790824, 0.7840713317);

    internal double Determinant =>
        M11 * (M22 * M33 - M23 * M32) -
        M12 * (M21 * M33 - M23 * M31) +
        M13 * (M21 * M32 - M22 * M31);

    internal bool IsFinite =>
        double.IsFinite(M11) && double.IsFinite(M12) && double.IsFinite(M13) &&
        double.IsFinite(M21) && double.IsFinite(M22) && double.IsFinite(M23) &&
        double.IsFinite(M31) && double.IsFinite(M32) && double.IsFinite(M33);

    internal Matrix3 Inverse()
    {
        var determinant = Determinant;
        return new(
            (M22 * M33 - M23 * M32) / determinant,
            (M13 * M32 - M12 * M33) / determinant,
            (M12 * M23 - M13 * M22) / determinant,
            (M23 * M31 - M21 * M33) / determinant,
            (M11 * M33 - M13 * M31) / determinant,
            (M13 * M21 - M11 * M23) / determinant,
            (M21 * M32 - M22 * M31) / determinant,
            (M12 * M31 - M11 * M32) / determinant,
            (M11 * M22 - M12 * M21) / determinant);
    }

    public static Matrix3 operator *(Matrix3 left, Matrix3 right) => new(
        left.M11 * right.M11 + left.M12 * right.M21 + left.M13 * right.M31,
        left.M11 * right.M12 + left.M12 * right.M22 + left.M13 * right.M32,
        left.M11 * right.M13 + left.M12 * right.M23 + left.M13 * right.M33,
        left.M21 * right.M11 + left.M22 * right.M21 + left.M23 * right.M31,
        left.M21 * right.M12 + left.M22 * right.M22 + left.M23 * right.M32,
        left.M21 * right.M13 + left.M22 * right.M23 + left.M23 * right.M33,
        left.M31 * right.M11 + left.M32 * right.M21 + left.M33 * right.M31,
        left.M31 * right.M12 + left.M32 * right.M22 + left.M33 * right.M32,
        left.M31 * right.M13 + left.M32 * right.M23 + left.M33 * right.M33);

    internal bool NearlyEquals(Matrix3 other, double tolerance) =>
        Math.Abs(M11 - other.M11) <= tolerance && Math.Abs(M12 - other.M12) <= tolerance &&
        Math.Abs(M13 - other.M13) <= tolerance && Math.Abs(M21 - other.M21) <= tolerance &&
        Math.Abs(M22 - other.M22) <= tolerance && Math.Abs(M23 - other.M23) <= tolerance &&
        Math.Abs(M31 - other.M31) <= tolerance && Math.Abs(M32 - other.M32) <= tolerance &&
        Math.Abs(M33 - other.M33) <= tolerance;
}
