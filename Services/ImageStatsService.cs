using ImageMagick;

namespace HappyPhoton.Services;

public sealed class ImageStatsService
{
    internal const int CanonicalLongEdge = 150;
    private const double QuantumRange = 65535.0;
    private readonly ISourceAvailabilityService _availabilityService;

    public ImageStatsService() : this(new SourceAvailabilityService())
    {
    }

    internal ImageStatsService(
        ISourceAvailabilityService availabilityService) =>
        _availabilityService = availabilityService ??
            throw new ArgumentNullException(nameof(availabilityService));

    public (double Sharpness, double ClippedHighlightsPct,
        double ClippedShadowsPct, double MeanLuminance) Compute(string imagePath)
        => Compute(imagePath, SourceReadIntent.Background);

    internal (double Sharpness, double ClippedHighlightsPct,
        double ClippedShadowsPct, double MeanLuminance) Compute(
        string imagePath,
        SourceReadIntent intent)
    {
        if (!File.Exists(imagePath))
            throw new FileNotFoundException("Thumbnail image was not found.", imagePath);

        var availability = _availabilityService.GetAvailability(imagePath);
        if (!SourceAccessPolicy.CanRead(availability, intent))
        {
            if (availability == SourceAvailability.RequiresHydration)
            {
                throw new SourceReadDeferredException(imagePath);
            }

            throw new FileNotFoundException(
                "Thumbnail image was not available.",
                imagePath);
        }

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
        NormalizeSize(image);
        image.Grayscale();

        var (highlights, shadows, mean) = ComputeLuminanceStats(image);
        var sharpness = ComputeSharpness(image);
        return (sharpness, highlights, shadows, mean);
    }

    private static void NormalizeSize(MagickImage image)
    {
        var longEdge = Math.Max(image.Width, image.Height);
        if (longEdge == CanonicalLongEdge) return;

        var scale = CanonicalLongEdge / (double)longEdge;
        image.FilterType = FilterType.Lanczos;
        image.Resize(
            (uint)Math.Max(1, Math.Round(image.Width * scale)),
            (uint)Math.Max(1, Math.Round(image.Height * scale)));
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
