using System.Text.Json;
using HappyPhoton.Models;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class HorizonDetectionSyntheticTests(ITestOutputHelper output)
{
    [Fact]
    public void G1AccuracyAndG2ClosedLoop()
    {
        var errors = new List<double>();
        var residuals = new List<double>();
        var failures = new List<string>();

        foreach (var scene in StraightenGateScenes.Names.Append("facade-blurred"))
        {
            foreach (var portrait in new[] { false, true })
            {
                using var basis = StraightenGateScenes.Create(scene, portrait);

                foreach (var tilt in StraightenGateScenes.Tilts)
                {
                    using var frame = StraightenGateScenes.Tilt(basis, tilt);
                    var result = HorizonDetection.Detect(frame, out var diagnostics);
                    var error = Math.Abs((result?.HorizonRotation ?? diagnostics.RawPeak) + tilt);
                    errors.Add(error);
                    Print("G1", new { scene, portrait, tilt, diagnostics, error, result });

                    if (result is null || error > .15)
                    {
                        failures.Add($"G1 {scene}/{portrait}/{tilt}: result={result}, error={error}");
                    }

                    if (Math.Abs(tilt) < .2 || result is null) continue;
                    if (Math.Sign(result.Value.HorizonRotation) != -Math.Sign(tilt)) failures.Add("G2 wrong sign");

                    var settings = new List<(string Name, EditSettings Settings)> { ("identity", new()) };

                    if (scene == "horizon" && new[] { 1.5, 3, 5 }.Contains(Math.Abs(tilt)))
                    {
                        settings.Add(("rotation90", new() { Rotation = 90 }));
                        settings.Add(("vertical30", new() { Geometry = new() { Vertical = 30 } }));
                        settings.Add(("aspect100", new() { Geometry = new() { Aspect = 100 } }));
                        settings.Add(("aspect-100_vertical-30", new() { Geometry = new() { Aspect = -100, Vertical = -30 } }));
                    }

                    foreach (var arm in settings)
                    {
                        arm.Settings.HorizonRotation = result.Value.HorizonRotation;
                        using var final = RenderGeometry.Apply(frame, arm.Settings, out _);
                        var fit = StraightenGateOracle.Fit(final,
                            StraightenGateScenes.CentralEdge(final, arm.Settings.Rotation == 90));
                        var residual = Math.Abs(fit.ContentAngle);
                        var limit = arm.Name.StartsWith("aspect", StringComparison.Ordinal) ? .25 : .15;
                        residuals.Add(residual);
                        Print("G2", new { scene, portrait, tilt, geometry = arm.Name, residual, limit });

                        if (residual > limit)
                        {
                            failures.Add($"G2 {scene}/{portrait}/{tilt}/{arm.Name}: residual={residual}");
                        }
                    }
                }
            }
        }

        Print("G1-summary", new { arms = errors.Count, max = errors.Max(), mean = errors.Average() });
        Print("G2-summary", new { arms = residuals.Count, max = residuals.Max() });
        Assert.Empty(failures);
        Assert.Equal(104, errors.Count);
        Assert.Equal(144, residuals.Count);
        Assert.True(errors.Average() <= .06);
    }

    [Theory]
    [InlineData(-8)]
    [InlineData(-6)]
    [InlineData(6)]
    [InlineData(8)]
    public void BeyondWindow(double tilt)
    {
        foreach (var portrait in new[] { false, true })
        {
            using var basis = StraightenGateScenes.Create("facade", portrait);
            using var frame = StraightenGateScenes.Tilt(basis, tilt);
            var result = HorizonDetection.Detect(frame, out var diagnostics);
            Print("window", new { portrait, tilt, diagnostics, result });
            Assert.Null(result);
        }
    }

    public static IEnumerable<object[]> Negatives() => StraightenGateNegativeScenes.Names.SelectMany(
        name => new[] { false, true }.Select(portrait => new object[] { name, portrait }));

    [Theory]
    [MemberData(nameof(Negatives))]
    public void G4SyntheticNegatives(string name, bool portrait)
    {
        using var frame = StraightenGateNegativeScenes.Create(name, portrait);
        var result = HorizonDetection.Detect(frame, out var diagnostics);
        Print("G4-synthetic", new { name, portrait, diagnostics, result,
            margin = HorizonDetection.ConfidenceCutoff - 1.10 * diagnostics.Confidence });
        Assert.Null(result);
        Assert.True(HorizonDetection.ConfidenceCutoff >= 1.10 * diagnostics.Confidence);
    }

    private void Print(string gate, object values) => output.WriteLine("STRAIGHTEN " +
        JsonSerializer.Serialize(new { gate, pid = Environment.ProcessId, values }));
}
