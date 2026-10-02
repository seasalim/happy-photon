using System.Text.RegularExpressions;
using Xunit;

namespace HappyPhoton.Tests;

public sealed partial class ThemeSourceGuardTests
{
    [Fact]
    public void SharedControlClasses_DoNotUseRetiredNames()
    {
        var root = FindRepositoryRoot();
        var sources = SourceFiles(Path.Combine(root, "Views"))
            .Concat(SourceFiles(Path.Combine(root, "Themes")))
            .Append(Path.Combine(root, "App.axaml"));
        var violations = sources.SelectMany(file => RetiredClassPattern().Matches(File.ReadAllText(file))
            .Select(match => $"{Path.GetRelativePath(root, file)}: {match.Value}"));

        Assert.Empty(violations);
    }

    [Theory]
    [InlineData("<Button Classes=\"wb-auto\"/>")]
    [InlineData("<Button Classes=\"develop-reset accent\"/>")]
    [InlineData("<Style Selector=\"ToggleButton.develop-action:checked\"/>")]
    public void RetiredClassMatcher_RejectsRetiredClass(string text)
    {
        Assert.Matches(RetiredClassPattern(), text);
    }

    [Theory]
    [InlineData("<Button Classes=\"view-toggle\"/>")]
    [InlineData("<Style Selector=\"RadioButton.thumbnail-size\"/>")]
    [InlineData("<Style Selector=\"Button.check-badge.selected\"/>")]
    public void RetiredClassMatcher_RejectsBrowseClasses(string text)
    {
        Assert.Matches(RetiredClassPattern(), text);
    }

    [Fact]
    public void RetiredClassMatcher_AcceptsSharedClasses()
    {
        Assert.DoesNotMatch(RetiredClassPattern(), "<Button Classes=\"icon-button compact\"/>");
    }

    [GeneratedRegex(@"(?<![\w-])(?:wb-auto|develop-reset|develop-action|view-toggle|thumbnail-size|check-badge)(?![\w-])")]
    private static partial Regex RetiredClassPattern();
}
