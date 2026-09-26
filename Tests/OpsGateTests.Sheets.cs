using System.Text;
using HappyPhoton.Services;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

public sealed partial class OpsGateTests
{
    [Fact]
    public void ReviewSheets()
    {
        OptIn(); using var pair = Loader().LoadPreviewBaseWithOutcome(LocalFile(), BaseDecodeSettings.Default, CancellationToken.None).Pair;
        Assert.NotNull(pair);
        using var skyline = OpsWorkloads.Skyline(1600);
        using var clear = Loader().LoadFullBase(LocalFile("canon-eos-6d-iso-6400.cr2"), BaseDecodeSettings.Default, CancellationToken.None);
        Assert.NotNull(clear);
        using var clearPixels = new MagickImage(clear.Pixels); clearPixels.Resize(1600, 0);
        using var clearSmall = new BaseImage(new MagickImage(clearPixels), clear.Info);
        using var haze = OpsWorkloads.Haze(clearSmall);
        var html = new StringBuilder("<!doctype html><meta charset=utf-8><title>OPS formulation review</title><h1>OPS-WP1 formulation review</h1><p>Operator off at left; candidate at right. Full frame and 1:1 centre crop. Candidate selection is pending owner review.</p>");
        html.Append("<p>Fixture: ").Append(Fixture).Append("; Clarity: ").Append(Candidate)
            .Append("; Dehaze: ").Append(Refine ? "edge-refined" : "lattice-only").Append(".</p>");
        var directory = Directory.CreateDirectory(Path.Combine(Folder, "sheets", Fixture, Candidate + (Refine ? "-refined" : "-lattice"))).FullName;
        foreach (var (name, basis) in new[] { ("fixture", pair.Interactive), ("skyline", skyline), ("haze", haze) })
        {
            html.Append("<h2>").Append(name).Append("</h2>");
            using var control = Render(basis, OpsArm.Off, RenderIntent.Preview);
            foreach (var amount in new[] { -100, -60, 60, 100 })
            foreach (var arm in new[] { new OpsArm("TX", Texture: amount), new OpsArm("CL", Clarity: amount), new OpsArm("DH", Dehaze: amount) })
            {
                using var actual = Render(basis, arm, RenderIntent.Preview);
                var slug = $"{name}-{arm.Name}-{amount}";
                WritePair(control, actual, Path.Combine(directory, slug + ".png"), false);
                WritePair(control, actual, Path.Combine(directory, slug + "-crop.png"), true);
                html.Append("<h3>").Append(arm.Name).Append(' ').Append(amount).Append("</h3><img width=1000 src=\"")
                    .Append(slug).Append(".png\"><br><img src=\"").Append(slug).Append("-crop.png\">");
            }
        }
        var path = Path.Combine(directory, "index.html"); File.WriteAllText(path, html.ToString());
        Report("sheets", new { path, images = 72, ownerApproved = false });
    }

    private static void WritePair(MagickImage control, MagickImage actual, string path, bool crop)
    {
        using var images = new MagickImageCollection();
        foreach (var source in new[] { control, actual })
        {
            var image = new MagickImage(source);
            if (crop)
            {
                var side = (uint)Math.Min(400, Math.Min(image.Width, image.Height));
                image.Crop(new MagickGeometry((int)(image.Width - side) / 2, (int)(image.Height - side) / 2, side, side));
                image.ResetPage();
            }
            else image.Resize(500, 0);
            images.Add(image);
        }
        using var pair = images.AppendHorizontally();
        // Review PNGs are 8-bit, including grayscale; numerical gates stay Q16.
        pair.Depth = 8; pair.Write(path);
        Assert.Equal(crop ? 800u : 1000u, pair.Width);
    }
}



