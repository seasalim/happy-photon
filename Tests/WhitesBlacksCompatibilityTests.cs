using System.Text.Json;
using HappyPhoton.Services;
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
}
