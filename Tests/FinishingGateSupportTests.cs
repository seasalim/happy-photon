using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using HappyPhoton.Models;
using HappyPhoton.Services;
using ImageMagick;
using Xunit;
using static HappyPhoton.Tests.FinishingGateSupport;

namespace HappyPhoton.Tests;

[CollectionDefinition(nameof(FinishingGateSupportCollection), DisableParallelization = true)]
public sealed class FinishingGateSupportCollection;

[Collection(nameof(FinishingGateSupportCollection))]
public sealed class FinishingGateSupportTests
{
    [Fact]
    public void GatedArmResetsExactlyTheApprovedComponents()
    {
        foreach (var candidate in FinishingLookHarness.Candidates())
        {
            var original = JsonSerializer.SerializeToNode(candidate.Settings)!.AsObject();
            var gated = JsonSerializer.SerializeToNode(GatedSettings(candidate))!.AsObject();
            var defaults = JsonSerializer.SerializeToNode(new EditSettings())!.AsObject();
            var exemptions = Exemptions(candidate);

            foreach (var field in original)
            {
                var expected = exemptions.Contains(field.Key) ? defaults[field.Key] : field.Value;
                Assert.True(JsonNode.DeepEquals(expected, gated[field.Key]), candidate.Id + ": " + field.Key);
            }

            Assert.Equal(CorrectionBytes(candidate.Settings), CorrectionBytes(GatedSettings(candidate)));
        }
    }

    private static byte[] CorrectionBytes(EditSettings settings) => FinishingLookHarness.CorrectionBytes(settings);

    [Fact]
    public void ClippingCountsAnyChannelOncePerPixel()
    {
        using var image = new MagickImage(MagickColors.Gray, 3, 1);
        using var pixels = image.GetPixels();
        pixels.SetPixel(0, 0, new ushort[] { 0, 0, 123 });
        pixels.SetPixel(1, 0, new ushort[] { 65535, 123, 65535 });
        pixels.SetPixel(2, 0, new ushort[] { 123, 123, 123 });
        Assert.Equal(new FinishingClip(3, 1, 1), Clipping(image));
    }

    [Theory]
    [InlineData("pass", "false")]
    [InlineData("candidateSha256", "\"stale\"")]
    [InlineData("assemblySha256", "\"stale\"")]
    [InlineData("productionSha256", "\"stale\"")]
    [InlineData("fixture", "\"standard\"")]
    [InlineData("gate", "\"G4\"")]
    [InlineData("candidate", "\"another-look\"")]
    public void FailedOrStaleReceiptCannotQualifyASheet(string field, string value)
    {
        var candidate = FinishingLookHarness.Candidates()[0];
        var receipt = ReceiptFor(candidate);
        Assert.True(FinishingReviewSheet.CurrentReceipt(
            JsonSerializer.SerializeToElement(receipt), candidate, "G1", "raw"));
        receipt[field] = JsonNode.Parse(value);
        Assert.False(FinishingReviewSheet.CurrentReceipt(
            JsonSerializer.SerializeToElement(receipt), candidate, "G1", "raw"));
    }

    private static JsonNode ReceiptFor(FinishingCandidate candidate)
    {
        var fingerprint = new
        {
            candidateSha256 = candidate.LoadedSha256, assemblySha256 = AssemblyHash, productionSha256 = ProductionHash
        };

        return JsonSerializer.SerializeToNode(new
        {
            pass = true, gate = "G1", fixture = "raw", candidate = candidate.Id,
            fingerprint.candidateSha256, fingerprint.assemblySha256, fingerprint.productionSha256,
            records = new[] { fingerprint, fingerprint }
        })!;
    }

    [Theory]
    [InlineData("candidateSha256")]
    [InlineData("assemblySha256")]
    [InlineData("productionSha256")]
    public void MixedOrMissingRecordFingerprintCannotQualifyASheet(string field)
    {
        var candidate = FinishingLookHarness.Candidates()[0];
        var receipt = ReceiptFor(candidate);
        var record = receipt["records"]![1]!.AsObject();
        record[field] = "stale";
        Assert.False(FinishingReviewSheet.CurrentReceipt(
            JsonSerializer.SerializeToElement(receipt), candidate, "G1", "raw"));
        record.Remove(field);
        Assert.False(FinishingReviewSheet.CurrentReceipt(
            JsonSerializer.SerializeToElement(receipt), candidate, "G1", "raw"));
    }

    [Fact]
    public void MissingRecordsCannotQualifyASheet()
    {
        var candidate = FinishingLookHarness.Candidates()[0];
        var receipt = ReceiptFor(candidate);
        receipt["records"] = new JsonArray();
        Assert.False(FinishingReviewSheet.CurrentReceipt(
            JsonSerializer.SerializeToElement(receipt), candidate, "G1", "raw"));
        receipt.AsObject().Remove("records");
        Assert.False(FinishingReviewSheet.CurrentReceipt(
            JsonSerializer.SerializeToElement(receipt), candidate, "G1", "raw"));
    }

    [Fact]
    public void LoadedFingerprintRetainsTheExactBytesDespiteLaterFileChanges()
    {
        using var directory = new TemporaryDirectory();
        var source = FinishingLookHarness.CandidatePaths.First();
        var bytes = File.ReadAllBytes(source);
        var path = Path.Combine(directory.Path, Path.GetFileName(source));
        File.WriteAllBytes(path, bytes);
        var loaded = FinishingLookHarness.Load(path);
        var expected = Convert.ToHexString(SHA256.HashData(bytes));
        Assert.Equal(expected, loaded.LoadedSha256);

        File.AppendAllText(path, "\n");
        Assert.NotEqual(expected, FinishingLookHarness.Load(path).LoadedSha256);
        Assert.Equal(expected, loaded.LoadedSha256);
    }

    [Fact]
    public void FollowOnTreatmentsIsolateExistingControls()
    {
        var looks = FinishingReviewSheet.FollowOnLooks();
        Assert.Equal(10, looks.Length);

        foreach (var (_, settings) in looks.Take(4))
        {
            Assert.Equal(-100, settings.Saturation);
            Assert.Equal(0, settings.Contrast);
            Assert.Null(settings.CurveRed);
            Assert.Null(settings.CurveBlue);
            Assert.Null(settings.Effects);

            if (settings.Mixer != null)
            {
                foreach (var band in Enum.GetValues<ColorMixerBand>())
                {
                    Assert.Equal(0, settings.Mixer.GetBand(band).Hue);
                    Assert.Equal(0, settings.Mixer.GetBand(band).Saturation);
                }
            }
        }

        for (var index = 4; index < looks.Length; index += 2)
        {
            var color = looks[index].Settings;
            var monochrome = looks[index + 1].Settings.Clone();
            Assert.Equal(0, color.Saturation);
            Assert.NotNull(color.CurveRed);
            Assert.NotNull(color.CurveGreen);
            Assert.NotNull(color.CurveBlue);
            Assert.Equal(-100, monochrome.Saturation);
            monochrome.Saturation = 0;
            Assert.True(color.HasSameEdits(monochrome));
        }
    }

    [Fact]
    public void ToningSurvivesOnColorButNotAfterDesaturationOrOnMonochromeRaw()
    {
        ushort[] samples = [10000, 10000, 10000, 25000, 25000, 25000, 45000, 45000, 45000];
        using var color = RenderPipelineTestSupport.CreateBase(samples, isRaw: false);
        using var monochrome = RenderPipelineTestSupport.CreateBase(
            [10000, 10000, 10000, 25000, 25000, 25000, 45000, 45000, 45000],
            isRaw: true, isMonochrome: true);
        using var monoControl = Render(monochrome, new(), RenderIntent.Preview);
        var looks = FinishingReviewSheet.FollowOnLooks();

        for (var index = 4; index < looks.Length; index += 2)
        {
            using var colored = Render(color, looks[index].Settings, RenderIntent.Preview);
            using var desaturated = Render(color, looks[index + 1].Settings, RenderIntent.Preview);
            using var mono = Render(monochrome, looks[index].Settings, RenderIntent.Preview);
            var coloredPixels = RenderPipelineTestSupport.ReadPixels(colored);
            var grayPixels = RenderPipelineTestSupport.ReadPixels(desaturated);
            Assert.Contains(coloredPixels.Chunk(3), pixel => pixel.Max() - pixel.Min() > 100);

            foreach (var pixel in grayPixels.Chunk(3))
            {
                Assert.InRange(pixel.Max() - pixel.Min(), 0, 8);
            }

            Assert.Equal(RenderPipelineTestSupport.ReadPixels(monoControl), RenderPipelineTestSupport.ReadPixels(mono));
        }
    }

    [Fact]
    public void DroppedLooksStayOutOfTheGates()
    {
        Assert.Empty(Directory.GetFiles(FinishingLookHarness.Folder, "*.preset.json"));

        var dropped = JsonSerializer.Deserialize<DroppedLook[]>(
            File.ReadAllText(Path.Combine(FinishingLookHarness.DroppedFolder, "dropped.json")),
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.Equal(dropped.Select(look => look.Id + ".preset.json").Order(),
            FinishingLookHarness.DroppedPaths.Select(Path.GetFileName).Order());
        Assert.All(dropped, look =>
        {
            Assert.False(string.IsNullOrWhiteSpace(look.Gate));
            Assert.False(string.IsNullOrWhiteSpace(look.Date));
            Assert.False(string.IsNullOrWhiteSpace(look.Spec));
        });

        var approved = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(
            Path.Combine(FinishingLookHarness.Folder, "approved-sha256.json")))!;
        var priorCandidate = Environment.GetEnvironmentVariable("HAPPY_PHOTON_FINISHING_CANDIDATE");

        try
        {
            Environment.SetEnvironmentVariable("HAPPY_PHOTON_FINISHING_CANDIDATE", null);
            Assert.Equal(approved.Keys.Where(key => key.EndsWith(".preset.json", StringComparison.Ordinal)).Order(),
                FinishingGateSupport.Candidates().Select(candidate => candidate.Id + ".preset.json").Order());
        }
        finally
        {
            Environment.SetEnvironmentVariable("HAPPY_PHOTON_FINISHING_CANDIDATE", priorCandidate);
        }
    }

    private sealed record DroppedLook(string Id, string Gate, string Date, string Spec);

    [Fact]
    public void ReviewPairWritesNativeDimensionsAndEscapesLabels()
    {
        using var directory = new TemporaryDirectory();
        using var left = new MagickImage(MagickColors.Gray, 1600, 1068);
        using var right = new MagickImage(MagickColors.White, 1600, 1068);
        var html = FinishingReviewSheet.Start("<candidate>");
        FinishingReviewSheet.Pair(html, directory.Path, "pair", "<owner-photo>", left, right);
        Assert.Contains("&lt;candidate&gt;", html.ToString());
        Assert.Contains("&lt;owner-photo&gt;", html.ToString());
        using var saved = new MagickImage(Path.Combine(directory.Path, "pair.png"));
        Assert.Equal(3200u, saved.Width);
        Assert.Equal(1068u, saved.Height);
    }
}
