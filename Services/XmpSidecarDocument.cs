using System.Xml.Linq;
using HappyPhoton.Models;

namespace HappyPhoton.Services;

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

        var rating = ParseRating(ratingText == "-1"
            ? privateRatingText
            : ratingText);
        var flag = ratingText == "-1"
            ? XmpFact<ImageFlag>.Matched(ImageFlag.Rejected)
            : string.Equals(privateFlag, "Pick", StringComparison.OrdinalIgnoreCase)
                ? XmpFact<ImageFlag>.Matched(ImageFlag.Picked)
                : string.IsNullOrWhiteSpace(privateFlag)
                    ? XmpFact<ImageFlag>.Missing
                    : XmpFact<ImageFlag>.Unsupported;
        var label = ParseLabel(labelText, labelNames);
        return new XmpSidecarFacts(rating, flag, label);
    }

    public static void Merge(
        XDocument document,
        AssessmentSnapshot snapshot,
        AssessmentAxes axes,
        IReadOnlyDictionary<ColorLabel, string> labelNames)
    {
        if (axes.HasFlag(AssessmentAxes.Rating))
            MergeRating(document, snapshot);
        if (axes.HasFlag(AssessmentAxes.Flag))
            MergeFlag(document, snapshot);
        if (axes.HasFlag(AssessmentAxes.Label))
            MergeLabel(document, snapshot.ColorLabel, labelNames);
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

    private static void MergeLabel(
        XDocument document,
        ColorLabel label,
        IReadOnlyDictionary<ColorLabel, string> labelNames)
    {
        var existing = ReadValue(document, Xmp, "Label");
        if (existing != null &&
            ParseLabel(existing, labelNames).Kind == XmpFactKind.Unsupported)
        {
            return;
        }
        var value = label == ColorLabel.None
            ? string.Empty
            : labelNames.GetValueOrDefault(label, label.ToString());
        SetValue(document, Xmp, "Label", value);
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
