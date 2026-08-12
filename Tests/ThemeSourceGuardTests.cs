using System.Text.RegularExpressions;
using Xunit;

namespace HappyPhoton.Tests;

public sealed partial class ThemeSourceGuardTests
{
    [Fact]
    public void AppUiSources_DoNotContainHardcodedColors()
    {
        var root = FindRepositoryRoot();
        var files = new[]
            {
                Path.Combine(root, "App.axaml")
            }
            .Concat(SourceFiles(Path.Combine(root, "Views")))
            .Concat(SourceFiles(Path.Combine(root, "Converters")))
            .Concat(SourceFiles(Path.Combine(root, "ViewModels")))
            .Where(path => !path.EndsWith(
                Path.Combine("Views", "HappyPhotonColors.cs"),
                StringComparison.OrdinalIgnoreCase));

        var violations = new List<string>();
        foreach (var file in files)
        {
            var text = File.ReadAllText(file);
            foreach (Match match in ColorLiteralPattern().Matches(text))
            {
                if (IsTransparent(match.Value))
                {
                    continue;
                }

                var line = text.AsSpan(0, match.Index).Count('\n') + 1;
                violations.Add(
                    $"{Path.GetRelativePath(root, file)}:{line}: {match.Value}");
            }
        }

        Assert.Empty(violations);
    }

    private static IEnumerable<string> SourceFiles(string directory) =>
        Directory.EnumerateFiles(directory, "*.*", SearchOption.AllDirectories)
            .Where(path => path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ||
                           path.EndsWith(".axaml", StringComparison.OrdinalIgnoreCase));

    private static bool IsTransparent(string value) =>
        value.Equals("Transparent", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("Brushes.Transparent", StringComparison.Ordinal) ||
        value.StartsWith("#00", StringComparison.OrdinalIgnoreCase);

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current != null &&
               !File.Exists(Path.Combine(current.FullName, "HappyPhoton.sln")))
        {
            current = current.Parent;
        }

        return current?.FullName ?? throw new DirectoryNotFoundException(
            "Could not locate the Happy Photon repository root.");
    }

    [GeneratedRegex(
        @"#[0-9a-fA-F]{3,8}\b|\b(?:Colors|Brushes)\.(?!Transparent\b)[A-Za-z]+|Color\.(?:FromArgb|FromRgb|Parse)\s*\(")]
    private static partial Regex ColorLiteralPattern();
}
