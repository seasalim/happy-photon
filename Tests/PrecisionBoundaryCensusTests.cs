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
    public void SliceA1_MeasuresChromaticAdaptationBoundaries()
    {
        var gate = Environment.GetEnvironmentVariable(GateName);
        if (string.IsNullOrEmpty(gate))
        {
            return;
        }
        Assert.Equal("1", gate);

        _fixture.RequireWindows();
        var previousCulture = CultureInfo.CurrentCulture;
        var previousUiCulture = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var payload = new StringBuilder();
            var gateFailures = new List<string>();
            var caseCount = 0;
            payload.AppendLine(
                "CENSUS_COVERAGE anchors=4000,5500,7500 " +
                "targets=2000,12000,4500,8000 tints=-100,0,100 " +
                "colors=primary-secondary-sweeps,near-gamut-ring " +
                "tones=neutral,recovery resizeVectors=1");

            foreach (var anchor in AsShotAnchors)
            {
                using var fixture =
                    PrecisionFixture.CreateChromaticAdaptationSweep(anchor);
                foreach (var target in Targets)
                {
                    foreach (var tint in Tints)
                    {
                        foreach (var tone in ToneVectors)
                        {
                            var name = CaseName(anchor, target, tint, tone.Name);
                            RunCase(
                                payload,
                                gateFailures,
                                fixture,
                                name,
                                tone,
                                CreateSettings(target, tint, tone),
                                maxDimension: null);
                            caseCount++;
                        }
                    }
                }
            }

            using (var resizeFixture =
                PrecisionFixture.CreateChromaticAdaptationSweep(5500))
            {
                var tone = ToneVectors[1];
                RunCase(
                    payload,
                    gateFailures,
                    resizeFixture,
                    "resize-capture-anchor-5500-target-12000-tint-p100-recovery",
                    tone,
                    CreateSettings(12000, 100, tone),
                    maxDimension: 128);
                caseCount++;
            }

            payload.Append("A1_INTERIM cases=").Append(caseCount)
                .Append(" p1aOutcome=deferred phase1Outcome=none")
                .AppendLine();
            stopwatch.Stop();
            _output.WriteLine("PRECISION_CENSUS_METRIC_PAYLOAD_BEGIN");
            _output.WriteLine(payload.ToString().TrimEnd());
            _output.WriteLine("PRECISION_CENSUS_METRIC_PAYLOAD_END");
            _output.WriteLine("PRECISION_CENSUS_ENVIRONMENT_BEGIN");
            _output.WriteLine($"os={RuntimeInformation.OSDescription}");
            _output.WriteLine($"osArchitecture={RuntimeInformation.OSArchitecture}");
            _output.WriteLine(
                $"processArchitecture={RuntimeInformation.ProcessArchitecture}");
            _output.WriteLine($"magickVersion={MagickNET.Version}");
            _output.WriteLine($"magickFeatures={MagickNET.Delegates}");
            _output.WriteLine(
                $"elapsedMilliseconds={stopwatch.Elapsed.TotalMilliseconds:F3}");
            _output.WriteLine("PRECISION_CENSUS_ENVIRONMENT_END");
            Assert.True(
                gateFailures.Count == 0,
                "Precision census validity gate failure(s): " +
                string.Join("; ", gateFailures));
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
            CultureInfo.CurrentUICulture = previousUiCulture;
        }
    }

    private static void RunCase(
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
    }

    private static EditSettings CreateSettings(
        double kelvin,
        double tint,
        ToneVector tone) =>
        new()
        {
            Exposure = 0,
            Wb = new WhiteBalanceSettings
            {
                Mode = WbMode.Custom,
                Kelvin = kelvin,
                Tint = tint
            },
            Brightness = tone.Brightness,
            Contrast = 0,
            Shadows = 0,
            Highlights = tone.Highlights,
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

    private sealed record ToneVector(
        string Name,
        int Brightness,
        int Highlights);
}
