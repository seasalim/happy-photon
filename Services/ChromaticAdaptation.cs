namespace HappyPhoton.Services;

public static class ChromaticAdaptation
{
    private static readonly double[,] Bradford =
    {
        { 0.8951, 0.2664, -0.1614 },
        { -0.7502, 1.7135, 0.0367 },
        { 0.0389, -0.0685, 1.0296 }
    };

    private static readonly double[,] BradfordInverse =
    {
        { 0.9869929, -0.1470543, 0.1599627 },
        { 0.4323053, 0.5183603, 0.0492912 },
        { -0.0085287, 0.0400428, 0.9684867 }
    };

    public static double[,] CreateLinearRec2020Matrix(
        double[] sourceWhite,
        double[] destinationWhite)
    {
        var adaptation = CreateBradfordMatrix(sourceWhite, destinationWhite);
        return Multiply(
            RgbColorSpaceMatrices.XyzD65ToLinearRec2020DerivedExact,
            Multiply(
                adaptation,
                RgbColorSpaceMatrices.LinearRec2020ToXyzD65DerivedExact));
    }

    public static double[,] CreateBradfordMatrix(
        double[] sourceWhite,
        double[] destinationWhite)
    {
        ValidateVector(sourceWhite);
        ValidateVector(destinationWhite);
        var sourceCone = Multiply(Bradford, sourceWhite);
        var destinationCone = Multiply(Bradford, destinationWhite);
        var ratios = new double[3];
        for (var index = 0; index < 3; index++)
        {
            if (Math.Abs(sourceCone[index]) < 1e-12)
            {
                throw new ArgumentException(
                    "Source white produces a zero Bradford cone response.",
                    nameof(sourceWhite));
            }

            ratios[index] = destinationCone[index] / sourceCone[index];
        }

        return Multiply(
            BradfordInverse,
            Multiply(CreateDiagonal(ratios), Bradford));
    }

    public static (double[,] Matrix, double Fold) NormalizeForRender(
        double[,] matrix)
    {
        ValidateMatrix(matrix);
        var scale = 1.0;
        for (var row = 0; row < 3; row++)
        {
            var positiveSum = 0.0;
            for (var column = 0; column < 3; column++)
            {
                positiveSum += Math.Max(matrix[row, column], 0);
            }

            scale = Math.Max(scale, positiveSum);
        }

        var normalized = new double[3, 3];
        for (var row = 0; row < 3; row++)
        {
            for (var column = 0; column < 3; column++)
            {
                normalized[row, column] = matrix[row, column] / scale;
            }
        }

        return (normalized, scale);
    }

    public static double[] LinearSrgbToXyz(double[] rgb) =>
        Multiply(RgbColorSpaceMatrices.LinearSrgbToXyzD65PublishedRounded, rgb);

    public static double[] XyzToLinearSrgb(double[] xyz) =>
        Multiply(RgbColorSpaceMatrices.XyzD65ToLinearSrgbPublishedRounded, xyz);

    public static double[] LinearRec2020ToXyz(double[] rgb) =>
        Multiply(RgbColorSpaceMatrices.LinearRec2020ToXyzD65DerivedExact, rgb);

    public static double[] XyzToLinearRec2020(double[] xyz) =>
        Multiply(RgbColorSpaceMatrices.XyzD65ToLinearRec2020DerivedExact, xyz);

    public static double[,] CreateDiagonal(IReadOnlyList<double> diagonal)
    {
        ArgumentNullException.ThrowIfNull(diagonal);
        if (diagonal.Count != 3)
        {
            throw new ArgumentException("A 3×3 diagonal needs three values.");
        }

        if (diagonal.Any(value => !double.IsFinite(value)))
        {
            throw new ArgumentException("Diagonal values must be finite.");
        }

        return new[,]
        {
            { diagonal[0], 0, 0 },
            { 0, diagonal[1], 0 },
            { 0, 0, diagonal[2] }
        };
    }

    public static double[,] Identity() => new[,]
    {
        { 1.0, 0, 0 },
        { 0, 1.0, 0 },
        { 0, 0, 1.0 }
    };

    public static double[] Multiply(double[,] matrix, double[] vector)
    {
        ValidateMatrix(matrix);
        ValidateVector(vector);
        var result = new double[3];
        for (var row = 0; row < 3; row++)
        {
            for (var column = 0; column < 3; column++)
            {
                result[row] += matrix[row, column] * vector[column];
            }
        }

        return result;
    }

    public static double[,] Multiply(double[,] left, double[,] right)
    {
        ValidateMatrix(left);
        ValidateMatrix(right);
        var result = new double[3, 3];
        for (var row = 0; row < 3; row++)
        {
            for (var column = 0; column < 3; column++)
            {
                for (var index = 0; index < 3; index++)
                {
                    result[row, column] += left[row, index] * right[index, column];
                }
            }
        }

        return result;
    }

    private static void ValidateVector(double[] vector)
    {
        ArgumentNullException.ThrowIfNull(vector);
        if (vector.Length != 3)
        {
            throw new ArgumentException("Expected a three-component vector.");
        }

        if (vector.Any(value => !double.IsFinite(value)))
        {
            throw new ArgumentException("Vector values must be finite.");
        }
    }

    private static void ValidateMatrix(double[,] matrix)
    {
        ArgumentNullException.ThrowIfNull(matrix);
        if (matrix.GetLength(0) != 3 || matrix.GetLength(1) != 3)
        {
            throw new ArgumentException("Expected a 3×3 matrix.");
        }

        foreach (var value in matrix)
        {
            if (!double.IsFinite(value))
            {
                throw new ArgumentException("Matrix values must be finite.");
            }
        }
    }
}
