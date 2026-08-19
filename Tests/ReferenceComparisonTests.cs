using HappyPhoton.Models;
using HappyPhoton.Services;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

internal sealed record ReferenceCandidate(string Tool, string FilePath);

internal sealed record ReferenceResolution(
    string Directory,
    bool UsedEnvironmentOverride,
    IReadOnlyList<ReferenceCandidate> Candidates);

internal static class ReferenceComparisonResolver
{
    public const string ReferenceDirectoryEnvironmentVariable =
        "HAPPY_PHOTON_COMPARE_REFERENCE_DIR";

    private static readonly HashSet<string> SupportedExtensions =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ".png", ".tif", ".tiff", ".jpg", ".jpeg"
        };

    public static ReferenceResolution Resolve(
        string fixturePath,
        string committedDirectory,
        Func<string, string?> readEnvironment)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fixturePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(committedDirectory);
        ArgumentNullException.ThrowIfNull(readEnvironment);
        var overrideDirectory = readEnvironment(
            ReferenceDirectoryEnvironmentVariable);
        var usesOverride = !string.IsNullOrWhiteSpace(overrideDirectory);
        var directory = usesOverride
            ? Path.GetFullPath(overrideDirectory!)
            : Path.GetFullPath(committedDirectory);
        if (!Directory.Exists(directory))
        {
            return new ReferenceResolution(
                directory,
                usesOverride,
                Array.Empty<ReferenceCandidate>());
        }

        var fixtureStem = Path.GetFileNameWithoutExtension(fixturePath);
        var prefix = fixtureStem + ".";
        var candidates = Directory.EnumerateFiles(directory, prefix + "*")
            .Where(path => SupportedExtensions.Contains(
                Path.GetExtension(path)))
            .Select(path => new
            {
                Path = path,
                Name = Path.GetFileName(path)
            })
            .Where(item => item.Name.StartsWith(
                prefix,
                StringComparison.OrdinalIgnoreCase))
            .Select(item => new ReferenceCandidate(
                item.Name[prefix.Length..^Path.GetExtension(item.Name).Length],
                item.Path))
            .Where(candidate => !string.IsNullOrWhiteSpace(candidate.Tool))
            .OrderBy(candidate => candidate.Tool, StringComparer.OrdinalIgnoreCase)
            .ThenBy(candidate => candidate.FilePath, StringComparer.Ordinal)
            .ToArray();
        return new ReferenceResolution(directory, usesOverride, candidates);
    }

    public static string MissingReferenceMessage(
        string fixturePath,
        ReferenceResolution resolution) =>
        $"No reference found for {Path.GetFileName(fixturePath)} in " +
        $"{resolution.Directory}. Add {Path.GetFileNameWithoutExtension(fixturePath)}" +
        $".<tool>.<ext> or set {ReferenceDirectoryEnvironmentVariable}.";
}

public sealed class ReferenceComparisonTests
{
    private const string CompareGate = "HAPPY_PHOTON_COMPARE";
    private readonly ITestOutputHelper _output;

    public ReferenceComparisonTests(ITestOutputHelper output) =>
        _output = output;

    [Theory]
    [InlineData("fujifilm-x30.raf")]
    [InlineData("canon-eos-6d-iso-6400.cr2")]
    public void ExternalReference_ReportsComparison(string fixtureName)
    {
        RequireOptInAndDeterminism();
        var fixturePath = RawBaseLoaderTestSupport.Asset(fixtureName);
        var committedDirectory = Path.Combine(
            GoldenTestPaths.AssetDirectory,
            "references");
        var resolution = ReferenceComparisonResolver.Resolve(
            fixturePath,
            committedDirectory,
            Environment.GetEnvironmentVariable);
        Assert.SkipWhen(
            resolution.Candidates.Count == 0,
            ReferenceComparisonResolver.MissingReferenceMessage(
                fixturePath,
                resolution));
        _output.WriteLine(
            $"REFERENCES fixture={fixtureName} " +
            $"tools={string.Join(",", resolution.Candidates.Select(candidate =>
                $"{candidate.Tool}:{Path.GetFileName(candidate.FilePath)}"))}");

        using var baseImage = LoadFullBase(fixturePath);
        foreach (var candidate in resolution.Candidates)
        {
            using var reference = new MagickImage(candidate.FilePath);
            var result = Compare(
                baseImage,
                reference,
                fixtureName,
                candidate.Tool,
                new EditSettings());
            WriteReport(result);
        }
    }

    [Theory]
    [InlineData("fujifilm-x30.raf")]
    [InlineData("canon-eos-6d-iso-6400.cr2")]
    public void SelfReference_DrivesFullComparisonComposition(string fixtureName)
    {
        RequireOptInAndDeterminism();
        var fixturePath = RawBaseLoaderTestSupport.Asset(fixtureName);
        using var baseImage = LoadFullBase(fixturePath);
        var smokeSettings = FindSelfReferenceSettings(baseImage);
        _output.WriteLine(
            $"SELF REFERENCE fixture={fixtureName} " +
            $"brightness={smokeSettings.Brightness}");
        var temporaryReference = Path.Combine(
            Path.GetTempPath(),
            $"happy-photon-{Path.GetFileNameWithoutExtension(fixtureName)}-" +
            $"{Guid.NewGuid():N}.png");
        try
        {
            using (var rendered = RenderAtCommonSize(baseImage, smokeSettings))
            {
                rendered.Format = MagickFormat.Png;
                rendered.Write(temporaryReference);
            }

            using var reference = new MagickImage(temporaryReference);
            var result = Compare(
                baseImage,
                reference,
                fixtureName,
                "self",
                smokeSettings);
            Assert.InRange(result.Bisection.Exposure, -0.02, 0.02);
            WriteReport(result);
        }
        finally
        {
            if (File.Exists(temporaryReference))
            {
                File.Delete(temporaryReference);
            }
        }
    }

    private ComparisonReport Compare(
        BaseImage baseImage,
        MagickImage suppliedReference,
        string fixtureName,
        string tool,
        EditSettings baseSettings)
    {
        using var reference = ImageComparisonMetrics.CanonicalizeReference(
            suppliedReference);
        ImageComparisonMetrics.ResizeToCommonSize(reference.Image);
        using var geometryCandidate = RenderAtCommonSize(baseImage, baseSettings);
        AssertGeometryAgreement(reference.Image, geometryCandidate);

        var referencePlanes = ImageComparisonMetrics.ReadPlanes(reference.Image);
        var targetMedian = ImageComparisonMetrics.MedianLuma(referencePlanes);
        var window = ImageComparisonMetrics.FindFlatWellLitWindow(referencePlanes);
        Assert.True(window.HasValue,
            "The reference has no well-lit 256px measurement window.");

        MagickImage? convergedCandidate = null;
        try
        {
            var bisection = ImageComparisonMetrics.BisectExposure(exposure =>
            {
                convergedCandidate?.Dispose();
                var settings = baseSettings.Clone();
                settings.Exposure = exposure;
                convergedCandidate = RenderAtCommonSize(baseImage, settings);
                return ImageComparisonMetrics.MedianLuma(
                    ImageComparisonMetrics.ReadPlanes(convergedCandidate));
            }, targetMedian);
            Assert.True(bisection.Converged,
                $"Exposure bisection did not converge: {bisection.Status}; " +
                $"target={targetMedian:F6}, last={bisection.MedianLuma:F6}.");
            Assert.NotNull(convergedCandidate);

            var candidatePlanes = ImageComparisonMetrics.ReadPlanes(
                convergedCandidate!);
            var referenceMetrics = ImageComparisonMetrics.Measure(
                referencePlanes,
                window!.Value);
            var candidateMetrics = ImageComparisonMetrics.Measure(
                candidatePlanes,
                window.Value);
            AssertFinite(referenceMetrics);
            AssertFinite(candidateMetrics);
            return new ComparisonReport(
                fixtureName,
                tool,
                reference.AssumedSrgb,
                reference.AppliedOrientation,
                targetMedian,
                bisection,
                window.Value,
                referenceMetrics,
                candidateMetrics);
        }
        finally
        {
            convergedCandidate?.Dispose();
        }
    }

    private static BaseImage LoadFullBase(string fixturePath)
    {
        var file = new ImageFile(fixturePath);
        return new RawBaseLoader().LoadFullBase(
            file,
            BaseDecodeSettings.Default,
            CancellationToken.None) ?? throw new InvalidOperationException(
                $"Could not decode RAW comparison fixture: {fixturePath}");
    }

    private static EditSettings FindSelfReferenceSettings(BaseImage baseImage)
    {
        foreach (var brightness in new[] { 0, 25, 50, 75, 100, -25, -50 })
        {
            var settings = new EditSettings { Brightness = brightness };
            using var candidate = RenderAtCommonSize(baseImage, settings);
            var planes = ImageComparisonMetrics.ReadPlanes(candidate);
            if (ImageComparisonMetrics.FindFlatWellLitWindow(planes).HasValue)
            {
                return settings;
            }
        }

        throw new InvalidOperationException(
            "Could not create a self-reference with a well-lit 256px window.");
    }

    private static MagickImage RenderAtCommonSize(
        BaseImage baseImage,
        EditSettings settings)
    {
        using var rendered = new RenderPipeline().Render(new RenderRequest(
            baseImage,
            settings,
            RenderIntent.Preview,
            ImageComparisonMetrics.CommonLongEdge,
            new RenderOptions(false, false)));
        return new MagickImage(rendered.Image);
    }

    private static void AssertGeometryAgreement(
        MagickImage reference,
        MagickImage candidate)
    {
        var referenceLandscape = reference.Width >= reference.Height;
        var candidateLandscape = candidate.Width >= candidate.Height;
        Assert.Equal(referenceLandscape, candidateLandscape);
        Assert.InRange(
            Math.Abs((long)reference.Width - candidate.Width),
            0,
            1);
        Assert.InRange(
            Math.Abs((long)reference.Height - candidate.Height),
            0,
            1);
    }

    private static void AssertFinite(ImageComparisonMeasurement measurement)
    {
        Assert.True(double.IsFinite(measurement.Acutance));
        AssertFinite(measurement.Luma);
        AssertFinite(measurement.Cb);
        AssertFinite(measurement.Cr);
    }

    private static void AssertFinite(PlaneVariation variation)
    {
        Assert.True(double.IsFinite(variation.TotalStandardDeviation));
        Assert.True(double.IsFinite(variation.BlurSurvivingStandardDeviation));
        if (variation.CoarseFraction is { } fraction)
        {
            Assert.True(double.IsFinite(fraction));
        }
    }

    private void WriteReport(ComparisonReport report)
    {
        _output.WriteLine(
            $"REFERENCE fixture={report.FixtureName} tool={report.Tool} " +
            $"color={(report.AssumedSrgb ? "assumed-sRGB (untagged)" : "normalized-to-sRGB")} " +
            $"orientation={report.AppliedOrientation}");
        _output.WriteLine(
            $"MATCH exposure={F6(report.Bisection.Exposure)}EV " +
            $"referenceMedian={F6(report.ReferenceMedian)} " +
            $"candidateMedian={F6(report.Bisection.MedianLuma)} " +
            $"evaluations={report.Bisection.Evaluations}");
        _output.WriteLine(
            $"ROI x={report.Window.X} y={report.Window.Y} " +
            $"size={report.Window.Width}x{report.Window.Height} " +
            $"referenceLumaMean={F6(report.Window.ReferenceLumaMean)} " +
            $"referenceLumaSd={F6(report.Window.ReferenceLumaStandardDeviation)}");
        WriteMeasurement("reference", report.Reference);
        WriteMeasurement("happy-photon", report.Candidate);
    }

    private void WriteMeasurement(
        string label,
        ImageComparisonMeasurement measurement) =>
        _output.WriteLine(
            $"METRICS image={label} acutance={F6(measurement.Acutance)} " +
            $"Y={Format(measurement.Luma)} " +
            $"Cb={Format(measurement.Cb)} " +
            $"Cr={Format(measurement.Cr)}");

    private static string Format(PlaneVariation variation) =>
        $"{F6(variation.TotalStandardDeviation)}/" +
        $"{F6(variation.BlurSurvivingStandardDeviation)}/" +
        (variation.CoarseFraction is { } fraction
            ? F6(fraction)
            : "undefined");

    private static string F6(double value) =>
        value.ToString("F6", System.Globalization.CultureInfo.InvariantCulture);

    private static void RequireOptInAndDeterminism()
    {
        Assert.SkipWhen(
            Environment.GetEnvironmentVariable(CompareGate) != "1",
            $"Set {CompareGate}=1 to run RAW reference comparisons.");
        Assert.SkipWhen(
            Environment.GetEnvironmentVariable("OMP_NUM_THREADS") != "1",
            "Start a fresh test process with OMP_NUM_THREADS=1 before LibRaw loads.");
    }

    private sealed record ComparisonReport(
        string FixtureName,
        string Tool,
        bool AssumedSrgb,
        int AppliedOrientation,
        double ReferenceMedian,
        ExposureBisectionResult Bisection,
        ComparisonWindow Window,
        ImageComparisonMeasurement Reference,
        ImageComparisonMeasurement Candidate);
}
