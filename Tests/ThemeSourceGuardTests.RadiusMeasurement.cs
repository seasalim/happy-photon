using Xunit;

namespace HappyPhoton.Tests;

public sealed partial class ThemeSourceGuardTests
{
    [Fact]
    public void ObserveRadiusRuleBaseline()
    {
        var root = FindRepositoryRoot();
        var files = SourceFiles(Path.Combine(root, "Views"))
            .Append(Path.Combine(root, "App.axaml"))
            .Concat(SourceFiles(Path.Combine(root, "Themes")))
            .Order(StringComparer.Ordinal);
        var offences = files.SelectMany(file =>
                RadiusRuleSourceMatcher.Find(File.ReadAllText(file), file.EndsWith(".cs"))
                    .Select(text => $"{Path.GetRelativePath(root, file)} | {text}"))
            .ToArray();
        var output = TestContext.Current.TestOutputHelper!;
        output.WriteLine($"G2 radiusOffences={offences.Length}");
        output.WriteLine("Allowlist: Button.mixer-band styles/buttons and their direct swatch Border; " +
            "edits dots, clipping dots and curve points use Ellipse/drawing geometry, not radius properties.");

        foreach (var offence in offences)
        {
            output.WriteLine(offence);
        }

        Assert.Empty(offences);
    }

    [Theory]
    [InlineData("<Border CornerRadius='4'/>", false, 1)]
    [InlineData("<Border CornerRadius='0'/>", false, 0)]
    [InlineData("<Border CornerRadius='0,1,0,0'/>", false, 1)]
    [InlineData("<Border CornerRadius='4 4 4 4'/>", false, 1)]
    [InlineData("<Border CornerRadius='0 0 0 0'/>", false, 0)]
    [InlineData("<Border CornerRadius='{StaticResource RadiusSmall}'/>", false, 1)]
    [InlineData("<Setter Property='CornerRadius' Value='3'/>", false, 1)]
    [InlineData("<Setter Property='Border.CornerRadius'><Setter.Value>8</Setter.Value></Setter>", false, 1)]
    [InlineData("<RadialGradientBrush RadiusX='0.5' RadiusY='0.5'/>", false, 0)]
    [InlineData("<Rectangle RadiusX='3' RadiusY='3'/>", false, 2)]
    [InlineData("CornerRadius = new(2);", true, 1)]
    [InlineData("CornerRadius = new CornerRadius(0);", true, 0)]
    [InlineData("RadiusX = new RelativeScalar(1, RelativeUnit.Absolute);", true, 1)]
    [InlineData("new RadialGradientBrush { RadiusX = new RelativeScalar(1, RelativeUnit.Absolute), RadiusY = new(1) };", true, 0)]
    [InlineData("new RadialGradientBrush { RadiusX = new(1) }; CornerRadius = new(4);", true, 1)]
    [InlineData("FindResource(\"RadiusReplacement\")", true, 1)]
    [InlineData("<Border CornerRadius='{StaticResource RadiusReplacement}'/>", false, 1)]
    [InlineData("<Button Classes='mixer-band'><Border CornerRadius='9'/></Button>", false, 0)]
    [InlineData("<Style Selector='Button.mixer-band'><Setter Property='CornerRadius' Value='12'/></Style>", false, 0)]
    [InlineData("<Button Classes='swatch'><Border CornerRadius='9'/></Button>", false, 1)]
    [InlineData("<Style Selector='Button.mixer-band + Border'><Setter Property='CornerRadius' Value='4'/></Style>", false, 1)]
    [InlineData("<CornerRadius xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' x:Key='RadiusSmall'>0</CornerRadius>", false, 1)]
    [InlineData("<Border.CornerRadius>4</Border.CornerRadius>", false, 1)]
    public void RadiusMatcherRejectsAddedRadiusAndAllowsSemanticCircle(
        string source, bool isCSharp, int expected)
    {
        Assert.Equal(expected, RadiusRuleSourceMatcher.Find(source, isCSharp).Count);
    }
}
