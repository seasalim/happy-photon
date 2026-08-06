using ImageMagick;

namespace HappyPhoton.Services;

public sealed class ImageStatsService
{
    private const double QuantumRange = 65535.0;

    public (double Sharpness, double ClippedHighlightsPct,
        double ClippedShadowsPct, double MeanLuminance) Compute(string imagePath)
    {
        if (!File.Exists(imagePath))
            throw new FileNotFoundException("Thumbnail image was not found.", imagePath);

        using var image = new MagickImage(imagePath);
        return Compute(image);
    }

    public (double Sharpness, double ClippedHighlightsPct,
        double ClippedShadowsPct, double MeanLuminance) Compute(byte[] imageData)
    {
        using var image = new MagickImage(imageData);
        return Compute(image);
    }

    private static (double Sharpness, double ClippedHighlightsPct,
        double ClippedShadowsPct, double MeanLuminance) Compute(MagickImage image)
    {
        image.Grayscale();

        var (highlights, shadows, mean) = ComputeLuminanceStats(image);
        var sharpness = ComputeSharpness(image);
        return (sharpness, highlights, shadows, mean);
    }

    private static (double Highlights, double Shadows, double Mean)
        ComputeLuminanceStats(MagickImage image)
    {
        using var pixels = image.GetPixelsUnsafe();
        var data = pixels.ToByteArray(PixelMapping.RGB);
        if (data == null || data.Length == 0)
            return (0, 0, 0);

        const int bytesPerPixel = 6;
        var pixelCount = data.Length / bytesPerPixel;
        long highlights = 0;
        long shadows = 0;
        long total = 0;

        for (var offset = 0; offset < data.Length; offset += bytesPerPixel)
        {
            var luminance = data[offset + 1];
            if (luminance >= 250) highlights++;
            if (luminance <= 5) shadows++;
            total += luminance;
        }

        return (
            highlights * 100.0 / pixelCount,
            shadows * 100.0 / pixelCount,
            total / (double)pixelCount);
    }

    private static double ComputeSharpness(MagickImage image)
    {
        using var laplacian = new MagickImage(image);
        var kernel = new ConvolveMatrix(3, new double[]
        {
            0, -1, 0,
            -1, 4, -1,
            0, -1, 0
        });
        laplacian.Convolve(kernel);

        var standardDeviation = laplacian.Statistics().Composite().StandardDeviation;
        var variance = standardDeviation * standardDeviation;

        // Express Q16 variance as a percentage of the full 16-bit signal range.
        return variance * 100.0 / (QuantumRange * QuantumRange);
    }
}
