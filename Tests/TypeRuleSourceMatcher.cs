using System.Globalization;
using System.Text.RegularExpressions;

namespace HappyPhoton.Tests;

internal static class TypeRuleSourceMatcher
{
    private const string Number = @"[+-]?(?:\d+(?:\.\d*)?|\.\d+)(?:[eE][+-]?\d+)?";

    private static readonly Regex XamlAssignment = new(
        """\b(?<property>FontSize|LetterSpacing)\s*=\s*["']\s*(?<value>""" + Number + """)\s*["']""",
        RegexOptions.CultureInvariant);

    private static readonly Regex Setter = new(
        @"<Setter\s[^>]*>(?:\s*<Setter\.Value>[^<]*</Setter\.Value>\s*</Setter>)?",
        RegexOptions.CultureInvariant);

    private static readonly Regex SetterProperty = new(
        """\bProperty\s*=\s*["'](?:[\w:]+\.)?(?<property>FontSize|LetterSpacing)["']""",
        RegexOptions.CultureInvariant);

    private static readonly Regex SetterValue = new(
        """\bValue\s*=\s*["']\s*(?<value>""" + Number + """)\s*["']""",
        RegexOptions.CultureInvariant);

    private static readonly Regex SetterElementValue = new(
        @"<Setter\.Value>\s*(?<value>" + Number + @")\s*</Setter\.Value>",
        RegexOptions.CultureInvariant);

    private static readonly Regex CSharpAssignment = new(
        @"\b(?<property>FontSize|LetterSpacing)\s*=\s*(?<value>" + Number + @")[dDfFmM]?(?![\w.])",
        RegexOptions.CultureInvariant);

    internal static IReadOnlyList<string> Find(string source, bool isCSharp)
    {
        var offences = new List<string>();
        var assignments = isCSharp ? CSharpAssignment : XamlAssignment;

        foreach (Match match in assignments.Matches(source))
        {
            if (IsOffence(match.Groups["property"].Value, match.Groups["value"].Value))
            {
                offences.Add(match.Value);
            }
        }

        if (!isCSharp)
        {
            foreach (Match setter in Setter.Matches(source))
            {
                var property = SetterProperty.Match(setter.Value);
                var value = SetterValue.Match(setter.Value);

                if (!value.Success)
                {
                    value = SetterElementValue.Match(setter.Value);
                }

                if (property.Success && value.Success &&
                    IsOffence(property.Groups["property"].Value, value.Groups["value"].Value))
                {
                    offences.Add(setter.Value);
                }
            }
        }

        return offences;
    }

    private static bool IsOffence(string property, string text)
    {
        var value = double.Parse(text, CultureInfo.InvariantCulture);
        // All numeric sizes are offences, including those below FontSizeSmall (10).

        return property == "FontSize" || property == "LetterSpacing" && value > 0;
    }
}
