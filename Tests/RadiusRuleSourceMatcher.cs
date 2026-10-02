using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace HappyPhoton.Tests;

internal static partial class RadiusRuleSourceMatcher
{
    internal static IReadOnlyList<string> Find(string source, bool isCSharp)
    {
        if (isCSharp) return FindCSharp(source);

        var root = XElement.Parse("<Root>" + source + "</Root>", LoadOptions.PreserveWhitespace);
        var violations = new List<string>();

        foreach (var element in root.Descendants())
        {
            if (IsSemanticCircle(element)) continue;

            foreach (var attribute in element.Attributes())
            {
                var name = attribute.Name.LocalName;

                if (element.Name.LocalName == "RadialGradientBrush" && name is "RadiusX" or "RadiusY") continue;

                if (element.Name.LocalName == "CornerRadius" && name == "Key" ||
                    element.Name.LocalName == "Setter" && name == "Value" &&
                    IsRadiusProperty(element.Attribute("Property")?.Value ?? ""))
                {
                    continue;
                }

                if (IsRadiusProperty(name) && IsOffence(attribute.Value) ||
                    !IsRadiusProperty(name) && TokenPattern().IsMatch(attribute.Value))
                {
                    violations.Add(element.Name.LocalName + " " + attribute);
                }
            }

            if (element.Name.LocalName == "Setter" &&
                IsRadiusProperty(element.Attribute("Property")?.Value ?? ""))
            {
                var value = element.Attribute("Value")?.Value ??
                    element.Elements().FirstOrDefault(child => child.Name.LocalName == "Setter.Value")?.Value;

                if (value != null && IsOffence(value))
                {
                    violations.Add(element.ToString(SaveOptions.DisableFormatting));
                }
            }

            if ((IsRadiusProperty(element.Name.LocalName) && IsOffence(element.Value)) ||
                element.Name.LocalName == "CornerRadius" &&
                element.Attributes().Any(attribute => attribute.Name.LocalName == "Key" &&
                    TokenPattern().IsMatch(attribute.Value)))
            {
                // Resource definitions count once, including the token key.
                violations.Add(element.ToString(SaveOptions.DisableFormatting));
            }
        }

        return violations;
    }

    private static bool IsSemanticCircle(XElement element)
    {
        if (element.AncestorsAndSelf().Any(candidate =>
            candidate.Name.LocalName == "Style" &&
            MixerSelectorPattern().IsMatch(candidate.Attribute("Selector")?.Value ?? "")))
        {
            return true;
        }

        if (element.Name.LocalName == "Button" &&
            (element.Attribute("Classes")?.Value ?? "").Split(' ').Contains("mixer-band"))
        {
            return true;
        }

        return element.Name.LocalName == "Border" && element.Parent?.Name.LocalName == "Button" &&
            (element.Parent.Attribute("Classes")?.Value ?? "").Split(' ').Contains("mixer-band");
    }

    private static bool IsRadiusProperty(string name) =>
        name.Split('.').Last() is "CornerRadius" or "RadiusX" or "RadiusY";

    private static bool IsOffence(string value)
    {
        if (TokenPattern().IsMatch(value)) return true;

        // Avalonia accepts commas and whitespace between corner values.
        var parts = value.Split([',', ' ', '	'], StringSplitOptions.RemoveEmptyEntries);

        return parts.All(part => double.TryParse(part.Trim(), NumberStyles.Float,
                   CultureInfo.InvariantCulture, out _)) &&
               parts.Any(part => double.Parse(part.Trim(), CultureInfo.InvariantCulture) != 0);
    }

    private static IReadOnlyList<string> FindCSharp(string source)
    {
        var withoutComments = Regex.Replace(source, @"//[^\r\n]*|/\*[\s\S]*?\*/", "");
        // Gradient axes describe mask geometry, not the corners of a control.
        var corners = Regex.Replace(withoutComments,
            @"\bnew\s+RadialGradientBrush(?:\s*\(\s*\))?\s*\{[^{}]*\}",
            brush => Regex.Replace(brush.Value, @"\bRadius[XY]\b", "GradientAxis"));
        var violations = AssignmentPattern().Matches(corners)
            .Where(match => IsOffence(match.Groups[1].Value))
            .Select(match => match.Value.Trim()).ToList();
        violations.AddRange(Regex.Matches(withoutComments, "\"Radius\\w*\"").Select(match => match.Value));

        return violations;
    }

    [GeneratedRegex(@"\bRadius(?![XY]\b)\w*\b")]
    private static partial Regex TokenPattern();

    [GeneratedRegex(@"^Button\.mixer-band(?:[.:#][\w-]+)*(?:\s*/template/\s*ContentPresenter#PART_ContentPresenter)?$")]
    private static partial Regex MixerSelectorPattern();

    [GeneratedRegex(@"\b(?:CornerRadius|RadiusX|RadiusY)\s*=\s*(?:new\s*(?:CornerRadius|RelativeScalar)?\s*\(\s*)?(-?\d+(?:\.\d+)?(?:\s*,\s*-?\d+(?:\.\d+)?)*)")]
    private static partial Regex AssignmentPattern();
}
