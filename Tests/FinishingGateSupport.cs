using System.Security.Cryptography;
using System.Text.Json;
using HappyPhoton.Models;
using HappyPhoton.Services;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

internal static class FinishingGateSupport
{
    internal static readonly (string Name, int Pixels, int Black, int White)[] Controls =
    [
        ("canon-eos-350d.cr2", 1705600, 1759, 0),
        ("canon-eos-6d-iso-6400.cr2", 1708800, 964375, 0),
        ("nikon-d300-colorchecker.nef", 1699200, 14911, 0),
        ("nikon-d70-burst-1.nef", 1530640, 7258, 0),
        ("nikon-d70-burst-2.nef", 1530640, 7258, 0),
        ("fujifilm-x30.raf", 1912000, 7, 0),
        ("pentax-k-r.dng", 1704000, 956, 0),
        ("iphone-14-pro-iso-1000.heic", 1920000, 69734, 22326),
        ("reference.heic", 958800, 0, 3395),
        ("srgb-reference.jpg", 958800, 0, 1416),
        ("adobe-rgb-reference.jpg", 958800, 0, 1847)
    ];

    internal static string Fixture => Environment.GetEnvironmentVariable("HAPPY_PHOTON_OPS_FIXTURE") == "standard"
        ? "iphone-14-pro-iso-1000.heic" : "canon-eos-6d-iso-6400.cr2";

    internal static string Folder => Directory.CreateDirectory(
        Path.Combine(GoldenTestPaths.RepositoryRoot, "artifacts", "finishing")).FullName;

    internal static IBaseImageLoader Loader() => new GatedBaseImageLoader(
        new BaseLoaderRouter(new RawBaseLoader(), new StandardBaseLoader()), new SourceAvailabilityService());

    internal static ImageFile LocalFile(string path)
    {
        Assert.Equal(SourceAvailability.AvailableLocally, new SourceAvailabilityService().GetAvailability(path));

        return new(path);
    }

    internal static FinishingCandidate[] Candidates()
    {
        var requested = Environment.GetEnvironmentVariable("HAPPY_PHOTON_FINISHING_CANDIDATE");
        var ids = requested?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [];
        var candidates = FinishingLookHarness.Candidates()
            .Where(candidate => ids.Length == 0 || ids.Contains(candidate.Id)).ToArray();
        Assert.NotEmpty(candidates);

        return candidates;
    }

    internal static void OptIn()
    {
        Assert.SkipWhen(Environment.GetEnvironmentVariable("HAPPY_PHOTON_FINISHING_GATES") != "1",
            "Opt-in finishing qualification and review sheets");
        Assert.Equal("1", Environment.GetEnvironmentVariable("HAPPY_PHOTON_FULL_CPU"));
        PerfEnvironment.AssertFullCpu();
#if DEBUG
        Assert.Fail("Use Release");
#endif
    }

    internal static string[] Exemptions(FinishingCandidate candidate)
    {
        Assert.Equal("580AF4E39C162B70015141C15FF046CDE8493BA23F6E45D272B357F461A24CED",
            Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(
                Path.Combine(FinishingLookHarness.Folder, "exempt-components.json")))));
        var frozen = JsonSerializer.Deserialize<Dictionary<string, string[]>>(
            File.ReadAllText(Path.Combine(FinishingLookHarness.Folder, "exempt-components.json")))!;
        var derived = FinishingLookManifest.Derive(candidate.Settings);
        Assert.True(frozen.TryGetValue(candidate.Id, out var approved) && approved.SequenceEqual(derived),
            $"Owner approval required before G3: {candidate.Id} => {JsonSerializer.Serialize(derived)}");

        return approved!;
    }

    internal static EditSettings GatedSettings(FinishingCandidate candidate)
    {
        var node = JsonSerializer.SerializeToNode(candidate.Settings)!.AsObject();

        foreach (var field in Exemptions(candidate))
        {
            node.Remove(field);
        }

        return JsonSerializer.Deserialize<EditSettings>(node)!;
    }

    internal static MagickImage Render(BaseImage basis, EditSettings look, RenderIntent intent, int? size = null)
    {
        using var result = new RenderPipeline().Render(new(basis,
            FinishingLookHarness.Apply(new(), look), intent, size, new(false, false)));

        return new(result.Image);
    }

    internal static GoldenComparison Compare(MagickImage full, MagickImage preview)
    {
        using var aligned = new MagickImage(full);
        WysiwygTests.AlignForComparison(aligned, preview);

        return GoldenImageComparer.Compare(aligned, preview, GoldenComparisonDomain.DisplaySrgb);
    }

    internal static (double Mean, double P99) ParityPin(bool raw, bool large) => (raw, large) switch
    {
        (true, false) => (1.675, 10.015),
        (true, true) => (3.754, 23.887),
        (false, false) => (.941, 10.634),
        (false, true) => (.418, 5.618)
    };

    internal static FinishingClip Clipping(MagickImage image)
    {
        var pixels = RenderPipelineTestSupport.ReadPixels(image);
        var black = 0;
        var white = 0;

        for (var index = 0; index < pixels.Length; index += 3)
        {
            if (pixels[index] == 0 || pixels[index + 1] == 0 || pixels[index + 2] == 0) black++;
            if (pixels[index] == ushort.MaxValue || pixels[index + 1] == ushort.MaxValue ||
                pixels[index + 2] == ushort.MaxValue) white++;
        }

        return new(pixels.Length / 3, black, white);
    }

    internal static string CandidateHash(FinishingCandidate candidate) => Convert.ToHexString(
        SHA256.HashData(File.ReadAllBytes(FinishingLookHarness.CandidatePath(candidate.Id))));

    internal static string AssemblyHash => Convert.ToHexString(SHA256.HashData(
        File.ReadAllBytes(typeof(FinishingGateSupport).Assembly.Location)));

    internal static string ProductionHash => Convert.ToHexString(SHA256.HashData(
        File.ReadAllBytes(typeof(RenderPipeline).Assembly.Location)));

    internal static double Median(IEnumerable<double> values)
    {
        var sorted = values.Order().ToArray();

        return sorted[sorted.Length / 2];
    }
}

internal sealed record FinishingClip(int Pixels, int Black, int White)
{
    public double BlackPercent => 100.0 * Black / Pixels;

    public double WhitePercent => 100.0 * White / Pixels;
}
