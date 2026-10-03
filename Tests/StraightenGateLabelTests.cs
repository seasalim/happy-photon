using System.Text.Json;
using HappyPhoton.Services;
using ImageMagick;
using ImageMagick.Drawing;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class StraightenGateLabelTests(ITestOutputHelper output)
{
    public static IEnumerable<object[]> Photos() => StraightenGateFixtures.Labels.Select(n => new object[] { n });

    // D300 and Panasonic regions are carried labels from run 316 (Panasonic span grandfathered).
    // Coordinates are selected on the unmodified production interactive bases.
    // The two labels are distinct physical edges, never outputs of a detector.
    // Nine-pixel along-edge averaging suppresses texture before locating each transition.
    // Rejected, unused first labels (retained for old -> new review):
    // Leica: central upright (vertical, 25..280, x 799..800, radius 4, along radius 4);
    // next upright (vertical, 25..260, x 890..890, radius 4, along radius 4).
    // Both were short and sofa-occluded, with foliage behind; delta was about 0.23 deg.
    // Sony: hanging square ruler (horizontal, 500..830, y 590..592, radius 4, along radius 4);
    // panel joint (horizontal, 380..515, y 456..458, radius 4, along radius 4).
    // The ruler is movable and the seam too short; delta was about 1.78 deg.
    // Rejected, unused vertical relabel (camera-pitch keystone):
    // Leica: right upright (vertical, 10..675, x 1427..1415, radius 4, along radius 4);
    // far-right upright (vertical, 10..675, x 1518..1502, radius 4, along radius 4).
    // Sony: left panel (vertical, 20..420, x 298..289, radius 4, along radius 4);
    // right panel (vertical, 20..440, x 1318..1330, radius 4, along radius 4).
    // Horizontal endpoints and visible spans are fixed before fitting. A region's
    // length is its end-to-end span; excluded occlusions never contribute samples.
    internal static StraightenGateRegion[] Regions(string name) => name switch
    {
        "nikon-d300-colorchecker.nef" =>
        [
            new("left chart: printed panel right edge", true, 330, 850, 738, 729, 4, 4),
            new("right chart: printed panel left edge", true, 340, 850, 850, 840, 4, 4)
        ],
        "m2462362.DNG" => [],
        "panasonic-s9-standard.RW2" =>
        [
            new("filled doorway: right jamb", true, 250, 710, 321, 319, 5, 4),
            new("open doorway: right jamb", true, 450, 680, 633, 633, 5, 4)
        ],
        "sony-a9m3-lossy.ARW" =>
        [
            new("pegboard: middle seam, visible spans", false, 380, 1205, 455, 471, 3, 4,
                [new(380, 525), new(701, 736), new(837, 860), new(925, 970), new(1120, 1205)]),
            new("pegboard: upper rims of fixed hole row", false, 518, 1009, 52, 75, 3, 4,
                [new(518, 519), new(550, 552), new(582, 584), new(615, 617), new(647, 649),
                    new(712, 714), new(778, 780), new(811, 813), new(876, 878), new(909, 911),
                    new(942, 944), new(975, 977), new(1008, 1009)])
        ],
        _ => throw new ArgumentOutOfRangeException(nameof(name))
    };

    [Theory]
    [MemberData(nameof(Photos))]
    public void G3Before(string name)
    {
        Assert.SkipWhen(name == "sony-a9m3-lossy.ARW",
            "Sony is unlabellable: final rigid-edge labels disagree by 1.754 degrees (limit 0.15)");
        using var pair = StraightenGateFixtures.Load(name);
        var basis = pair.Interactive;
        var regions = Regions(name);

        if (regions.Length == 0)
        {
            StraightenGateHorizontalEligibility.ReportLeica(basis, output);
            Assert.Skip("No two rigid horizontal edges at least 400 px long; final label unavailable");
            return;
        }

        var fits = regions.Select(roi => StraightenGateOracle.Fit(basis.Pixels, roi)).ToArray();
        // G3 labels are correction angles. Oracle content angles are clockwise in y-down coordinates.
        var angles = fits.Select(f => -f.ContentAngle).ToArray();
        var difference = Math.Abs(angles[0] - angles[1]);
        var label = angles.Average();
        var valid = difference <= .15 && Math.Abs(label) <= 3;
        var imagePath = StraightenGateFixtures.ImagePath(name);
        WriteOverlay(basis, regions, fits, imagePath, label);
        output.WriteLine("STRAIGHTEN " + JsonSerializer.Serialize(new { gate = "G3", pid = Environment.ProcessId,
            values = new { fixture = name, width = basis.Pixels.Width, height = basis.Pixels.Height,
                regions, fits, correctionAngles = angles, difference, L = label, valid, imagePath } }));
        Assert.True(valid, $"G3 labels disagree or exceed the envelope for {name}; owner review required");
    }

    internal static void WriteOverlay(BaseImage basis, StraightenGateRegion[] regions,
        StraightenGateFit[] fits, string path, double label)
    {
        // Display-only normalization makes the monochrome base inspectable; fitting
        // always uses the unchanged linear Q16 base, never this visualization.
        using var view = new MagickImage(basis.Pixels);
        view.AutoLevel();
        view.GammaCorrect(2.2);
        view.Depth = 8;
        var font = Path.Combine(GoldenTestPaths.RepositoryRoot, "Assets", "Fonts", "HankenGrotesk-Regular.ttf");
        var draw = new Drawables().Font(font).FontPointSize(19);

        for (var i = 0; i < regions.Length; i++)
        {
            var roi = regions[i];
            var fit = fits[i];
            var color = i == 0 ? MagickColors.Red : MagickColors.Lime;
            (double X, double Y) Point(double along, double across) =>
                roi.Vertical ? (across, along) : (along, across);
            var a = Point(roi.Start, roi.AcrossStart - roi.Radius);
            var b = Point(roi.End, roi.AcrossEnd - roi.Radius);
            var c = Point(roi.End, roi.AcrossEnd + roi.Radius);
            var d = Point(roi.Start, roi.AcrossStart + roi.Radius);
            draw.FillColor(MagickColors.Transparent).StrokeColor(color).StrokeWidth(1)
                .Line(a.X, a.Y, b.X, b.Y).Line(b.X, b.Y, c.X, c.Y)
                .Line(c.X, c.Y, d.X, d.Y).Line(d.X, d.Y, a.X, a.Y);
            var first = Point(roi.Start, fit.At(roi.Start));

            foreach (var span in roi.VisibleSpans ?? [new(roi.Start, roi.End)])
            {
                var start = Point(span.Start, fit.At(span.Start));
                var end = Point(span.End, fit.At(span.End));
                draw.StrokeWidth(roi.VisibleSpans is null ? 2 : 3).Line(start.X, start.Y, end.X, end.Y);
            }

            draw.StrokeColor(MagickColors.Black).StrokeWidth(.5).FillColor(color)
                .Text(15, 30 + i * 28, $"{i + 1}: {roi.Name}; correction {-fit.ContentAngle:F5} deg")
                .Text(first.X + 8, first.Y + 15, (i + 1).ToString());
        }

        draw.FillColor(MagickColors.White).Text(15, 86, $"L = {label:F5} deg; linear-base display normalization only").Draw(view);
        view.Write(path);
    }

    [Fact]
    public void DumpPreviewBases()
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable("HAPPY_PHOTON_STRAIGHTEN_DUMP") == "1", "Opt-in label inspection images");

        foreach (var name in StraightenGateFixtures.Labels)
        {
            using var pair = StraightenGateFixtures.Load(name);
            using var result = new RenderPipeline().Render(new(pair.Interactive, new(), RenderIntent.Preview,
                1600, new(false, false)));
            result.Image.Write(StraightenGateFixtures.ImagePath(name + "-base"));
            using var linear = new MagickImage(pair.Interactive.Pixels);
            linear.AutoLevel();
            linear.GammaCorrect(2.2);
            linear.Depth = 8;
            linear.Write(StraightenGateFixtures.ImagePath(name + "-linear"));
            output.WriteLine($"{name}: {pair.Interactive.Pixels.Width}x{pair.Interactive.Pixels.Height}");
        }
    }
}


