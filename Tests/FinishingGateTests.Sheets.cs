using System.Text.Json;
using HappyPhoton.Services;
using ImageMagick;
using Xunit;
using static HappyPhoton.Tests.FinishingGateSupport;
using static HappyPhoton.Tests.FinishingReviewSheet;

namespace HappyPhoton.Tests;

public sealed partial class FinishingGateTests
{
    [Fact]
    public void ReviewSheets()
    {
        OptIn();
        var candidates = Candidates();
        var qualified = candidates.Select(candidate => (Candidate: candidate, Receipts: Evidence(candidate)))
            .Where(item => item.Receipts != null).ToArray();
        Assert.NotEmpty(qualified);
        var directory = NewDirectory("review");
        var index = Start("Finishing looks — qualified candidates");
        index.Append("<p>Draft looks; shipping selection, grouping, order and Simplicity verdict await owner review.</p>");
        var pages = qualified.ToDictionary(item => item.Candidate.Id, item => Start(item.Candidate.Name));

        foreach (var (candidate, receipts) in qualified)
        {
            var html = pages[candidate.Id];
            html.Append("<p>").Append(Encode(candidate.Group)).Append(" · ").Append(candidate.Order)
                .Append(" · ").Append(Encode(candidate.Intent)).Append("</p>")
                .Append("<p>Unedited at left; look at right. Production previews at maximum dimension 1600; click for native pixels.</p>");
            AppendEvidence(html, receipts!);
            Exemptions(candidate);
            index.Append("<p><a href='").Append(candidate.Id).Append(".html'>").Append(Encode(candidate.Group))
                .Append(" · ").Append(Encode(candidate.Name)).Append("</a></p>");
        }

        var photos = PhotoPaths();

        for (var photo = 0; photo < photos.Length; photo++)
        {
            var file = LocalFile(photos[photo]);
            using var pair = Loader().LoadPreviewBaseWithOutcome(file, BaseDecodeSettings.Default, default).Pair;
            Assert.NotNull(pair);
            using var control = Render(pair.Interactive, new(), RenderIntent.Preview, 1600);

            foreach (var (candidate, _) in qualified)
            {
                using var actual = Render(pair.Interactive, candidate.Settings, RenderIntent.Preview, 1600);
                Pair(pages[candidate.Id], directory, $"{candidate.Id}-{photo:D2}",
                    Path.GetFileName(photos[photo]) + " — unedited / " + candidate.Name, control, actual);
            }

            // Only the committed cost fixtures have full-look G3 evidence.
            if (photos[photo] == GoldenTestPaths.Asset("canon-eos-6d-iso-6400.cr2") ||
                photos[photo] == GoldenTestPaths.Asset("iphone-14-pro-iso-1000.heic"))
            {
                using var full = Loader().LoadFullBase(file, BaseDecodeSettings.Default, default);
                Assert.NotNull(full);
                Assert.NotNull(pair.Large);

                foreach (var (candidate, _) in qualified.Where(item => Exemptions(item.Candidate).Length > 0))
                {
                    using var reference = Render(full, candidate.Settings, RenderIntent.Export);

                    foreach (var basis in new[] { pair.Interactive, pair.Large })
                    {
                        using var preview = Render(basis, candidate.Settings, RenderIntent.Preview);
                        using var aligned = new MagickImage(reference);
                        WysiwygTests.AlignForComparison(aligned, preview);
                        Pair(pages[candidate.Id], directory, $"{candidate.Id}-{photo:D2}-parity-{basis.Pixels.Width}",
                            Path.GetFileName(photos[photo]) + $" {basis.Pixels.Width}×{basis.Pixels.Height} — Develop / aligned full export; see full-look ΔE above",
                            preview, aligned);
                    }
                }
            }
        }

        foreach (var (candidate, _) in qualified)
        {
            File.WriteAllText(Path.Combine(directory, candidate.Id + ".html"), pages[candidate.Id].ToString());
        }

        File.WriteAllText(Path.Combine(directory, "index.html"), index.ToString());
        output.WriteLine("FINISHING " + JsonSerializer.Serialize(new
        {
            gate = "sheets", directory, photos = photos.Length, qualified = qualified.Select(item => item.Candidate.Id),
            withheld = candidates.Where(candidate => !pages.ContainsKey(candidate.Id)).Select(candidate => candidate.Id),
            ownerApproved = false
        }));
    }

    [Fact]
    public void FollowOnSheets()
    {
        OptIn();
        var directory = NewDirectory("follow-on");
        var looks = FollowOnLooks();
        var html = Start("Existing controls — B&W mixes and RGB toning");
        html.Append("<p>Unedited at left; named treatment at right. Production previews at maximum dimension 1600.</p>")
            .Append("<p>The four monochrome treatments use only Saturation −100 and mixer Luminance. ")
            .Append("Sepia, cool tone and teal/warm split tone use RGB curves, paired with the same curves plus Saturation −100. These are capability evidence, not shipping candidates.</p>")
            .Append("<p>Limits: toned monochrome is unreachable with these controls. Saturation −100 runs after the curves ")
            .Append("and removes their color; a true monochrome RAW skips RGB curves. Curves also provide no independent ")
            .Append("post-chroma shadows, midtones and highlights color wheels.</p>");
        var photos = FinishingFollowOn.PhotoPaths();
        var monochromeRawCount = 0;

        for (var photo = 0; photo < photos.Length; photo++)
        {
            using var pair = Loader().LoadPreviewBaseWithOutcome(
                LocalFile(photos[photo]), BaseDecodeSettings.Default, default).Pair;
            Assert.NotNull(pair);
            var monochromeRaw = pair.Interactive.Info.IsRawSource && pair.Interactive.Info.IsMonochrome;

            if (monochromeRaw)
            {
                monochromeRawCount++;
            }

            using var control = Render(pair.Interactive, new(), RenderIntent.Preview, 1600);

            for (var look = 0; look < looks.Length; look++)
            {
                using var actual = Render(pair.Interactive, looks[look].Settings, RenderIntent.Preview, 1600);
                var label = monochromeRaw
                    ? looks[look].Name.Replace("on color — reachable", "on true monochrome RAW — unreachable") +
                        " — true monochrome RAW: RGB curves skipped"
                    : looks[look].Name;
                Pair(html, directory, $"photo-{photo:D2}-look-{look}",
                    Path.GetFileName(photos[photo]) + " — " + label, control, actual);
            }
        }

        html.Append(monochromeRawCount == 0
            ? "<p>No true monochrome RAW is available in the supplied photos or compatibility cache; that source case is not pictured.</p>"
            : $"<p>True monochrome RAW photos shown: {monochromeRawCount}. RGB curves are skipped on these sources.</p>");
        File.WriteAllText(Path.Combine(directory, "index.html"), html.ToString());
        output.WriteLine("FINISHING " + JsonSerializer.Serialize(new
        {
            gate = "follow-on-sheets", directory, photos = photos.Length, images = photos.Length * looks.Length, monochromeRawCount
        }));
    }
}
