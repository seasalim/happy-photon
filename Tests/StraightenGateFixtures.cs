using System.Security.Cryptography;
using System.Text.Json;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

internal static class StraightenGateFixtures
{
    internal static readonly string[] Labels = ["nikon-d300-colorchecker.nef", "m2462362.DNG",
        "panasonic-s9-standard.RW2", "sony-a9m3-lossy.ARW"];

    internal static readonly string[] NegativePhotos = ["canon-eos-6d-iso-6400.cr2",
        "iphone-14-pro-iso-1000.heic", "fujifilm-x30.raf", "pentax-k-r.dng",
        "fuji-xt50-compressed.RAF", "canon-r5m2-raw-apsc.CR3"];

    internal static string PathFor(string name)
    {
        var committed = GoldenTestPaths.Asset(name);

        return File.Exists(committed) ? committed : Path.Combine(GoldenTestPaths.RepositoryRoot,
            "artifacts", "compatibility-fixtures", name);
    }

    internal static object Verify(string name)
    {
        var path = PathFor(name);
        Assert.SkipUnless(File.Exists(path), $"Missing straighten fixture: {name}");
        GoldenTestPaths.RequireReadableFixture(path);
        var manifestPath = Path.Combine(GoldenTestPaths.RepositoryRoot, "Tests", "compatibility-fixtures.json");
        using var manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var entry = manifest.RootElement.GetProperty("fixtures").EnumerateArray().FirstOrDefault(f =>
            f.GetProperty("slug").GetString() + f.GetProperty("extension").GetString() == name);
        var length = new FileInfo(path).Length;
        string expected;
        long? expectedLength = null;

        if (entry.ValueKind != JsonValueKind.Undefined)
        {
            expected = entry.GetProperty("sha256").GetString()!;
            expectedLength = entry.GetProperty("sizeBytes").GetInt64();
            Assert.Equal(expectedLength.Value, length);
        }
        else
        {
            var row = File.ReadLines(GoldenTestPaths.Asset("README.md")).Single(l => l.StartsWith($"| `{name}` |"));
            expected = System.Text.RegularExpressions.Regex.Match(row, "[a-f0-9]{64}").Value;

            if (name == "nikon-d300-colorchecker.nef") expectedLength = 11443794;
            if (name == "iphone-14-pro-iso-1000.heic") expectedLength = 2019243;
            if (expectedLength.HasValue) Assert.Equal(expectedLength.Value, length);
        }

        // The live availability check precedes opening source content even for hashing.
        GoldenTestPaths.RequireReadableFixture(path);
        using var stream = File.OpenRead(path);
        var actual = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        Assert.Equal(expected, actual);

        return new { name, present = true, length, expectedLength, sha256 = actual, match = true };
    }

    internal static PreviewBasePair Load(string name)
    {
        Verify(name);
        var file = FinishingGateSupport.LocalFile(PathFor(name));
        var pair = FinishingGateSupport.Loader().LoadPreviewBaseWithOutcome(file,
            BaseDecodeSettings.Default, CancellationToken.None).Pair;
        Assert.NotNull(pair);

        return pair;
    }

    internal static string ImagePath(string name) => Path.Combine(Directory.CreateDirectory(
        Path.Combine(GoldenTestPaths.RepositoryRoot, "artifacts", "straighten-baseline")).FullName,
        name + ".png");
}

public sealed class StraightenGatePresenceTests(ITestOutputHelper output)
{
    public static IEnumerable<object[]> Photos() => StraightenGateFixtures.NegativePhotos.Select(n => new object[] { n });

    [Theory]
    [MemberData(nameof(Photos))]
    public void G4Before(string name) => output.WriteLine("STRAIGHTEN " + JsonSerializer.Serialize(
        new { gate = "G4", pid = Environment.ProcessId, values = StraightenGateFixtures.Verify(name) }));
}
