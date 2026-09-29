using System.Text.Json;
using HappyPhoton.Services;
using ImageMagick;
using Xunit;
using static HappyPhoton.Tests.FinishingGateSupport;

namespace HappyPhoton.Tests;

[Collection(AvaloniaTestCollection.Name)]
public sealed class FinishingAuthoringTests(ITestOutputHelper output)
{
    [Fact]
    public void FreezeManifest()
    {
        Assert.SkipWhen(Environment.GetEnvironmentVariable("HAPPY_PHOTON_FREEZE_LOOK_MANIFEST") != "1",
            "Explicit authoring action only; never invoked by G3");

        var manifest = FinishingLookHarness.Candidates().ToDictionary(
            candidate => candidate.Id, candidate => FinishingLookManifest.Derive(candidate.Settings));
        var path = Path.Combine(FinishingLookHarness.Folder, "exempt-components.json");
        File.WriteAllText(path, JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }) + "\n");
        output.WriteLine(path);
    }

    [Fact]
    public void Samples()
    {
        Assert.SkipWhen(Environment.GetEnvironmentVariable("HAPPY_PHOTON_LOOK_AUTHORING") != "1",
            "Unqualified authoring diagnostics only; not owner review sheets");
        OptIn();

        var directory = FinishingReviewSheet.NewDirectory("authoring");
        var candidates = Candidates();
        var requested = Environment.GetEnvironmentVariable("HAPPY_PHOTON_LOOK_AUTHORING_PHOTOS");
        var photos = string.IsNullOrWhiteSpace(requested)
            ? new[] { "canon-eos-350d.cr2", "iphone-14-pro-iso-1000.heic", "srgb-reference.jpg" }
                .Select(GoldenTestPaths.Asset).ToArray()
            : requested.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var group in candidates.GroupBy(candidate => candidate.Group))
        {
            using var rows = new MagickImageCollection();

            foreach (var photo in photos)
            {
                using var pair = Loader().LoadPreviewBaseWithOutcome(
                    LocalFile(photo), BaseDecodeSettings.Default, default).Pair;
                Assert.NotNull(pair);
                using var columns = new MagickImageCollection();
                using var control = Render(pair.Interactive, new(), RenderIntent.Preview, 1600);
                var thumb = new MagickImage(control);
                thumb.Resize(400, 0);
                columns.Add(thumb);

                foreach (var candidate in group)
                {
                    using var actual = Render(pair.Interactive, candidate.Settings, RenderIntent.Preview, 1600);
                    actual.Depth = 8;
                    actual.Write(Path.Combine(directory, candidate.Id + "-" + Path.GetFileName(photo) + ".png"));
                    var sample = new MagickImage(actual);
                    sample.Resize(400, 0);
                    columns.Add(sample);
                }

                rows.Add(columns.AppendHorizontally());
            }

            using var sheet = rows.AppendVertically();
            sheet.Depth = 8;
            sheet.Write(Path.Combine(directory, group.Key.Replace(" & ", "-") + ".png"));
            output.WriteLine(group.Key + ": unedited, " + string.Join(", ", group.Select(candidate => candidate.Name)));
        }

        output.WriteLine("AUTHORING " + directory);
    }
}
