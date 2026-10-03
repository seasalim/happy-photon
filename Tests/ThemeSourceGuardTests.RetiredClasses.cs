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
    [InlineData("<Button Classes=\"root-override\"/>")]
    [InlineData("<Button Classes=\"wizard-primary accent\"/>")]
    [InlineData("<Style Selector=\"Button.wizard-quiet\"/>")]
    public void RetiredClassMatcher_RejectsBrowseClasses(string text)
    {
        Assert.Matches(RetiredClassPattern(), text);
    }

    [Fact]
    public void RetiredClassMatcher_AcceptsSharedClasses()
    {
        Assert.DoesNotMatch(RetiredClassPattern(), "<Button Classes=\"icon-button compact\"/>");
    }

    [Fact]
    public void G6_TourIdentifiersAreRetired()
    {
        var root = FindRepositoryRoot();
        var sources = new[] { "Views", "ViewModels", "Themes" }
            .SelectMany(folder => SourceFiles(Path.Combine(root, folder)));
        var violations = sources.SelectMany(file => File.ReadLines(file)
            .Where(line => RetiredTourPattern().IsMatch(line))
            .Select(line => $"{Path.GetRelativePath(root, file)}: {line}"));

        Assert.Empty(violations);
    }

    [GeneratedRegex(@"WorkflowCoachmark|WorkflowTour|tour-region|tour-dimmed|tour-focus|tour-glow|TourArrow|Coachmark\w*|x:Key=""Tour\w*|SuppressEmptyState")]
    private static partial Regex RetiredTourPattern();

    [GeneratedRegex(@"(?<![\w-])(?:wb-auto|develop-reset|develop-action|view-toggle|thumbnail-size|check-badge|root-override|wizard-primary|wizard-quiet)(?![\w-])")]
    private static partial Regex RetiredClassPattern();
}
