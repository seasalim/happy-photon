using System.Text.Json;
using HappyPhoton.Models;
using HappyPhoton.Services;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class HorizonDetectionRobustnessTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public void ConflictingFieldsUseBoundedOrientation(double angle)
    {
        // Equal-sized left/right fields, mirrored about the centre, with identical contrast and period.
        using var frame = Create((x, y) => Math.Sin(2 * Math.PI *
            (y - Math.Abs(x - 800) * Math.Tan(angle * Math.PI / 180)) / 100) > 0 ? .7 : .1);
        var result = HorizonDetection.Detect(frame, out var diagnostics);
        Print("conflicting", angle, diagnostics, result);
        Assert.True(diagnostics.NegativeLines > 0 && diagnostics.PositiveLines > 0,
            "Both conflicting fields must supply fitted lines");
        Assert.True(diagnostics.Spread >= angle * .5, "Disagreement must see the opposing field");
        Assert.InRange(result.HorizonRotation, -5, 5);
        Assert.Equal(HorizonDetection.Tier.Orientation, diagnostics.Tier);
    }

    [Theory]
    [InlineData(-3, -30)]
    [InlineData(0, 30)]
    [InlineData(3, 30)]
    public void CameraPitchNeverReturnsWrongRoll(double roll, int pitch)
    {
        using var grid = Create(Grid);
        using var pitched = RenderGeometry.Apply(grid,
            new EditSettings { Geometry = new() { Vertical = pitch } }, out _);
        using var frame = StraightenGateScenes.Tilt(pitched, roll);
        Check("pitch", roll, frame);
    }

    [Theory]
    [InlineData(-3)]
    [InlineData(0)]
    [InlineData(3)]
    public void BarrelDistortionNeverReturnsWrongRoll(double roll)
    {
        // Inverse radial sampling expands source radius by 1 + .2*r², bending both grid axes.
        using var distorted = Create((x, y) =>
        {
            var dx = x - 800;
            var dy = y - 533.5;
            var radial = 1 + .2 * (dx * dx + dy * dy) / (800 * 800 + 533.5 * 533.5);

            return Grid(800 + dx * radial, 533.5 + dy * radial);
        });
        using var frame = StraightenGateScenes.Tilt(distorted, roll);
        Check("barrel", roll, frame);
    }

    [Theory]
    [InlineData(-3)]
    [InlineData(0)]
    [InlineData(3)]
    public void ShortStairsNeverOverrideLongRigidEdges(double roll)
    {
        // 8x10 short 100-px treads tilted +2 degrees, plus three 1200-px level rigid edges.
        // Their centres occupy separate bands so the long edges remain continuous.
        using var scene = Create((x, y) =>
        {
            if (x > 200 && x < 1400 &&
                (Math.Abs(y - 440) < 5 || Math.Abs(y - 530) < 5 || Math.Abs(y - 620) < 5))
                return .1;

            var cellX = x % 160;
            var rowY = y < 400 ? y : y - 670;
            var treadY = rowY % 95 - (cellX - 80) * Math.Tan(2 * Math.PI / 180);
            var tread = (y < 400 || y > 670) && cellX > 30 && cellX < 130 && Math.Abs(treadY - 45) < 4;

            return tread ? .1 : .7;
        });
        using var frame = StraightenGateScenes.Tilt(scene, roll);
        Check("stairs", roll, frame);
    }

    [Theory]
    [InlineData(-3)]
    [InlineData(-.5)]
    [InlineData(0)]
    [InlineData(.5)]
    [InlineData(3)]
    public void DiffuseMinorityLowersConfidenceWithoutVeto(double roll)
    {
        // Three level 1200-px bands carry the reference cluster. One 1200-px band 0.6 degrees
        // off is a small coherent competitor, and six 400-px bands at scattered angles hold
        // the inlier share near half without forming a comparable cluster.
        double[] scattered = [1.2, -1, 1.8, -1.6, 2.4, -2.2];

        using var scene = Create((x, y) =>
        {
            var level = x > 200 && x < 1400 &&
                (Math.Abs(y - 333) < 5 || Math.Abs(y - 533) < 5 || Math.Abs(y - 733) < 5);
            var competitor = x > 200 && x < 1400 &&
                Math.Abs(y - 433 - (x - 800) * Math.Tan(.6 * Math.PI / 180)) < 5;
            var slot = (int)((x - 200) / 450);
            var row = y < 733 ? 0 : 1;
            var centre = 250 + 450 * slot;
            var angle = slot is >= 0 and < 3 ? scattered[row * 3 + slot] : 0;
            var band = row == 0 ? 633 : 833;
            var piece = slot is >= 0 and < 3 && Math.Abs(x - centre) < 200 &&
                Math.Abs(y - band - (x - centre) * Math.Tan(angle * Math.PI / 180)) < 5;

            return level || competitor || piece ? .1 : .7;
        });
        using var frame = StraightenGateScenes.Tilt(scene, roll);
        var result = HorizonDetection.Detect(frame, out var diagnostics);
        Print("diffuse-minority", roll, diagnostics, result);

        Assert.True(diagnostics.Horizontal.InlierLength < .6 * diagnostics.Horizontal.Length,
            "The scene must keep the level cluster's share under 0.6");
        Assert.True(diagnostics.Horizontal.CompetitorLength <= .5 * diagnostics.Horizontal.InlierLength,
            "The competitor must stay a minority cluster");
        Assert.True(Math.Abs(result.HorizonRotation + roll) <= .15,
            $"diffuse minority: roll {roll}, correction {result.HorizonRotation}");
    }

    [Fact]
    public void BroadRivalCountsByLengthNotPeakHeight()
    {
        // Cross-review probe: the 2-degree cluster is broad, so a tight 4-degree line has the
        // higher density peak, yet the broad cluster holds more length and must be the rival.
        HorizonDetection.Line[] lines =
        [
            new(0, false, 1000), new(0, false, 1000), new(1.78, false, 400), new(2, false, 400),
            new(2.22, false, 400), new(4, false, 800)
        ];
        var family = HorizonDetection.Summarize(lines, false);

        Assert.Equal(2000, family.InlierLength, 6);
        Assert.Equal(1200, family.CompetitorLength, 6);
    }

    [Fact]
    public void FartherOutliersLowerConfidence()
    {
        // Same level reference and the same outlier length; only the outliers' angle moves.
        double Confidence(double outlier)
        {
            using var scene = Create((x, y) =>
            {
                var level = x > 200 && x < 1400 &&
                    (Math.Abs(y - 333) < 5 || Math.Abs(y - 533) < 5 || Math.Abs(y - 733) < 5);
                var off = x > 500 && x < 1100 &&
                    Math.Abs(y - 433 - (x - 800) * Math.Tan(outlier * Math.PI / 180)) < 5;

                return level || off ? .1 : .7;
            });
            var result = HorizonDetection.Detect(scene, out var diagnostics);
            Print("outlier-spread", outlier, diagnostics, result);
            Assert.True(Math.Abs(result.HorizonRotation) <= .15);

            return diagnostics.Confidence;
        }

        Assert.True(Confidence(3) < Confidence(1), "All-line spread must lower confidence");
    }

    private void Check(string scene, double roll, MagickImage frame)
    {
        var result = HorizonDetection.Detect(frame, out var diagnostics);
        Print(scene, roll, diagnostics, result);

        Assert.True(Math.Abs(result.HorizonRotation + roll) <= .15,
            $"{scene}: roll {roll}, correction {result.HorizonRotation}");
    }

    private void Print(string scene, double roll, HorizonDetection.Diagnostics diagnostics,
        HorizonDetection.Result? result) => output.WriteLine("STRAIGHTEN " + JsonSerializer.Serialize(
            new { gate = "robustness", pid = Environment.ProcessId, values = new { scene, roll, diagnostics, result } }));

    private static double Grid(double x, double y) =>
        Math.Abs(x - 800 - 160 * Math.Round((x - 800) / 160)) < 5 ||
        Math.Abs(y - 533.5 - 130 * Math.Round((y - 533.5) / 130)) < 5 ? .1 : .7;

    private static MagickImage Create(Func<double, double, double> sample)
    {
        const int width = 1600;
        const int height = 1067;
        var pixels = new ushort[width * height * 3];

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var sum = 0d;

                for (var sy = 0; sy < 4; sy++)
                {
                    for (var sx = 0; sx < 4; sx++)
                    {
                        sum += sample(x + (sx + .5) / 4, y + (sy + .5) / 4);
                    }
                }

                var value = (ushort)Math.Round(sum / 16 * ushort.MaxValue);
                var i = (y * width + x) * 3;
                pixels[i] = value;
                pixels[i + 1] = value;
                pixels[i + 2] = value;
            }
        }

        using var basis = RenderPipelineTestSupport.CreateBase(pixels, height: height);

        return new MagickImage(basis.Pixels);
    }
}
