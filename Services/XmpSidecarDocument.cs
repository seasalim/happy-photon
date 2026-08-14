using System.Xml.Linq;
using HappyPhoton.Models;

namespace HappyPhoton.Services;

public readonly record struct XmpMergeResult(bool ReplacedUnsupportedLabel);

public static class XmpSidecarDocument
{
    public const string HappyPhotonNamespaceUri =
        "http://happyphoton.app/xmp/1.0/";
    internal static readonly XNamespace Xmp = "http://ns.adobe.com/xap/1.0/";
    internal static readonly XNamespace Rdf =
        "http://www.w3.org/1999/02/22-rdf-syntax-ns#";
    internal static readonly XNamespace HappyPhoton = HappyPhotonNamespaceUri;

    public static XDocument Create() => new(
        new XElement(XNamespace.Get("adobe:ns:meta/") + "xmpmeta",
            new XAttribute(XNamespace.Xmlns + "x", "adobe:ns:meta/"),
            new XElement(Rdf + "RDF",
                new XAttribute(XNamespace.Xmlns + "rdf", Rdf.NamespaceName),
                new XElement(Rdf + "Description",
                    new XAttribute(Rdf + "about", string.Empty),
                    new XAttribute(XNamespace.Xmlns + "xmp", Xmp.NamespaceName),
                    new XAttribute(
                        XNamespace.Xmlns + "happyphoton",
                        HappyPhoton.NamespaceName)))));

    public static XmpSidecarFacts ReadFacts(
        XDocument document,
        IReadOnlyDictionary<ColorLabel, string> labelNames)
    {
        var ratingText = ReadValue(document, Xmp, "Rating");
        var privateRatingText = ReadValue(document, HappyPhoton, "Rating");
        var privateFlag = ReadValue(document, HappyPhoton, "Flag");
        var labelText = ReadValue(document, Xmp, "Label");
        var declaredAxes = ParseAxes(
            ReadValue(document, HappyPhoton, "Axes"));

        var rating = ParseRating(ratingText == "-1"
            ? privateRatingText
            : ratingText);
        var flag = ParseFlag(ratingText, privateFlag, declaredAxes);
        var label = labelText == null && declaredAxes.HasFlag(AssessmentAxes.Label)
            ? XmpFact<ColorLabel>.Empty
            : ParseLabel(labelText, labelNames);
        return new XmpSidecarFacts(rating, flag, label);
    }

    public static XmpMergeResult Merge(
        XDocument document,
        AssessmentSnapshot snapshot,
        AssessmentAxes axes,
        IReadOnlyDictionary<ColorLabel, string> labelNames)
    {
        if (axes.HasFlag(AssessmentAxes.Rating))
            MergeRating(document, snapshot);
        if (axes.HasFlag(AssessmentAxes.Flag))
            MergeFlag(document, snapshot);
        var replacedUnsupportedLabel = axes.HasFlag(AssessmentAxes.Label) &&
            MergeLabel(document, snapshot.ColorLabel, labelNames);
        MergeAxes(document, axes);
        SetValue(document, Xmp, "MetadataDate", DateTime.UtcNow.ToString("O"));
        return new XmpMergeResult(replacedUnsupportedLabel);
    }

    internal static string? ReadValue(
        XDocument document,
        XNamespace xmlNamespace,
        string localName)
    {
        var attribute = document.Descendants()
            .Attributes()
            .FirstOrDefault(candidate =>
                candidate.Name.NamespaceName == xmlNamespace.NamespaceName &&
                candidate.Name.LocalName == localName);
        if (attribute != null) return attribute.Value;
        return document.Descendants().FirstOrDefault(candidate =>
            candidate.Name.NamespaceName == xmlNamespace.NamespaceName &&
            candidate.Name.LocalName == localName)?.Value;
    }

    private static XmpFact<int> ParseRating(string? text)
    {
        if (text == null) return XmpFact<int>.Missing;
        return int.TryParse(text.Trim(), out var rating) && rating is >= 0 and <= 5
            ? XmpFact<int>.Matched(rating)
            : XmpFact<int>.Unsupported;
    }

    private static XmpFact<ImageFlag> ParseFlag(
        string? ratingText,
        string? privateFlag,
        AssessmentAxes declaredAxes)
    {
        if (ratingText == "-1")
            return XmpFact<ImageFlag>.Matched(ImageFlag.Rejected);
        if (string.Equals(privateFlag, "Pick", StringComparison.OrdinalIgnoreCase))
            return XmpFact<ImageFlag>.Matched(ImageFlag.Picked);
        if (!string.IsNullOrWhiteSpace(privateFlag))
            return XmpFact<ImageFlag>.Unsupported;
        if (declaredAxes.HasFlag(AssessmentAxes.Flag))
            return XmpFact<ImageFlag>.Matched(ImageFlag.Unflagged);
        return ParseRating(ratingText).Kind == XmpFactKind.Matched
            ? XmpFact<ImageFlag>.WeakClear(ImageFlag.Unflagged)
            : XmpFact<ImageFlag>.Missing;
    }

    private static XmpFact<ColorLabel> ParseLabel(
        string? text,
        IReadOnlyDictionary<ColorLabel, string> labelNames)
    {
        if (text == null) return XmpFact<ColorLabel>.Missing;
        if (string.IsNullOrWhiteSpace(text))
            return new XmpFact<ColorLabel>(XmpFactKind.Empty, ColorLabel.None);
        foreach (var (label, name) in labelNames)
        {
            if (string.Equals(text.Trim(), name, StringComparison.OrdinalIgnoreCase))
                return XmpFact<ColorLabel>.Matched(label);
        }
        return XmpFact<ColorLabel>.Unsupported;
    }

    private static void MergeRating(
        XDocument document,
        AssessmentSnapshot snapshot)
    {
        if (ReadValue(document, Xmp, "Rating") == "-1" ||
            snapshot.Flag == ImageFlag.Rejected)
        {
            SetValue(document, HappyPhoton, "Rating", snapshot.Rating.ToString());
            return;
        }
        SetValue(document, Xmp, "Rating", snapshot.Rating.ToString());
        RemoveValue(document, HappyPhoton, "Rating");
    }

    private static void MergeFlag(
        XDocument document,
        AssessmentSnapshot snapshot)
    {
        if (snapshot.Flag == ImageFlag.Rejected)
        {
            SetValue(document, HappyPhoton, "Rating", snapshot.Rating.ToString());
            SetValue(document, Xmp, "Rating", "-1");
            RemoveValue(document, HappyPhoton, "Flag");
            return;
        }

        if (ReadValue(document, Xmp, "Rating") == "-1")
        {
            var restored = ParseRating(ReadValue(document, HappyPhoton, "Rating"));
            SetValue(document, Xmp, "Rating",
                (restored.Kind == XmpFactKind.Matched
                    ? restored.Value
                    : snapshot.Rating).ToString());
        }
        RemoveValue(document, HappyPhoton, "Rating");
        if (snapshot.Flag == ImageFlag.Picked)
            SetValue(document, HappyPhoton, "Flag", "Pick");
        else
            RemoveValue(document, HappyPhoton, "Flag");
    }

    private static bool MergeLabel(
        XDocument document,
        ColorLabel label,
        IReadOnlyDictionary<ColorLabel, string> labelNames)
    {
        var existing = ReadValue(document, Xmp, "Label");
        var unsupported = existing != null &&
            ParseLabel(existing, labelNames).Kind == XmpFactKind.Unsupported;
        if (label == ColorLabel.None)
        {
            if (!unsupported) RemoveValue(document, Xmp, "Label");
            return false;
        }
        var value = labelNames.GetValueOrDefault(label, label.ToString());
        SetValue(document, Xmp, "Label", value);
        return unsupported;
    }

    private static AssessmentAxes ParseAxes(string? text)
    {
        var axes = AssessmentAxes.None;
        foreach (var token in SplitAxes(text))
        {
            if (string.Equals(token, "rating", StringComparison.OrdinalIgnoreCase))
                axes |= AssessmentAxes.Rating;
            else if (string.Equals(token, "flag", StringComparison.OrdinalIgnoreCase))
                axes |= AssessmentAxes.Flag;
            else if (string.Equals(token, "label", StringComparison.OrdinalIgnoreCase))
                axes |= AssessmentAxes.Label;
        }
        return axes;
    }

    private static void MergeAxes(XDocument document, AssessmentAxes axes)
    {
        var tokens = SplitAxes(ReadValue(document, HappyPhoton, "Axes")).ToList();
        AddAxisToken(tokens, axes, AssessmentAxes.Rating, "rating");
        AddAxisToken(tokens, axes, AssessmentAxes.Flag, "flag");
        AddAxisToken(tokens, axes, AssessmentAxes.Label, "label");
        SetValue(document, HappyPhoton, "Axes", string.Join(",", tokens));
    }

    private static IEnumerable<string> SplitAxes(string? text) =>
        (text ?? string.Empty).Split(',', StringSplitOptions.TrimEntries |
            StringSplitOptions.RemoveEmptyEntries);

    private static void AddAxisToken(
        ICollection<string> tokens,
        AssessmentAxes axes,
        AssessmentAxes axis,
        string token)
    {
        if (axes.HasFlag(axis) && !tokens.Contains(
                token, StringComparer.OrdinalIgnoreCase))
            tokens.Add(token);
    }

    private static void SetValue(
        XDocument document,
        XNamespace xmlNamespace,
        string localName,
        string value)
    {
        var name = xmlNamespace + localName;
        var attribute = document.Descendants().Attributes(name).FirstOrDefault();
        if (attribute != null)
        {
            attribute.Value = value;
            return;
        }
        var element = document.Descendants(name).FirstOrDefault();
        if (element != null)
        {
            element.Value = value;
            return;
        }
        Description(document).SetAttributeValue(name, value);
    }

    private static void RemoveValue(
        XDocument document,
        XNamespace xmlNamespace,
        string localName)
    {
        var name = xmlNamespace + localName;
        document.Descendants().Attributes(name).Remove();
        document.Descendants(name).Remove();
    }

    private static XElement Description(XDocument document)
    {
        var description = document.Descendants(Rdf + "Description").FirstOrDefault();
        if (description != null) return description;
        var rdf = document.Descendants(Rdf + "RDF").FirstOrDefault();
        if (rdf == null)
        {
            var replacement = Create();
            document.RemoveNodes();
            document.Add(replacement.Root!);
            return document.Descendants(Rdf + "Description").Single();
        }
        description = new XElement(Rdf + "Description",
            new XAttribute(Rdf + "about", string.Empty));
        rdf.Add(description);
        return description;
    }
}
