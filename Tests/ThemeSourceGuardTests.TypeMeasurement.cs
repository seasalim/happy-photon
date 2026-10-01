using System.Text.Json;
using Xunit;

namespace HappyPhoton.Tests;

public sealed partial class ThemeSourceGuardTests
{
    [Fact]
    public void ObserveTypeRuleBaseline()
    {
        var root = FindRepositoryRoot();
        var files = SourceFiles(Path.Combine(root, "Views"))
            .Append(Path.Combine(root, "App.axaml"));
        var entries = files.SelectMany(file =>
                TypeRuleSourceMatcher.Find(File.ReadAllText(file), file.EndsWith(".cs"))
                    .GroupBy(text => text, StringComparer.Ordinal)
                    .Select(group => new TypeBaselineEntry(
                        Path.GetRelativePath(root, file).Replace(Path.DirectorySeparatorChar, '/'),
                        group.Key, group.Count())))
            .OrderBy(entry => entry.Path, StringComparer.Ordinal)
            .ThenBy(entry => entry.Text, StringComparer.Ordinal)
            .ToArray();
        var output = TestContext.Current.TestOutputHelper!;
        output.WriteLine($"G3 offences={entries.Sum(entry => entry.Count)} " +
            $"files={entries.Select(entry => entry.Path).Distinct().Count()} " +
            $"App.axaml={entries.Where(entry => entry.Path == "App.axaml").Sum(entry => entry.Count)}");

        var baseline = JsonSerializer.Deserialize<TypeBaselineEntry[]>(
            File.ReadAllText(Path.Combine(root, "Tests", "TypeRuleBaseline.json")))!;

        foreach (var entry in baseline)
        {
            var count = entries.SingleOrDefault(current =>
                current.Path == entry.Path && current.Text == entry.Text)?.Count ?? 0;

            if (count < entry.Count)
            {
                output.WriteLine($"Lower frozen entry: {entry.Path} | {entry.Text} | {entry.Count} -> {count}");
            }
        }

        Assert.Empty(FindIncreases(entries, baseline));
    }

    [Theory]
    [InlineData("<TextBlock FontSize=\"9\" LetterSpacing=\"0.5\"/>", false, 2)]
    [InlineData("<Setter Property=\"FontSize\" Value=\"9\"/>", false, 1)]
    [InlineData("<Setter Value=\"1\" Property=\"LetterSpacing\"/>", false, 1)]
    [InlineData("<Setter Property=\"TextBlock.FontSize\" Value=\"9\"/>", false, 1)]
    [InlineData("<Setter Value='1' Property='TextBlock.LetterSpacing'/>", false, 1)]
    [InlineData("<Setter Property='local:Owner.FontSize' Value='11'/>", false, 1)]
    [InlineData("<Setter Property='Owner.LetterSpacing' Value='.5'/>", false, 1)]
    [InlineData("<Setter Property='FontSize'><Setter.Value>9</Setter.Value></Setter>", false, 1)]
    [InlineData("<Setter Property='LetterSpacing'><Setter.Value>1</Setter.Value></Setter>", false, 1)]
    [InlineData("<Setter Property='Owner.FontSize'>\n<Setter.Value> 11 </Setter.Value>\n</Setter>", false, 1)]
    [InlineData("<Setter Property='local:Owner.LetterSpacing'><Setter.Value>.5</Setter.Value></Setter>", false, 1)]
    [InlineData("<Setter Property='TextBlock.FontSize' Value='{StaticResource FontSizeSmall}'/>", false, 0)]
    [InlineData("<Setter Property='FontSize'><Setter.Value>{StaticResource FontSizeSmall}</Setter.Value></Setter>", false, 0)]
    [InlineData("<Setter Property='TextBlock.LetterSpacing' Value='0'/>", false, 0)]
    [InlineData("<Setter Property='LetterSpacing'><Setter.Value>0</Setter.Value></Setter>", false, 0)]
    [InlineData("<Setter Property='Owner.LetterSpacing'><Setter.Value>-1</Setter.Value></Setter>", false, 0)]
    [InlineData("<Setter Property='FontSize' Value='{StaticResource FontSizeSmall}'/><Setter Property='Width'><Setter.Value>9</Setter.Value></Setter>", false, 0)]
    [InlineData("FontSize = 14; LetterSpacing = .5;", true, 2)]
    [InlineData("<TextBlock FontSize=\"{StaticResource FontSizeSmall}\" LetterSpacing=\"0\"/>", false, 0)]
    [InlineData("LetterSpacing = -1; FontSize = size;", true, 0)]
    public void TypeMatcherRecognizesSourceForms(string source, bool isCSharp, int count)
    {
        Assert.Equal(count, TypeRuleSourceMatcher.Find(source, isCSharp).Count);
    }

    [Theory]
    [InlineData("<TextBlock FontSize=\"11\"/>", false)]
    [InlineData("<TextBlock FontSize=\"12\"/>", false)]
    [InlineData("<Setter Property=\"TextBlock.FontSize\" Value=\"9\"/>", false)]
    [InlineData("<Setter Value='1' Property='TextBlock.LetterSpacing'/>", false)]
    [InlineData("<Setter Property='local:Owner.FontSize' Value='11'/>", false)]
    [InlineData("<Setter Property='Owner.LetterSpacing' Value='.5'/>", false)]
    [InlineData("<Setter Property='FontSize'><Setter.Value>9</Setter.Value></Setter>", false)]
    [InlineData("<Setter Property='LetterSpacing'><Setter.Value>1</Setter.Value></Setter>", false)]
    [InlineData("<Setter Property='Owner.FontSize'>\n<Setter.Value> 11 </Setter.Value>\n</Setter>", false)]
    [InlineData("<Setter Property='local:Owner.LetterSpacing'><Setter.Value>.5</Setter.Value></Setter>", false)]
    [InlineData("", true)]
    public void TypeBaselineRejectsAdditionsAndAllowsRemoval(string addition, bool passes)
    {
        const string original = "<TextBlock FontSize=\"11\"/><TextBlock FontSize=\"11\"/>";
        var baseline = CountSynthetic(original);
        var changed = passes ? "<TextBlock FontSize=\"11\"/>" : original + addition;

        Assert.Equal(passes, FindIncreases(CountSynthetic(changed), baseline).Length == 0);
    }

    private static TypeBaselineEntry[] CountSynthetic(string source) =>
        TypeRuleSourceMatcher.Find(source, isCSharp: false)
            .GroupBy(text => text, StringComparer.Ordinal)
            .Select(group => new TypeBaselineEntry("Views/Existing.axaml", group.Key, group.Count()))
            .ToArray();

    private static string[] FindIncreases(
        IEnumerable<TypeBaselineEntry> current, IEnumerable<TypeBaselineEntry> baseline)
    {
        var frozen = baseline.ToDictionary(entry => (entry.Path, entry.Text), entry => entry.Count);

        return current.Where(entry => entry.Count > frozen.GetValueOrDefault((entry.Path, entry.Text)))
            .Select(entry => $"{entry.Path} | {entry.Text} | " +
                $"{frozen.GetValueOrDefault((entry.Path, entry.Text))} -> {entry.Count}")
            .ToArray();
    }

    private sealed record TypeBaselineEntry(string Path, string Text, int Count);
}
