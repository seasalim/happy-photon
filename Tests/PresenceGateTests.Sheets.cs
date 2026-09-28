using System.Text;
using HappyPhoton.Models;
using HappyPhoton.Services;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

public sealed partial class PresenceGateTests
{
    [Fact]
    public void ProductionReviewSheets()
    {
        OptIn();
        using var pair = Loader().LoadPreviewBaseWithOutcome(LocalFile(), BaseDecodeSettings.Default, default).Pair;
        Assert.NotNull(pair);
        ReviewSheet(pair.Interactive, Fixture, false);
        using var skyline = OpsWorkloads.Skyline(1600);
        ReviewSheet(skyline, "skyline", true);
    }

    private void ReviewSheet(BaseImage basis, string name, bool clarityOnly)
    {
        var folder = Directory.CreateDirectory(Path.Combine(Folder, "sheets", name)).FullName;
        var html = new StringBuilder("<!doctype html><meta charset=utf-8><title>Presence</title>" +
            "<h1>OPS-WP3 production look</h1><p>Operators off at left, production treatment at right. Owner approval pending.</p>");

        if (clarityOnly)
        {
            html.Append("<p>Synthetic step edge for the halo check. The third panel is the treatment minus the control, " +
                "×10 around mid-gray: a halo shows as a light or dark band along the edge.</p>");
        }

        using var control = Render(basis, new(), RenderIntent.Preview);

        foreach (var amount in new[] { -100, -60, 60, 100 })
        foreach (var texture in clarityOnly ? new[] { false } : new[] { true, false })
        {
            var settings = new EditSettings { Texture = texture ? amount : 0, Clarity = texture ? 0 : amount };
            using var actual = Render(basis, settings, RenderIntent.Preview);
            var slug = (texture ? "texture-" : "clarity-") + amount;

            foreach (var crop in new[] { false, true })
            {
                using var images = new MagickImageCollection();

                foreach (var source in new[] { control, actual })
                {
                    var image = new MagickImage(source);

                    if (crop)
                    {
                        image.Crop(new MagickGeometry((int)(image.Width - 400) / 2, (int)(image.Height - 400) / 2, 400, 400));
                        image.ResetPage();
                    }
                    else
                    {
                        image.Resize(500, 0);
                    }

                    images.Add(image);
                }

                if (clarityOnly) images.Add(AmplifiedDifference(images[0], images[1]));

                using var sheet = images.AppendHorizontally();
                sheet.Depth = 8;
                var filename = slug + (crop ? "-crop" : "") + ".png";
                sheet.Write(Path.Combine(folder, filename));
                html.Append("<h2>").Append(slug).Append("</h2><img src=\"").Append(filename).Append("\">");
            }
        }

        File.WriteAllText(Path.Combine(folder, "index.html"), html.ToString());
        Report("sheets", new { folder, ownerApproved = false });
    }

    private static MagickImage AmplifiedDifference(IMagickImage<ushort> control, IMagickImage<ushort> actual)
    {
        var result = new MagickImage(control);
        using var controlPixels = control.GetPixels();
        using var actualPixels = actual.GetPixels();
        using var resultPixels = result.GetPixels();
        var before = controlPixels.ToArray()!;
        var after = actualPixels.ToArray()!;
        var difference = new ushort[before.Length];
        var channels = (int)controlPixels.Channels;

        // Colour channels only; any alpha stays as rendered.
        for (var i = 0; i < difference.Length; i++)
        {
            difference[i] = i % channels < 3
                ? (ushort)Math.Clamp(32768 + 10 * (after[i] - before[i]), 0, 65535)
                : after[i];
        }

        resultPixels.SetPixels(difference);

        return result;
    }
}
