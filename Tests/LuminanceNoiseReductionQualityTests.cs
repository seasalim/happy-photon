using HappyPhoton.Models;
using HappyPhoton.Services;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class LuminanceNoiseReductionQualityTests
{
    private readonly ITestOutputHelper _output;

    public LuminanceNoiseReductionQualityTests(ITestOutputHelper output) =>
        _output = output;

    [Theory]
    [InlineData("canon-eos-6d-iso-6400.cr2", true)]
    [InlineData("iphone-14-pro-iso-1000.heic", false)]
    public void HighIsoFixture_MeetsNoiseEdgeAndChromaGates(
        string fileName,
        bool isRaw)
    {
        Assert.SkipWhen(
            Environment.GetEnvironmentVariable("HAPPY_PHOTON_NR_QUALITY") != "1",
            "Set HAPPY_PHOTON_NR_QUALITY=1 to run luminance-NR quality tuning.");
        var loader = isRaw
            ? (IBaseImageLoader)new RawBaseLoader()
            : new StandardBaseLoader();
        var file = new ImageFile(GoldenTestPaths.Asset(fileName));
        using var baseImage = loader.LoadPreviewBase(
            file,
            BaseDecodeSettings.Default,
            CancellationToken.None);
        Assert.SkipWhen(baseImage == null,
            $"The platform cannot decode {fileName}.");
        var scales = RenderNoiseReduction.ResolveScales(
            baseImage!.Pixels,
            baseImage.Info,
            0.5f);
        _output.WriteLine(
            $"{fileName}: {baseImage.Pixels.Width}x{baseImage.Pixels.Height}; " +
            $"scales {string.Join(", ", scales.Select(scale =>
                $"d{scale.Dilation}/t{scale.Threshold:F1}"))}.");
        using var baseline = Render(baseImage, 0);
        var baselinePixels = RenderPipelineTestSupport.ReadPixels(baseline);
        var baselineLuma = ReadLuma(baselinePixels);
        var displayPlanes = ImageComparisonMetrics.ReadPlanes(baseline);
        var flat = ImageComparisonMetrics.FindFlatWellLitWindow(
            displayPlanes,
            Math.Min(256, Math.Min(displayPlanes.Width, displayPlanes.Height) / 3));
        Assert.NotNull(flat);
        var baselineSigma = HighPassSigma(
            baselineLuma,
            displayPlanes.Width,
            flat.Value);
        var edgeSamples = StrongestEdgeSamples(
            baselineLuma,
            displayPlanes.Width,
            displayPlanes.Height);

        var failures = new List<string>();
        var reductions = new Dictionary<int, double>();
        foreach (var value in new[] { 25, 50, 100 })
        {
            using var candidate = Render(baseImage, value);
            var candidatePixels = RenderPipelineTestSupport.ReadPixels(candidate);
            var candidateLuma = ReadLuma(candidatePixels);
            var sigma = HighPassSigma(
                candidateLuma,
                displayPlanes.Width,
                flat.Value);
            var acutanceRatio = EdgeAcutance(
                candidateLuma,
                displayPlanes.Width,
                edgeSamples) /
                EdgeAcutance(
                    baselineLuma,
                    displayPlanes.Width,
                    edgeSamples);
            var maxChromaDelta = MaximumChromaDelta(
                baselinePixels,
                candidatePixels);
            reductions[value] = baselineSigma - sigma;
            _output.WriteLine(
                $"{fileName} NR {value}: flat sigma {sigma:F2} Q16 " +
                $"({1 - sigma / baselineSigma:P1} reduction), " +
                $"edge acutance {acutanceRatio:P1}, " +
                $"max chroma delta {maxChromaDelta:F3} Q16.");

            if (value == 50)
            {
                if (sigma > baselineSigma * 0.6)
                {
                    failures.Add(
                        $"NR 50 reduced flat-patch sigma by only " +
                        $"{1 - sigma / baselineSigma:P1}.");
                }
            }
            var minimumAcutance = value <= 50 ? 0.9 : 0.7;
            if (acutanceRatio < minimumAcutance)
            {
                failures.Add(
                    $"NR {value} retained only {acutanceRatio:P1} edge acutance.");
            }
            if (maxChromaDelta > 1)
            {
                failures.Add(
                    $"NR {value} shifted chroma by {maxChromaDelta:F3} Q16.");
            }
        }
        if (reductions[100] < reductions[50] * 1.15)
        {
            failures.Add(
                $"NR 100 reduced sigma by only " +
                $"{reductions[100] / reductions[50]:F2}x NR 50.");
        }
        Assert.Empty(failures);
    }

    private static MagickImage Render(BaseImage baseImage, int value) =>
        new RenderPipeline().RenderDisplayRec2020(new RenderRequest(
            baseImage,
            new EditSettings
            {
                Detail = new DetailSettings
                {
                    CaptureSharpen = 0,
                    LuminanceNr = value
                }
            },
            RenderIntent.Export,
            null,
            new RenderOptions(false, false)));

    private static double[] ReadLuma(ushort[] pixels)
    {
        var luma = new double[pixels.Length / 3];
        for (var index = 0; index < luma.Length; index++)
        {
            var pixel = index * 3;
            luma[index] = Rec2020Luminance.Red * pixels[pixel] +
                Rec2020Luminance.Green * pixels[pixel + 1] +
                Rec2020Luminance.Blue * pixels[pixel + 2];
        }
        return luma;
    }

    private static double HighPassSigma(
        double[] luma,
        int width,
        ComparisonWindow window)
    {
        var residuals = new List<double>(window.Width * window.Height);
        for (var y = window.Y + 1; y < window.Y + window.Height - 1; y++)
        for (var x = window.X + 1; x < window.X + window.Width - 1; x++)
        {
            double local = 0;
            for (var dy = -1; dy <= 1; dy++)
            for (var dx = -1; dx <= 1; dx++)
            {
                local += luma[(y + dy) * width + x + dx];
            }
            residuals.Add(luma[y * width + x] - local / 9);
        }
        var mean = residuals.Average();
        return Math.Sqrt(residuals.Sum(value =>
            (value - mean) * (value - mean)) / residuals.Count);
    }

    private static int[] StrongestEdgeSamples(
        double[] luma,
        int width,
        int height)
    {
        var smoothed = new double[luma.Length];
        for (var y = 4; y < height - 4; y++)
        for (var x = 4; x < width - 4; x++)
        {
            double sum = 0;
            for (var dy = -4; dy <= 4; dy++)
            for (var dx = -4; dx <= 4; dx++)
            {
                sum += luma[(y + dy) * width + x + dx];
            }
            smoothed[y * width + x] = sum / 81;
        }

        var edges = new List<(double Magnitude, int Index)>();
        for (var y = 5; y < height - 5; y++)
        for (var x = 5; x < width - 5; x++)
        {
            var index = y * width + x;
            var dx = (smoothed[index + 1] - smoothed[index - 1]) / 2;
            var dy = (smoothed[index + width] - smoothed[index - width]) / 2;
            edges.Add((Math.Sqrt(dx * dx + dy * dy), index));
        }
        return edges
            .OrderByDescending(edge => edge.Magnitude)
            .Take(Math.Max(1, edges.Count / 1000))
            .Select(edge => edge.Index)
            .ToArray();
    }

    private static double EdgeAcutance(
        double[] luma,
        int width,
        int[] samples)
    {
        double sum = 0;
        foreach (var index in samples)
        {
            var dx = (SmoothedAt(luma, width, index + 1) -
                SmoothedAt(luma, width, index - 1)) / 2;
            var dy = (SmoothedAt(luma, width, index + width) -
                SmoothedAt(luma, width, index - width)) / 2;
            sum += Math.Sqrt(dx * dx + dy * dy);
        }
        return sum / samples.Length;
    }

    private static double SmoothedAt(
        double[] luma,
        int width,
        int index)
    {
        double sum = 0;
        for (var dy = -4; dy <= 4; dy++)
        for (var dx = -4; dx <= 4; dx++)
        {
            sum += luma[index + dy * width + dx];
        }
        return sum / 81;
    }

    private static double MaximumChromaDelta(
        ushort[] baseline,
        ushort[] candidate)
    {
        double maximum = 0;
        for (var index = 0; index < baseline.Length; index += 3)
        {
            var beforeY = Rec2020Luminance.Red * baseline[index] +
                Rec2020Luminance.Green * baseline[index + 1] +
                Rec2020Luminance.Blue * baseline[index + 2];
            var afterY = Rec2020Luminance.Red * candidate[index] +
                Rec2020Luminance.Green * candidate[index + 1] +
                Rec2020Luminance.Blue * candidate[index + 2];
            maximum = Math.Max(maximum, Math.Abs(
                (candidate[index + 2] - afterY) -
                (baseline[index + 2] - beforeY)));
            maximum = Math.Max(maximum, Math.Abs(
                (candidate[index] - afterY) -
                (baseline[index] - beforeY)));
        }
        return maximum;
    }
}
