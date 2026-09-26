using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using HappyPhoton.Models;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

// Freezes canonical outputs, not source whitespace or legacy-version syntax.
// Frozen G1 corpus and bound: docs/pipeline/TESTING.md#heal-wp2-frozen-controls (section 5).
public sealed class HealSerializationControlTests(ITestOutputHelper output)
{
    private static string BaselinePath => Path.Combine(
        GoldenTestPaths.RepositoryRoot, "Tests", "HealSerializationBaseline.jsonl");

    [Fact]
    public void CanonicalDocuments()
    {
        var inputs = HealSettingsCorpus.Documents().ToArray();
        var frozen = File.ReadAllLines(BaselinePath)
            .Select(line => JsonSerializer.Deserialize<FrozenDocument>(line)!).ToArray();
        Assert.Equal(frozen.Select(item => item.Name), inputs.Select(item => item.Name));
        long differences = 0;
        for (var index = 0; index < frozen.Length; index++)
        {
            var item = frozen[index];
            Assert.Equal(item.Input, inputs[index].Input);
            var actual = EditSettingsJson.Serialize(EditSettingsJson.Deserialize(item.Input, out _));
            var expectedBytes = Encoding.UTF8.GetBytes(item.Canonical);
            var actualBytes = Encoding.UTF8.GetBytes(actual);
            differences += Math.Abs(expectedBytes.Length - actualBytes.Length);
            differences += expectedBytes.Zip(actualBytes).Count(pair => pair.First != pair.Second);
            output.WriteLine($"DOCUMENT {item.Name}");
        }
        output.WriteLine($"G1 documents={frozen.Length}; differing_bytes={differences}");
        Assert.Equal(0, differences);
    }

    private sealed record FrozenDocument(string Name, string Input, string Canonical);
}

internal static class HealSettingsCorpus
{
    internal sealed record Document(string Name, string Input);

    internal static IEnumerable<Document> Documents()
    {
        foreach (var asset in GoldenTestCases.Assets)
        foreach (var settings in asset.SettingsCases)
            yield return FromSettings($"GoldenTestCases/{asset.Slug}/{settings.Slug}",
                settings.CreateSettings());
        foreach (var term in new[] { "Vertical", "Horizontal", "Aspect", "Distortion" })
        foreach (var value in new[] { -100, -50, 50, 100 })
        {
            var geometry = new GeometrySettings();
            typeof(GeometrySettings).GetProperty(term)!.SetValue(geometry, value);
            yield return FromSettings($"GeometryGoldenTests/{term}/{value}",
                new EditSettings { Geometry = geometry });
        }
        yield return FromSettings("RenderSequenceGoldenTests/Settings", RenderSequenceGoldenTests.Settings());
        foreach (var tree in new[] { "Tests", "HeadlessTests" })
        {
            var root = Path.Combine(GoldenTestPaths.RepositoryRoot, tree);
            foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                         .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") &&
                                        !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
                         .Where(path => Path.GetExtension(path) is ".cs" or ".json")
                         .Where(path => !Path.GetFileName(path).StartsWith("HealSerialization"))
                         .Order(StringComparer.Ordinal))
            {
                var text = File.ReadAllText(file);
                var relative = Path.GetRelativePath(GoldenTestPaths.RepositoryRoot, file).Replace('\\', '/');
                var strings = Path.GetExtension(file) == ".json"
                    ? new[] { text } : StringValues(text);
                var ordinal = 0;
                foreach (var value in strings)
                foreach (var candidate in Candidates(value))
                {
                    var name = $"{relative}/document-{++ordinal}";
                    // Invalid and unsupported-version negative fixtures have no canonical output.
                    try { EditSettingsJson.Deserialize(candidate, out _); }
                    catch (JsonException) { continue; }
                    yield return new(name, candidate);
                }
            }
        }
    }

    private static Document FromSettings(string name, EditSettings settings) =>
        new(name, EditSettingsJson.Serialize(settings));

    private static IEnumerable<string> StringValues(string source)
    {
        // Consume raw strings first so their embedded quotation marks aren't
        // mistaken for ordinary literals. Join adjacent constant string terms.
        var pattern = """"
            """(?<raw>[\s\S]*?)"""|@"(?<verbatim>(?:[^"]|"")*)"|(?<normal>"(?:\\.|[^"\\\r\n])*"(?:\s*\+\s*"(?:\\.|[^"\\\r\n])*")*)
            """";
        foreach (Match match in Regex.Matches(source, pattern))
        {
            if (match.Groups["raw"].Success)
                yield return match.Groups["raw"].Value;
            else if (match.Groups["verbatim"].Success)
                yield return match.Groups["verbatim"].Value.Replace("\"\"", "\"");
            else
            {
                if (!match.Value.Contains("version")) continue;
                var value = new StringBuilder();
                foreach (Match literal in Regex.Matches(match.Value, "\"(?:\\\\.|[^\"\\\\])*\""))
                    value.Append(Regex.Unescape(literal.Value[1..^1]));
                yield return value.ToString();
            }
        }
    }

    private static IEnumerable<string> Candidates(string value)
    {
        // Includes settings nested in preset fixtures and pipe-delimited goldens.
        for (var start = 0; start < value.Length; start++)
        {
            if (value[start] != '{') continue;
            JsonDocument? document = null;
            try
            {
                var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(value[start..]));
                document = JsonDocument.ParseValue(ref reader);
            }
            catch (JsonException) { }
            if (document == null) continue;
            using (document)
            {
                var root = document.RootElement;
                if (root.TryGetProperty("version", out var version) &&
                    version.TryGetInt32(out var number) &&
                    // Exclude preset envelopes and other versioned manifests.
                    !root.TryGetProperty("settings", out _) &&
                    (number is 3 or 4 || root.TryGetProperty("exposure", out _)))
                    yield return root.GetRawText();
            }
        }
    }
}





