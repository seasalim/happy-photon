using System.Text.Json;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

public sealed partial class StraightenGateOwnerTests(ITestOutputHelper output)
{
    public static IEnumerable<object[]> Photos() =>
        StraightenGateOwnerFixtures.Labels.Select(name => new object[] { name });

    // Claude placed these on temp-only overlays of the production bases and committed them before
    // any oracle fit was read (one attempt per photo). Spans skip occluded or low-contrast stretches:
    // DSCF0076's block top is fitted only where sky is behind it, and DSCF0075's lines skip the banner.
    internal static StraightenGateRegion[] Regions(string name) => name switch
    {
        "DSCF0076.JPG" =>
        [
            new("upper plinth block: top edge, sky-backed spans", false, 518, 1112, 606.6, 623.9, 4, 4,
                [new(520, 615), new(995, 1110)]),
            new("lower plinth: top edge under and beside the block", false, 450, 1174, 669.9, 688.6, 4, 4)
        ],
        "DSCF0075.JPG" =>
        [
            new("facade: panel joint below the lower window row", false, 340, 1305, 556.0, 576.9, 4, 4,
                [new(340, 700), new(910, 1305)]),
            new("steps: top edge of the top step", false, 160, 1160, 567.65, 589.05, 4, 4,
                [new(160, 700), new(910, 1160)])
        ],
        "DSCF0076.RAF" =>
        [
            new("upper plinth block: top edge, sky-backed spans", false, 518, 1112, 606.35, 623.76, 4, 4,
                [new(520, 615), new(995, 1110)]),
            new("lower plinth: top edge under and beside the block", false, 450, 1174, 668.9, 689.0, 4, 4)
        ],
        // Unlabellable on the single attempt: the brick coping (x 600..1320) is the only rigid
        // horizontal of 400 px; the fascia lines under the two shop signs span about 330 and 320 px
        // and, by column probes, slope against the coping (camera yaw). No angle, delta or L assigned.
        "DSCF0129.RAF" => [],
        "IMG_5569.JPG" =>
        [
            new("left gate post: inner edge where it meets the hinged leaf", true, 640, 1160, 161, 160.5, 4, 4),
            new("right gate post: inner edge beside the leaf gap", true, 640, 1140, 1363.5, 1363.2, 4, 4)
        ],
        _ => throw new ArgumentOutOfRangeException(nameof(name))
    };

    [Fact]
    public void OwnerPresence()
    {
        foreach (var name in StraightenGateOwnerFixtures.Manifest.Keys)
        {
            Print("Owner-presence", StraightenGateOwnerFixtures.Verify(name));
        }
    }

    [Theory]
    [MemberData(nameof(Photos))]
    public void OwnerG3Before(string name)
    {
        StraightenGateOwnerFixtures.Verify(name);
        var regions = Regions(name);
        Assert.SkipUnless(regions.Length > 0, "No two rigid structures of at least 400 px; label unavailable");
        Assert.Equal(2, regions.Length);

        foreach (var region in regions)
        {
            var length = Math.Sqrt(Math.Pow(region.End - region.Start, 2) +
                Math.Pow(region.AcrossEnd - region.AcrossStart, 2));
            Assert.True(length >= 400, $"Owner G3 region must be at least 400 px: {region.Name}");
        }

        using var pair = StraightenGateOwnerFixtures.Load(name);
        var basis = pair.Interactive;
        var fits = regions.Select(region => StraightenGateOracle.Fit(basis.Pixels, region)).ToArray();
        var angles = fits.Select(fit => -fit.ContentAngle).ToArray();
        var difference = Math.Abs(angles[0] - angles[1]);
        var label = angles.Average();
        var valid = difference <= .15 && Math.Abs(label) <= 3;
        var imagePath = StraightenGateOwnerFixtures.ImagePath(name, "overlay");
        StraightenGateLabelTests.WriteOverlay(basis, regions, fits, imagePath, label);
        Print("G3-owner", new { fixture = name, width = basis.Pixels.Width, height = basis.Pixels.Height,
            regions, fits, correctionAngles = angles, difference, L = label, valid, imagePath });
        Assert.True(valid, $"G3 owner labels disagree or exceed the envelope for {name}; owner review required");
    }

    [Fact]
    public void DumpOwnerPreviewBases()
    {
        // Resolve the opt-in folder before considering any image output.
        StraightenGateOwnerFixtures.PathFor(StraightenGateOwnerFixtures.Labels[0]);
        Assert.SkipUnless(Environment.GetEnvironmentVariable("HAPPY_PHOTON_STRAIGHTEN_OWNER_DUMP") == "1",
            "Opt-in owner linear-base inspection images");

        foreach (var name in StraightenGateOwnerFixtures.Manifest.Keys)
        {
            using var pair = StraightenGateOwnerFixtures.Load(name);
            using var linear = new MagickImage(pair.Interactive.Pixels);
            linear.AutoLevel();
            linear.GammaCorrect(2.2);
            linear.Depth = 8;
            var imagePath = StraightenGateOwnerFixtures.ImagePath(name, "linear");
            linear.Write(imagePath);
            Print("Owner-dump", new { fixture = name, width = pair.Interactive.Pixels.Width,
                height = pair.Interactive.Pixels.Height, imagePath });
        }
    }

    private void Print(string gate, object values) => output.WriteLine("STRAIGHTEN " +
        JsonSerializer.Serialize(new { gate, pid = Environment.ProcessId, values }));
}
