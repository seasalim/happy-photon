using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using HappyPhoton.Models;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

[Collection(AvaloniaTestCollection.Name)]
public sealed class PipelinePrecisionInvestigationTests
{
    private readonly AvaloniaTestFixture _fixture;
    private readonly ITestOutputHelper _output;

    public PipelinePrecisionInvestigationTests(
        AvaloniaTestFixture fixture,
        ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    [Fact]
    public void PhaseZero_MeasuresBandingAttribution()
    {
        if (Environment.GetEnvironmentVariable("HAPPY_PHOTON_PRECISION") != "1")
        {
            return;
        }

        _fixture.RequireWindows();
        var previousCulture = CultureInfo.CurrentCulture;
        var previousUiCulture = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
        var stopwatch = Stopwatch.StartNew();
        var temporaryRoot = Directory.CreateDirectory(Path.Combine(
            Path.GetTempPath(),
            $"happy-photon-precision-{Guid.NewGuid():N}")).FullName;
        IReadOnlyList<PrecisionFixture>? fixtures = null;
        try
        {
            fixtures = PrecisionFixture.CreateAll(temporaryRoot);
            var payload = new StringBuilder();
            var cases = new List<PrecisionCaseMetrics>();
            var gateFailures = new List<string>();
            foreach (var fixture in fixtures)
            {
                foreach (var vector in CreateVectors())
                {
                    var stem = $"{fixture.Name}-{vector.Name}";
                    var pngPath = Path.Combine(temporaryRoot, $"{stem}.png");
                    var ditherPath = Path.Combine(
                        temporaryRoot,
                        $"{stem}-dither.png");
                    using var capture = PrecisionOracle.Capture(
                        fixture,
                        vector.Settings,
                        pngPath);
                    var metrics = PrecisionMetrics.Analyze(
                        fixture.Name,
                        vector.Name,
                        fixture.Width,
                        fixture.Height,
                        capture,
                        ditherPath);
                    cases.Add(metrics);
                    PrecisionReport.AppendCase(payload, metrics, capture.Parity);
                    var tiffBaseGate = fixture.LoadedFromTiff
                        ? capture.GateFailures.Count == 0 ? "pass" : "fail"
                        : "n/a";
                    payload.Append("GATE fixture=").Append(fixture.Name)
                        .Append(" vector=").Append(vector.Name)
                        .Append(" tiffBase=")
                        .Append(tiffBaseGate)
                        .Append(" previewPng=")
                        .Append(capture.PreviewExportMatch ? "pass" : "fail")
                        .Append(" ditherPreviewPng=")
                        .Append(metrics.Dither.PreviewExportMatch ? "pass" : "fail")
                        .AppendLine();
                    foreach (var failure in capture.GateFailures)
                    {
                        gateFailures.Add(
                            $"{fixture.Name}/{vector.Name}: {failure}");
                    }
                    if (!capture.PreviewExportMatch)
                    {
                        gateFailures.Add(
                            $"{fixture.Name}/{vector.Name}: preview and PNG differ");
                    }
                    if (!metrics.Dither.PreviewExportMatch)
                    {
                        gateFailures.Add(
                            $"{fixture.Name}/{vector.Name}: dithered preview and PNG differ");
                    }
                }
            }

            var outcome = PrecisionMetrics.SelectOutcome(cases);
            var gatesPassed = gateFailures.Count == 0;
            payload.Append("TERMINAL outcome=").Append(outcome)
                .Append(" gates=").Append(gatesPassed ? "pass" : "fail")
                .Append(" status=").Append(gatesPassed ? "confirmed" : "provisional")
                .AppendLine();
            stopwatch.Stop();
            _output.WriteLine("PRECISION_METRIC_PAYLOAD_BEGIN");
            _output.WriteLine(payload.ToString().TrimEnd());
            _output.WriteLine("PRECISION_METRIC_PAYLOAD_END");
            _output.WriteLine("PRECISION_ENVIRONMENT_BEGIN");
            _output.WriteLine($"os={RuntimeInformation.OSDescription}");
            _output.WriteLine($"osArchitecture={RuntimeInformation.OSArchitecture}");
            _output.WriteLine(
                $"processArchitecture={RuntimeInformation.ProcessArchitecture}");
            _output.WriteLine($"magickVersion={MagickNET.Version}");
            _output.WriteLine($"magickFeatures={MagickNET.Delegates}");
            _output.WriteLine(
                $"elapsedMilliseconds={stopwatch.Elapsed.TotalMilliseconds:F3}");
            _output.WriteLine("PRECISION_ENVIRONMENT_END");
            Assert.True(
                gateFailures.Count == 0,
                "Precision validity gate failure(s): " +
                string.Join("; ", gateFailures));
        }
        finally
        {
            if (fixtures != null)
            {
                foreach (var fixture in fixtures)
                {
                    fixture.Dispose();
                }
            }
            if (Directory.Exists(temporaryRoot))
            {
                Directory.Delete(temporaryRoot, recursive: true);
            }
            CultureInfo.CurrentCulture = previousCulture;
            CultureInfo.CurrentUICulture = previousUiCulture;
        }
    }

    private static IReadOnlyList<PrecisionSettingsVector> CreateVectors() =>
    [
        new("identity", CreateSettings(0, 0, 0)),
        new("shadow-lift", CreateSettings(3.0, 100, 0)),
        new("shadow-lift-contrast", CreateSettings(3.0, 100, 50))
    ];

    private static EditSettings CreateSettings(
        double exposure,
        int shadows,
        int contrast) =>
        new()
        {
            Exposure = exposure,
            Wb = new WhiteBalanceSettings { Mode = WbMode.AsShot },
            Brightness = 0,
            Contrast = contrast,
            Shadows = shadows,
            Highlights = 0,
            Saturation = 0,
            Vibrance = 0,
            BaseLook = false,
            HlReconstruction = HlReconstructionMode.Clip,
            Detail = new DetailSettings
            {
                CaptureSharpen = 0,
                NoiseReduction = FbddMode.Off,
                ChromaNr = 0
            },
            Rotation = 0,
            HorizonRotation = 0,
            Crop = null,
            Curve = new CurveData(),
            AppliedPresetId = null
        };

    private sealed record PrecisionSettingsVector(
        string Name,
        EditSettings Settings);
}

internal sealed record PrecisionMetricRow(
    int Row,
    int UsefulSamples,
    int ActualUnique,
    int ReferenceUnique,
    double UniqueCoverage,
    int LongestIdenticalRun,
    double MaximumStep,
    double MaximumStepExcess,
    double P99AbsoluteError,
    int LongestMissingCodes,
    double SignedMeanError,
    double SignedMinimumError,
    double SignedMaximumError,
    bool PreOutputBanding);

internal sealed record PrecisionCheckpointMetrics(
    PrecisionCheckpoint Checkpoint,
    IReadOnlyList<PrecisionMetricRow> Rows,
    double BlockMeanP99,
    bool PreOutputBanding,
    bool FinalOutputBanding);

internal sealed record PrecisionDitherMetrics(
    PrecisionCheckpointMetrics Checkpoint,
    double NativeBlockMeanP99,
    double BlockMeanReduction,
    double PointErrorP99,
    PrecisionParityMetrics Parity,
    bool Viable)
{
    public bool PreviewExportMatch => Parity.Match;
}

internal sealed record PrecisionCaseMetrics(
    string Fixture,
    string Vector,
    IReadOnlyList<PrecisionCheckpointMetrics> Checkpoints,
    PrecisionDitherMetrics Dither)
{
    public PrecisionCheckpointMetrics Get(int number) =>
        Checkpoints.Single(item => item.Checkpoint.Number == number);
}
