using System.Security.Cryptography;
using HappyPhoton.Models;
using Xunit;

namespace HappyPhoton.Tests;

internal static class FinishingFollowOn
{
    internal static string[] PhotoPaths()
    {
        var paths = FinishingReviewSheet.PhotoPaths().ToList();
        var requested = Environment.GetEnvironmentVariable("HAPPY_PHOTON_LOOKS_MONOCHROME_FIXTURE");
        var path = requested ?? Path.Combine(GoldenTestPaths.RepositoryRoot,
            "artifacts", "compatibility-fixtures", "m2462362.DNG");

        if (requested == null && !File.Exists(path))
        {
            return paths.ToArray();
        }

        FinishingGateSupport.LocalFile(path);
        var manifest = CompatibilityFixtureManifest.Load(Path.Combine(
            GoldenTestPaths.RepositoryRoot, "Tests", "compatibility-fixtures.json"));
        var fixture = manifest.SelectedFixtures.Single(item => item.Slug == "m2462362");
        Assert.Equal(fixture.SizeBytes, new FileInfo(path).Length);
        using var stream = File.OpenRead(path);
        Assert.Equal(fixture.Sha256, Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant());
        paths.Add(path);

        return paths.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    internal static (string Name, EditSettings Settings)[] Looks()
    {
        var looks = new List<(string, EditSettings)>
        {
            ("B&W neutral", new() { Saturation = -100 }),
            ("B&W red filter", new()
            {
                Saturation = -100,
                Mixer = new()
                {
                    Red = new() { Luminance = 31 }, Orange = new() { Luminance = 22 },
                    Blue = new() { Luminance = -34 }, Aqua = new() { Luminance = -19 }
                }
            }),
            ("B&W green filter", new()
            {
                Saturation = -100,
                Mixer = new()
                {
                    Green = new() { Luminance = 33 }, Yellow = new() { Luminance = 16 },
                    Red = new() { Luminance = -22 }, Orange = new() { Luminance = -14 }
                }
            }),
            ("B&W blue filter", new()
            {
                Saturation = -100,
                Mixer = new()
                {
                    Blue = new() { Luminance = 29 }, Aqua = new() { Luminance = 18 },
                    Red = new() { Luminance = -26 }, Orange = new() { Luminance = -21 }
                }
            })
        };
        (string Name, EditSettings Settings)[] tones =
        [
            ("Sepia", new()
            {
                CurveRed = Curve(.04, .32, .61, .84, .99),
                CurveGreen = Curve(.04, .28, .53, .76, .95),
                CurveBlue = Curve(.04, .25, .42, .62, .88)
            }),
            ("Cool tone", new()
            {
                CurveRed = Curve(.04, .25, .45, .69, .92),
                CurveGreen = Curve(.04, .28, .52, .76, .95),
                CurveBlue = Curve(.04, .33, .59, .84, .99)
            }),
            ("Split tone (teal shadows / warm highlights)", new()
            {
                CurveRed = Curve(.04, .23, .53, .83, .98),
                CurveGreen = Curve(.04, .30, .52, .75, .94),
                CurveBlue = Curve(.04, .33, .51, .66, .89)
            })
        ];

        foreach (var (name, settings) in tones)
        {
            looks.Add((name + " on color — reachable", settings));
            var monochrome = settings.Clone();
            monochrome.Saturation = -100;
            looks.Add((name + " with Saturation -100 — toning unreachable", monochrome));
        }

        return looks.ToArray();
    }

    private static CurveData Curve(params double[] outputs) => new()
    {
        Points = outputs.Select((value, index) => new CurvePoint(index / 4.0, value)).ToList()
    };
}
