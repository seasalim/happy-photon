using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using HappyPhoton.Models;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

[Collection(AvaloniaTestCollection.Name)]
public sealed class PrecisionBoundaryCensusTests
{
    private const string GateName = "HAPPY_PHOTON_PRECISION_CENSUS";
    private const string OpenMpModeName =
        "HAPPY_PHOTON_PRECISION_CENSUS_OPENMP";

    private static readonly double[] AsShotAnchors = [4000, 5500, 7500];
    private static readonly double[] Targets = [2000, 12000, 4500, 8000];
    private static readonly double[] Tints = [-100, 0, 100];
    private static readonly ToneVector[] ToneVectors =
    [
        new("neutral", Brightness: 0, Highlights: 0),
        new("recovery", Brightness: 5, Highlights: -100)
    ];

    private readonly AvaloniaTestFixture _fixture;
    private readonly ITestOutputHelper _output;

    public PrecisionBoundaryCensusTests(
        AvaloniaTestFixture fixture,
        ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    [Fact]
    public void SliceA2_MeasuresCasesFiveFirstThenTwoThroughFour()
    {
        var gate = Environment.GetEnvironmentVariable(GateName);
        if (string.IsNullOrEmpty(gate))
        {
            return;
        }
        Assert.Equal("1", gate);

        _fixture.RequireWindows();
        var artifactPath = Environment.GetEnvironmentVariable(
            "HAPPY_PHOTON_PRECISION_CENSUS_ARTIFACT");
        Assert.False(
            string.IsNullOrWhiteSpace(artifactPath),
            "Set HAPPY_PHOTON_PRECISION_CENSUS_ARTIFACT to a distinct run path.");
        var manifest = PrecisionCensusManifest.Load();
        var openMpMode = Environment.GetEnvironmentVariable(OpenMpModeName);
        Assert.True(
            string.IsNullOrEmpty(openMpMode) || openMpMode == "uncontrolled",
            $"{OpenMpModeName} must be unset or 'uncontrolled'.");
        var controlOpenMp = string.IsNullOrEmpty(openMpMode);
        var previousCulture = CultureInfo.CurrentCulture;
        var previousUiCulture = CultureInfo.CurrentUICulture;
        var previousOpenMpThreads = Environment.GetEnvironmentVariable(
            "OMP_NUM_THREADS");
        var previousOpenMpDynamic = Environment.GetEnvironmentVariable(
            "OMP_DYNAMIC");
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
        if (controlOpenMp)
        {
            Environment.SetEnvironmentVariable("OMP_NUM_THREADS", "1");
            Environment.SetEnvironmentVariable("OMP_DYNAMIC", "FALSE");
        }
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var failures = new List<string>();
            using var artifact = new PrecisionCensusArtifact(
                artifactPath!,
                manifest,
                controlOpenMp);

            var caseFive = new StringBuilder();
            var failureCount = failures.Count;
            PrecisionRawCases.Run(caseFive, manifest, failures);
            RecordCase(
                artifact,
                "case-5-real-raw",
                caseFive,
                failures,
                failureCount);

            var caseTwo = new StringBuilder();
            failureCount = failures.Count;
            PrecisionColorCases.RunWidePrimaries(caseTwo, manifest);
            RecordCase(
                artifact,
                "case-2-wide-primaries",
                caseTwo,
                failures,
                failureCount);

            var caseThree = new StringBuilder();
            failureCount = failures.Count;
            PrecisionEditCases.RunExposure(caseThree, manifest, failures);
            RecordCase(
                artifact,
                "case-3-exposure-swings",
                caseThree,
                failures,
                failureCount);

            var caseFour = new StringBuilder();
            failureCount = failures.Count;
            PrecisionEditCases.RunStacked(caseFour, manifest, failures);
            RecordCase(
                artifact,
                "case-4-stacked-edits",
                caseFour,
                failures,
                failureCount);

            var caseOne = new StringBuilder();
            failureCount = failures.Count;
            RunSyntheticBaseline(caseOne, failures, manifest);
            RecordCase(
                artifact,
                "case-1-synthetic-baseline",
                caseOne,
                failures,
                failureCount);
            artifact.Flush();

            stopwatch.Stop();
            _output.WriteLine("PRECISION_CENSUS_METRIC_PAYLOAD_BEGIN");
            _output.WriteLine(artifact.Payload.TrimEnd());
            _output.WriteLine("PRECISION_CENSUS_METRIC_PAYLOAD_END");
            _output.WriteLine("PRECISION_CENSUS_ENVIRONMENT_BEGIN");
            _output.WriteLine($"artifact={Path.GetFullPath(
                artifactPath!, GoldenTestPaths.RepositoryRoot)}");
            _output.WriteLine($"os={RuntimeInformation.OSDescription}");
            _output.WriteLine($"osArchitecture={RuntimeInformation.OSArchitecture}");
            _output.WriteLine(
                $"processArchitecture={RuntimeInformation.ProcessArchitecture}");
            _output.WriteLine($"magickVersion={MagickNET.Version}");
            _output.WriteLine($"magickFeatures={MagickNET.Delegates}");
            _output.WriteLine($"openMpControl={(controlOpenMp
                ? "single-thread"
                : "uncontrolled")}");
            _output.WriteLine($"openMpThreads={Environment.GetEnvironmentVariable(
                "OMP_NUM_THREADS") ?? "unset"}");
            _output.WriteLine($"openMpDynamic={Environment.GetEnvironmentVariable(
                "OMP_DYNAMIC") ?? "unset"}");
            _output.WriteLine(
                $"elapsedMilliseconds={stopwatch.Elapsed.TotalMilliseconds:F3}");
            _output.WriteLine("PRECISION_CENSUS_ENVIRONMENT_END");
            Assert.True(
                failures.Count == 0,
                "Precision census validity gate failure(s): " +
                string.Join("; ", failures));
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
            CultureInfo.CurrentUICulture = previousUiCulture;
            if (controlOpenMp)
            {
                Environment.SetEnvironmentVariable(
                    "OMP_NUM_THREADS", previousOpenMpThreads);
                Environment.SetEnvironmentVariable(
                    "OMP_DYNAMIC", previousOpenMpDynamic);
            }
        }
    }

    private static void RecordCase(
        PrecisionCensusArtifact artifact,
        string caseName,
        StringBuilder payload,
        IReadOnlyList<string> failures,
        int previousFailureCount)
    {
        var succeeded = failures.Count == previousFailureCount;
        artifact.RecordCase(caseName, payload, succeeded);
        Assert.True(
            succeeded,
            $"{caseName} validity failure(s): " +
            string.Join("; ", failures.Skip(previousFailureCount)));
    }

    private static void RunSyntheticBaseline(
        StringBuilder payload,
        List<string> allFailures,
        PrecisionCensusManifest manifest)
    {
        payload.AppendLine(
            "CENSUS_COVERAGE anchors=4000,5500,7500 " +
            "targets=2000,12000,4500,8000 tints=-100,0,100 " +
            "colors=primary-secondary-sweeps,near-gamut-ring " +
            "tones=neutral,recovery resizeVectors=1");
        var totals = new SyntheticTotals();
        var population = manifest.Population("synthetic-saturation-extreme");
        var caseCount = 0;
        foreach (var anchor in AsShotAnchors)
        {
            using var fixture = PrecisionFixture.CreateChromaticAdaptationSweep(
                anchor,
                new PrecisionFixturePopulation(
                    population.Id,
                    population.Kind,
                    population.RowSemantics,
                    population.Intensity));
            foreach (var target in Targets)
            foreach (var tint in Tints)
            foreach (var tone in ToneVectors)
            {
                var name = CaseName(anchor, target, tint, tone.Name);
                var capture = RunCase(
                    payload,
                    allFailures,
                    fixture,
                    name,
                    tone,
                    CreateSettings(target, tint, tone),
                    maxDimension: null);
                AddSyntheticEvidence(
                    payload, population.Id, name, capture, totals);
                caseCount++;
            }
        }

        using (var fixture = PrecisionFixture.CreateChromaticAdaptationSweep(
            5500,
            new PrecisionFixturePopulation(
                population.Id,
                population.Kind,
                population.RowSemantics,
                population.Intensity)))
        {
            var tone = ToneVectors[1];
            RunCase(
                payload,
                allFailures,
                fixture,
                "resize-capture-anchor-5500-target-12000-tint-p100-recovery",
                tone,
                CreateSettings(12000, 100, tone),
                maxDimension: 128);
            caseCount++;
        }

        payload.Append("CENSUS_SYNTHETIC_HEADLINE case=case-1-synthetic-baseline")
            .Append(" population=").Append(population.Id)
            .Append(" meaning=saturation-extreme-instrument-not-real-rate")
            .Append(" channelSamples=").Append(totals.ChannelSamples)
            .Append(" negativeClips=").Append(totals.NegativeClips)
            .Append(" negativeChannelRate=").Append(
                (totals.NegativeClips / (double)totals.ChannelSamples).ToString(
                    "F12", CultureInfo.InvariantCulture))
            .Append(" pixels=").Append(totals.Pixels)
            .Append(" anyNegativePixels=").Append(totals.AnyNegativePixels)
            .Append(" anyNegativePixelRate=").Append(
                (totals.AnyNegativePixels / (double)totals.Pixels).ToString(
                    "F12", CultureInfo.InvariantCulture))
            .Append(" maximumChannelCaseNegativeClips=")
            .Append(totals.MaximumChannelCaseNegativeClips)
            .Append(" maximumChannelCaseSamples=")
            .Append(totals.MaximumChannelCaseSamples)
            .Append(" maximumChannelCaseNegativeRate=").Append(
                (totals.MaximumChannelCaseNegativeClips /
                    (double)totals.MaximumChannelCaseSamples).ToString(
                        "F12", CultureInfo.InvariantCulture))
            .Append(" indeterminate=").Append(totals.Indeterminate)
            .Append(" basis=exact-full-population").AppendLine();
        payload.Append("A2_CASE_ONE cases=").Append(caseCount)
            .Append(" phaseZeroStructuralGate=false")
            .Append(" phaseZeroOutputGate=false").AppendLine();
    }

    private static void AddSyntheticEvidence(
        StringBuilder payload,
        string population,
        string caseName,
        PrecisionCensusCapture capture,
        SyntheticTotals totals)
    {
        var boundary = capture.Boundaries.Single(value =>
            value.Name == "post-chromatic-matrix");
        foreach (var aggregate in boundary.Aggregates)
        {
            totals.ChannelSamples += aggregate.ChannelSamples;
            totals.NegativeClips += aggregate.NegativeClips;
            totals.Indeterminate += aggregate.Indeterminate;
            if (aggregate.NegativeClips /
                (double)aggregate.ChannelSamples >
                totals.MaximumChannelCaseNegativeClips /
                (double)Math.Max(1, totals.MaximumChannelCaseSamples))
            {
                totals.MaximumChannelCaseNegativeClips = aggregate.NegativeClips;
                totals.MaximumChannelCaseSamples = aggregate.ChannelSamples;
            }
        }
        totals.Pixels += checked(boundary.Width * boundary.Height);
        totals.AnyNegativePixels += boundary.Samples
            .Where(sample => sample.Clip == PrecisionClipDirection.Negative)
            .Select(sample => (sample.X, sample.Y))
            .Distinct()
            .Count();
        PrecisionEvidenceReport.AppendBoundary(
            payload,
            "case-1-synthetic-baseline",
            population,
            boundary with
            {
                Name = $"{caseName}/post-chromatic-matrix"
            },
            capture.WorkingStorageQuality,
            phaseZeroThresholdCrossed: false);
    }

    private static PrecisionCensusCapture RunCase(
        StringBuilder payload,
        List<string> allFailures,
        PrecisionFixture fixture,
        string name,
        ToneVector tone,
        EditSettings settings,
        int? maxDimension)
    {
        var capture = PrecisionBoundaryCensus.Capture(
            fixture, settings, maxDimension);
        PrecisionReport.AppendCensusCase(
            payload, name, tone.Name, maxDimension, fixture, capture);
        foreach (var failure in capture.GateFailures)
        {
            allFailures.Add($"{name}: {failure}");
        }
        return capture;
    }

    private static EditSettings CreateSettings(
        double kelvin,
        double tint,
        ToneVector tone) =>
        new()
        {
            Wb = new WhiteBalanceSettings
            {
                Mode = WbMode.Custom,
                Kelvin = kelvin,
                Tint = tint
            },
            Brightness = tone.Brightness,
            Highlights = tone.Highlights,
            BaseLook = false,
            HlReconstruction = HlReconstructionMode.Clip,
            Detail = new DetailSettings
            {
                CaptureSharpen = 0,
                NoiseReduction = FbddMode.Off,
                ChromaNr = 0
            }
        };

    private static string CaseName(
        double anchor,
        double target,
        double tint,
        string tone) =>
        $"anchor-{anchor:0}-target-{target:0}-tint-{Signed(tint)}-{tone}";

    private static string Signed(double value) => value switch
    {
        < 0 => $"m{Math.Abs(value):0}",
        > 0 => $"p{value:0}",
        _ => "0"
    };

    internal static IEnumerable<string> SyntheticEvidenceBoundaries()
    {
        foreach (var anchor in AsShotAnchors)
        foreach (var target in Targets)
        foreach (var tint in Tints)
        foreach (var tone in ToneVectors)
        {
            yield return $"{CaseName(anchor, target, tint, tone.Name)}/" +
                "post-chromatic-matrix";
        }
    }

    private sealed record ToneVector(
        string Name,
        int Brightness,
        int Highlights);

    private sealed class SyntheticTotals
    {
        public long ChannelSamples;
        public long NegativeClips;
        public long Indeterminate;
        public long Pixels;
        public long AnyNegativePixels;
        public long MaximumChannelCaseNegativeClips;
        public long MaximumChannelCaseSamples;
    }
}
