using System.Text.Json;
using HappyPhoton.Models;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class StraightenGateSyntheticTests(ITestOutputHelper output)
{
    [Fact]
    public void G1Before() => MeasureG1(StraightenGateScenes.Names.Append("facade-blurred"));

    [Fact]
    public void G1BlurredBefore() => MeasureG1(["facade-blurred"]);

    private void MeasureG1(IEnumerable<string> scenes)
    {
        var errors = new List<double>();
        var pass = true;

        foreach (var scene in scenes)
        {
            foreach (var portrait in new[] { false, true })
            {
                using var basis = StraightenGateScenes.Create(scene, portrait);

                foreach (var tilt in StraightenGateScenes.Tilts)
                {
                    using var frame = StraightenGateScenes.Tilt(basis, tilt);
                    var fit = StraightenGateOracle.Fit(frame, StraightenGateScenes.CentralEdge(frame));
                    var error = Math.Abs(fit.ContentAngle - tilt);
                    var valid = error <= .02 && (tilt == 0 || Math.Sign(fit.ContentAngle) == Math.Sign(tilt));
                    errors.Add(error);
                    pass &= valid;
                    Print("G1", new { scene, portrait, tilt, tau = fit.ContentAngle, error, fit.RmsPixels, valid });
                }
            }
        }

        Print("G1-summary", new { arms = errors.Count, max = errors.Max(), mean = errors.Average(), pass });
        Assert.True(pass, "G1 oracle baseline outside the envelope; owner review required");
    }

    [Fact]
    public void G2Before()
    {
        var residuals = new List<double>();

        foreach (var scene in StraightenGateScenes.Names.Append("facade-blurred"))
        {
            foreach (var portrait in new[] { false, true })
            {
                using var basis = StraightenGateScenes.Create(scene, portrait);

                foreach (var tilt in StraightenGateScenes.Tilts.Where(t => Math.Abs(t) >= .2))
                {
                    using var frame = StraightenGateScenes.Tilt(basis, tilt);
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
                        arm.Settings.HorizonRotation = -tilt;
                        using var final = RenderGeometry.Apply(frame, arm.Settings, out _);
                        var fit = StraightenGateOracle.Fit(final,
                            StraightenGateScenes.CentralEdge(final, arm.Settings.Rotation == 90));
                        var residual = Math.Abs(fit.ContentAngle);
                        residuals.Add(residual);
                        Print("G2", new { scene, portrait, tilt, geometry = arm.Name,
                            residual = fit.ContentAngle, fit.RmsPixels, valid = residual <= .03 });
                    }
                }
            }
        }

        Print("G2-summary", new { arms = residuals.Count, max = residuals.Max(), mean = residuals.Average() });
        Assert.True(residuals.All(r => r <= .03), "G2 reference residual outside the envelope; owner review required");
    }

    private void Print(string gate, object values) => output.WriteLine("STRAIGHTEN " +
        JsonSerializer.Serialize(new { gate, pid = Environment.ProcessId, values }));
}
