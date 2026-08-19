using HappyPhoton.Services;
using ImageMagick;

namespace HappyPhoton.Tests;

internal sealed record ComparisonPlanes(
    int Width,
    int Height,
    double[] Luma,
    double[] Cb,
    double[] Cr);

internal readonly record struct ComparisonWindow(
    int X,
    int Y,
    int Width,
    int Height,
    double ReferenceLumaMean,
    double ReferenceLumaStandardDeviation);

internal readonly record struct PlaneVariation(
    double TotalStandardDeviation,
    double BlurSurvivingStandardDeviation,
    double? CoarseFraction);

internal readonly record struct ImageComparisonMeasurement(
    double Acutance,
    PlaneVariation Luma,
    PlaneVariation Cb,
    PlaneVariation Cr);

internal enum ExposureBisectionStatus
{
    Converged,
    TargetBelowRange,
    TargetAboveRange,
    IterationLimit
}

internal readonly record struct ExposureBisectionResult(
    ExposureBisectionStatus Status,
    double Exposure,
    double MedianLuma,
    int Evaluations)
{
    public bool Converged => Status == ExposureBisectionStatus.Converged;
}

internal sealed class CanonicalReference : IDisposable
{
    public MagickImage Image { get; }
    public bool AssumedSrgb { get; }
    public int AppliedOrientation { get; }

    public CanonicalReference(
        MagickImage image,
        bool assumedSrgb,
        int appliedOrientation)
    {
        Image = image;
        AssumedSrgb = assumedSrgb;
        AppliedOrientation = appliedOrientation;
    }

    public void Dispose() => Image.Dispose();
}

internal static class ImageComparisonMetrics
{
    public const int CommonLongEdge = 1600;
    public const int FlatWindowSize = 256;
    public const int CoarseBlurRadius = 4;
    public const double MedianTolerance = 0.25;
    public const double VariationEpsilon = 1e-12;

    public static CanonicalReference CanonicalizeReference(MagickImage source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var image = (MagickImage)source.Clone();
        try
        {
            var orientation = image.Orientation == OrientationType.Undefined
                ? 1
                : (int)image.Orientation;
            image.AutoOrient();
            var profile = image.GetColorProfile();
            var assumedSrgb = profile == null;
            if (profile != null)
            {
                image.TransformColorSpace(profile, ColorProfiles.SRGB);
            }
            else if (image.ColorSpace != ColorSpace.sRGB)
            {
                image.ColorSpace = ColorSpace.sRGB;
            }

            return new CanonicalReference(image, assumedSrgb, orientation);
        }
        catch
        {
            image.Dispose();
            throw;
        }
    }

    public static void ResizeToCommonSize(MagickImage image)
    {
        ArgumentNullException.ThrowIfNull(image);
        var longEdge = Math.Max(image.Width, image.Height);
        if (longEdge < CommonLongEdge)
        {
            throw new InvalidOperationException(
                $"Reference long edge {longEdge}px is smaller than the " +
                $"{CommonLongEdge}px measurement edge; references are never upscaled.");
        }

        RenderColorEncoding.ResizeInLinearLight(image, CommonLongEdge);
    }

    public static ComparisonPlanes ReadPlanes(MagickImage image)
    {
        ArgumentNullException.ThrowIfNull(image);
        using var pixels = image.GetPixels();
        var samples = pixels.ToByteArray(PixelMapping.RGB) ??
            throw new InvalidOperationException("Could not read display-sRGB pixels.");
        var count = checked((int)(image.Width * image.Height));
        var luma = new double[count];
        var cb = new double[count];
        var cr = new double[count];
        for (var pixel = 0; pixel < count; pixel++)
        {
            var index = pixel * 3;
            var y = 0.2126 * samples[index] +
                0.7152 * samples[index + 1] +
                0.0722 * samples[index + 2];
            luma[pixel] = y;
            cb[pixel] = samples[index + 2] - y;
            cr[pixel] = samples[index] - y;
        }

        return new ComparisonPlanes(
            checked((int)image.Width),
            checked((int)image.Height),
            luma,
            cb,
            cr);
    }

    public static double MedianLuma(ComparisonPlanes planes)
    {
        ArgumentNullException.ThrowIfNull(planes);
        if (planes.Luma.Length == 0)
            throw new ArgumentException("The image has no pixels.", nameof(planes));
        var values = (double[])planes.Luma.Clone();
        Array.Sort(values);
        var middle = values.Length / 2;
        return values.Length % 2 == 0
            ? (values[middle - 1] + values[middle]) / 2
            : values[middle];
    }

    public static ComparisonWindow? FindFlatWellLitWindow(
        ComparisonPlanes reference,
        int windowSize = FlatWindowSize)
    {
        ArgumentNullException.ThrowIfNull(reference);
        if (windowSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(windowSize));
        if (reference.Width < windowSize || reference.Height < windowSize)
            return null;

        var stride = reference.Width + 1;
        var integralLength = checked(stride * (reference.Height + 1));
        var sums = new double[integralLength];
        var squares = new double[integralLength];
        for (var y = 0; y < reference.Height; y++)
        {
            double rowSum = 0;
            double rowSquare = 0;
            for (var x = 0; x < reference.Width; x++)
            {
                var value = reference.Luma[y * reference.Width + x];
                rowSum += value;
                rowSquare += value * value;
                var target = (y + 1) * stride + x + 1;
                sums[target] = sums[target - stride] + rowSum;
                squares[target] = squares[target - stride] + rowSquare;
            }
        }

        ComparisonWindow? best = null;
        var count = windowSize * windowSize;
        for (var y = 0; y <= reference.Height - windowSize; y++)
        {
            for (var x = 0; x <= reference.Width - windowSize; x++)
            {
                var sum = RectSum(sums, stride, x, y, windowSize);
                var mean = sum / count;
                if (mean < 40 || mean > 200) continue;
                var squareSum = RectSum(squares, stride, x, y, windowSize);
                var variance = Math.Max(0, squareSum / count - mean * mean);
                var sd = Math.Sqrt(variance);
                if (best == null || sd < best.Value.ReferenceLumaStandardDeviation)
                {
                    best = new ComparisonWindow(
                        x, y, windowSize, windowSize, mean, sd);
                }
            }
        }

        return best;
    }

    public static ImageComparisonMeasurement Measure(
        ComparisonPlanes planes,
        ComparisonWindow window)
    {
        ArgumentNullException.ThrowIfNull(planes);
        ValidateWindow(planes, window);
        return new ImageComparisonMeasurement(
            Acutance(planes),
            MeasurePlane(planes.Luma, planes.Width, window),
            MeasurePlane(planes.Cb, planes.Width, window),
            MeasurePlane(planes.Cr, planes.Width, window));
    }

    public static double Acutance(ComparisonPlanes planes)
    {
        ArgumentNullException.ThrowIfNull(planes);
        if (planes.Width < 3 || planes.Height < 3) return 0;
        double sum = 0;
        var count = 0;
        for (var y = 1; y < planes.Height - 1; y++)
        {
            for (var x = 1; x < planes.Width - 1; x++)
            {
                var index = y * planes.Width + x;
                var dx = (planes.Luma[index + 1] -
                    planes.Luma[index - 1]) / 2;
                var dy = (planes.Luma[index + planes.Width] -
                    planes.Luma[index - planes.Width]) / 2;
                sum += Math.Sqrt(dx * dx + dy * dy);
                count++;
            }
        }
        return sum / count;
    }

    public static ExposureBisectionResult BisectExposure(
        Func<double, double> evaluateMedian,
        double targetMedian,
        int maxIterations = 12)
    {
        ArgumentNullException.ThrowIfNull(evaluateMedian);
        if (!double.IsFinite(targetMedian))
            throw new ArgumentOutOfRangeException(nameof(targetMedian));
        if (maxIterations <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxIterations));

        const double minimum = -3;
        const double maximum = 3;
        var evaluations = 1;
        var lowMedian = evaluateMedian(minimum);
        if (WithinTolerance(lowMedian, targetMedian))
            return Converged(minimum, lowMedian, evaluations);
        evaluations++;
        var highMedian = evaluateMedian(maximum);
        if (WithinTolerance(highMedian, targetMedian))
            return Converged(maximum, highMedian, evaluations);
        if (targetMedian < lowMedian)
            return new(ExposureBisectionStatus.TargetBelowRange,
                minimum, lowMedian, evaluations);
        if (targetMedian > highMedian)
            return new(ExposureBisectionStatus.TargetAboveRange,
                maximum, highMedian, evaluations);

        var low = minimum;
        var high = maximum;
        var lastExposure = minimum;
        var lastMedian = lowMedian;
        for (var iteration = 0; iteration < maxIterations; iteration++)
        {
            lastExposure = (low + high) / 2;
            lastMedian = evaluateMedian(lastExposure);
            evaluations++;
            if (WithinTolerance(lastMedian, targetMedian))
                return Converged(lastExposure, lastMedian, evaluations);
            if (lastMedian < targetMedian)
                low = lastExposure;
            else
                high = lastExposure;
        }

        return new(ExposureBisectionStatus.IterationLimit,
            lastExposure, lastMedian, evaluations);
    }

    private static ExposureBisectionResult Converged(
        double exposure,
        double median,
        int evaluations) =>
        new(ExposureBisectionStatus.Converged, exposure, median, evaluations);

    private static bool WithinTolerance(double actual, double target) =>
        double.IsFinite(actual) && Math.Abs(actual - target) <= MedianTolerance;

    private static double RectSum(
        double[] integral,
        int stride,
        int x,
        int y,
        int size)
    {
        var right = x + size;
        var bottom = y + size;
        return integral[bottom * stride + right] -
            integral[y * stride + right] -
            integral[bottom * stride + x] +
            integral[y * stride + x];
    }

    private static PlaneVariation MeasurePlane(
        double[] plane,
        int imageWidth,
        ComparisonWindow window)
    {
        var values = ExtractWindow(plane, imageWidth, window);
        var blurred = BoxBlurClampToEdge(
            values,
            window.Width,
            window.Height,
            CoarseBlurRadius);
        var total = StandardDeviation(values);
        var surviving = StandardDeviation(blurred);
        return new PlaneVariation(
            total,
            surviving,
            total < VariationEpsilon ? null : surviving / total);
    }

    private static double[] ExtractWindow(
        double[] plane,
        int imageWidth,
        ComparisonWindow window)
    {
        var result = new double[window.Width * window.Height];
        for (var y = 0; y < window.Height; y++)
        {
            Array.Copy(
                plane,
                (window.Y + y) * imageWidth + window.X,
                result,
                y * window.Width,
                window.Width);
        }
        return result;
    }

    private static double[] BoxBlurClampToEdge(
        double[] values,
        int width,
        int height,
        int radius)
    {
        var result = new double[values.Length];
        var diameter = radius * 2 + 1;
        var area = diameter * diameter;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                double sum = 0;
                for (var offsetY = -radius; offsetY <= radius; offsetY++)
                {
                    var sampleY = Math.Clamp(y + offsetY, 0, height - 1);
                    for (var offsetX = -radius; offsetX <= radius; offsetX++)
                    {
                        var sampleX = Math.Clamp(x + offsetX, 0, width - 1);
                        sum += values[sampleY * width + sampleX];
                    }
                }
                result[y * width + x] = sum / area;
            }
        }
        return result;
    }

    private static double StandardDeviation(double[] values)
    {
        if (values.Length == 0) return 0;
        var mean = values.Average();
        double sum = 0;
        foreach (var value in values)
        {
            var delta = value - mean;
            sum += delta * delta;
        }
        return Math.Sqrt(sum / values.Length);
    }

    private static void ValidateWindow(
        ComparisonPlanes planes,
        ComparisonWindow window)
    {
        if (window.Width <= 0 || window.Height <= 0 ||
            window.X < 0 || window.Y < 0 ||
            window.X + window.Width > planes.Width ||
            window.Y + window.Height > planes.Height)
        {
            throw new ArgumentOutOfRangeException(nameof(window));
        }
    }
}
