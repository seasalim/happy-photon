using System.Security.Cryptography;
using System.Text.Json;
using HappyPhoton.Services;
using HappyPhoton.Models;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class WhitesBlacksCompatibilityTests(ITestOutputHelper output)
{
    [Fact]
    public void FrozenHealCorpusCanonicalBytes()
    {
        var path = Path.Combine(GoldenTestPaths.RepositoryRoot, "Tests", "HealSerializationBaseline.jsonl");
        var rows = File.ReadAllLines(path).Select(line => JsonSerializer.Deserialize<FrozenDocument>(line)!).ToArray();
        Assert.Equal(84, rows.Length);
        var differences = 0;
        foreach (var row in rows)
        {
            var actual = System.Text.Encoding.UTF8.GetBytes(EditSettingsJson.Serialize(EditSettingsJson.Deserialize(row.Input, out _)));
            var expected = System.Text.Encoding.UTF8.GetBytes(row.Canonical);
            var changed = Math.Abs(actual.Length - expected.Length) + actual.Zip(expected).Count(pair => pair.First != pair.Second);
            differences += changed;
            if (changed != 0) output.WriteLine($"G5 changed: {row.Name}, bytes={changed}");
        }
        output.WriteLine($"G5 frozen HEAL corpus documents={rows.Length}, differing canonical bytes={differences}");
        Assert.Equal(0, differences);
    }

    private sealed record FrozenDocument(string Name, string Input, string Canonical);

    [Fact]
    public void FixtureFingerprints()
    {
        Assert.SkipWhen(Environment.GetEnvironmentVariable("HAPPY_PHOTON_PERF") != "1", "Opt-in G5 compatibility");
        var path = Path.Combine(GoldenTestPaths.RepositoryRoot, "artifacts", "ops-wp2", "before.json");
        var current = new SortedDictionary<string, string>();
        var renderer = new CurrentPipelineGoldenRenderer();
        var record = Environment.GetEnvironmentVariable("HAPPY_PHOTON_WB_BASELINE_RECORD") == "1";
        foreach (var asset in GoldenTestCases.Assets)
        {
            Assert.Equal(SourceAvailability.AvailableLocally, new SourceAvailabilityService().GetAvailability(asset.FilePath));
            using var basis = FrozenBase(asset);
            foreach (var item in asset.SettingsCases)
            {
                Record(asset.Slug + "__" + item.Slug, basis, item.CreateSettings());
            }
        }
        using (var basis = FrozenBase(GoldenTestCases.Assets.Single(a => a.Slug == "srgb-reference")))
            foreach (var term in new[] { "vertical", "horizontal", "aspect", "distortion" })
            foreach (var amount in new[] { -100, -50, 50, 100 })
            {
                var geometry = new GeometrySettings();
                typeof(GeometrySettings).GetProperty(char.ToUpperInvariant(term[0]) + term[1..])!.SetValue(geometry, amount);
                Record($"geometry__{term}-{(amount < 0 ? "minus" : "plus")}{Math.Abs(amount)}", basis, new() { Geometry = geometry });
            }
        var goldenNames = Directory.GetFiles(Path.Combine(GoldenTestPaths.GoldenDirectory, "v15"), "*.png")
            .Select(p => Path.GetFileNameWithoutExtension(p)).Order().ToArray();
        Assert.Equal(goldenNames, current.Keys.Where(k => k.EndsWith("/pixels")).Select(k => k[..^7]).Order());
        var documents = ConstructionBaselineGoldens.migration.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line[(line.IndexOf('|') + 1)..].Trim())
            .Concat([DocumentBoundaryGoldens.Current, DocumentBoundaryGoldens.Neutral]).ToArray();
        for (var i = 0; i < documents.Length; i++)
            current[$"legacy-document/{i}"] = EditSettingsJson.Serialize(EditSettingsJson.Deserialize(documents[i], out _));
        var description = $"{goldenNames.Length} golden renders/settings and {documents.Length} legacy documents";
        if (record)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(current));
            output.WriteLine($"G5 recorded {description}");
            return;
        }
        var baseline = JsonSerializer.Deserialize<SortedDictionary<string, string>>(File.ReadAllText(path))!;
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(path)!, "after.json"), JsonSerializer.Serialize(current));
        var differences = current.Count(pair => !baseline.TryGetValue(pair.Key, out var old) || old != pair.Value);
        foreach (var pair in current.Where(pair => !baseline.TryGetValue(pair.Key, out var old) || old != pair.Value))
            output.WriteLine($"G5 changed: {pair.Key}");
        output.WriteLine($"G5 {description}, differing canonical bytes or pixel hashes={differences}");
        Assert.Equal(baseline.Keys, current.Keys); Assert.Equal(0, differences);
        BaseImage FrozenBase(GoldenAssetCase asset)
        {
            var folder = Directory.CreateDirectory(Path.Combine(Path.GetDirectoryName(path)!, "bases")).FullName;
            var pixels = Path.Combine(folder, asset.Slug + ".miff");
            var metadata = Path.Combine(folder, asset.Slug + ".json");
            if (record)
            {
                using var decoded = renderer.LoadBase(asset);
                decoded.Pixels.Write(pixels);
                // Render uses the characterized Rec.2020 base; the camera-space decode matrix is no longer needed.
                File.WriteAllText(metadata, JsonSerializer.Serialize(decoded.Info with { CamToSrgb = null }));
            }
            return new BaseImage(new MagickImage(pixels), JsonSerializer.Deserialize<BaseImageInfo>(File.ReadAllText(metadata))!);
        }
        void Record(string key, BaseImage basis, EditSettings settings)
        {
            current[key + "/json"] = EditSettingsJson.Serialize(settings);
            using var image = renderer.Render(basis, settings);
            var pixels = RenderPipelineTestSupport.ReadPixels(image);
            var bytes = new byte[pixels.Length * 2]; Buffer.BlockCopy(pixels, 0, bytes, 0, bytes.Length);
            current[key + "/pixels"] = Convert.ToHexString(SHA256.HashData(bytes));
        }
    }
}
