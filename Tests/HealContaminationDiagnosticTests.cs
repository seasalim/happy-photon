using HappyPhoton.Services;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

// Report-only: how much of a snug blemish leaks into the healed disc through the
// boundary estimate, for point, ring-centred and outward boundary footprints.
public sealed class HealContaminationDiagnosticTests(ITestOutputHelper output)
{
    private const int Width = 480, Height = 320;
    private const double RadiusPixels = 40, DustDepth = 18000;

    [Fact]
    public void SnugBlemishLeakageByBoundaryFootprint()
    {
        var clean = Field(0);
        var footprints = new (string Name, HealBoundary Boundary)[]
        {
            ("point", new(1e-3, 0)), ("ring-centred r/3", new(1d / 3, 0)), ("outward r/3", new(1d / 3, 1d / 3)),
            ("outward sqrt2/3 (default)", HealBoundary.Default),
        };
        var candidate = new HealCandidate(HealFormulation.Membrane, HealDomain.Additive);
        foreach (var fill in new[] { 0, .5, .7, .8, .9 })
        {
            var dusty = Field(fill);
            foreach (var (name, boundary) in footprints)
            {
                using var basis = RenderPipelineTestSupport.CreateBase(dusty, height: Height);
                using var copy = new MagickImage(basis.Pixels);
                // Destination at (240,160), clean source at (100,160); radius 40 px.
                var spot = new HealSpot(240.5 / Width, 160.5 / Height, 100.5 / Width, 160.5 / Height, RadiusPixels / Width);
                new HealPrototype().Apply(copy, [spot], candidate, 2, boundary);
                var healed = RenderPipelineTestSupport.ReadPixels(copy);
                double sum = 0, max = 0; var count = 0;
                for (var y = 0; y < Height; y++)
                for (var x = 0; x < Width; x++)
                {
                    if (Math.Pow(x - 240, 2) + Math.Pow(y - 160, 2) >= RadiusPixels * RadiusPixels) continue;
                    for (var c = 0; c < 3; c++)
                    {
                        var error = Math.Abs(healed[(y * Width + x) * 3 + c] - clean[(y * Width + x) * 3 + c]);
                        sum += error; max = Math.Max(max, error); count++;
                    }
                }
                Assert.True(count > 0 && double.IsFinite(sum));
                output.WriteLine($"HEAL_CONTAMINATION fill={fill:0.0}r footprint={name} " +
                    $"mean_abs_codes={sum / count:0.0} max_abs_codes={max:0} dust_depth={DustDepth}");
            }
        }
    }

    // Textured clean field; a dark disc of radius fill x r at the destination, 1 px soft edge.
    private static ushort[] Field(double fill)
    {
        var pixels = new ushort[Width * Height * 3];
        for (var y = 0; y < Height; y++)
        for (var x = 0; x < Width; x++)
        {
            var value = 30000 + 1500 * Math.Sin(x / 7d) * Math.Cos(y / 9d);
            var distance = Math.Sqrt(Math.Pow(x - 240, 2) + Math.Pow(y - 160, 2));
            var dust = fill <= 0 ? 0 : Math.Clamp(fill * RadiusPixels + .5 - distance, 0, 1);
            for (var c = 0; c < 3; c++)
                pixels[(y * Width + x) * 3 + c] = (ushort)Math.Round(value - dust * DustDepth);
        }
        return pixels;
    }
}
