using System.Xml.Linq;
using HappyPhoton.Models;

namespace HappyPhoton.Services;

public readonly record struct XmpMergeResult(bool ReplacedUnsupportedLabel);

public static class XmpSidecarDocument
{
    public const string XmpDynamicMediaNamespaceUri =
        "http://ns.adobe.com/xmp/1.0/DynamicMedia/";
    internal static readonly XNamespace Xmp = "http://ns.adobe.com/xap/1.0/";
    internal static readonly XNamespace Rdf =
        "http://www.w3.org/1999/02/22-rdf-syntax-ns#";
    private static readonly XNamespace HappyPhoton =
        "http://happyphoton.app/xmp/1.0/";
    internal static readonly XNamespace XmpDynamicMedia =
        XmpDynamicMediaNamespaceUri;

    public static XDocument Create() => new(
        new XElement(XNamespace.Get("adobe:ns:meta/") + "xmpmeta",
            new XAttribute(XNamespace.Xmlns + "x", "adobe:ns:meta/"),
            new XElement(Rdf + "RDF",
                new XAttribute(XNamespace.Xmlns + "rdf", Rdf.NamespaceName),
                new XElement(Rdf + "Description",
                    new XAttribute(Rdf + "about", string.Empty),
                    new XAttribute(XNamespace.Xmlns + "xmp", Xmp.NamespaceName),
                    new XAttribute(
                        XNamespace.Xmlns + "xmpDM",
                        XmpDynamicMedia.NamespaceName)))));

    public static XmpSidecarFacts ReadFacts(
        XDocument document,
        IReadOnlyDictionary<ColorLabel, string> labelNames)
    {
        var ratingText = ReadValue(document, Xmp, "Rating");
        var pickText = ReadValue(document, XmpDynamicMedia, "pick");
        var labelText = ReadValue(document, Xmp, "Label");

        var rating = ratingText == "-1"
            ? XmpFact<int>.Missing
            : ParseRating(ratingText);
        var flag = ParseFlag(ratingText, pickText);
        var label = ParseLabel(labelText, labelNames);
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
        string? pickText)
    {
        if (ratingText == "-1")
            return XmpFact<ImageFlag>.Matched(ImageFlag.Rejected);
        if (pickText != null)
        {
            return pickText.Trim() switch
            {
                "-1" => XmpFact<ImageFlag>.Matched(ImageFlag.Rejected),
                "1" => XmpFact<ImageFlag>.Matched(ImageFlag.Picked),
                "0" => XmpFact<ImageFlag>.Matched(ImageFlag.Unflagged),
                _ => XmpFact<ImageFlag>.Unsupported
            };
        }
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
        SetValue(document, Xmp, "Rating", snapshot.Rating.ToString());
        RemoveValue(document, HappyPhoton, "Rating");
        if (snapshot.Flag == ImageFlag.Rejected)
            SetPick(document, ImageFlag.Rejected);
    }

    private static void MergeFlag(
        XDocument document,
        AssessmentSnapshot snapshot)
    {
        if (ReadValue(document, Xmp, "Rating") == "-1")
        {
            SetValue(document, Xmp, "Rating", snapshot.Rating.ToString());
            RemoveValue(document, HappyPhoton, "Rating");
        }
        SetPick(document, snapshot.Flag);
        RemoveValue(document, HappyPhoton, "Flag");
    }

    private static void SetPick(XDocument document, ImageFlag flag)
    {
        SetValue(document, XmpDynamicMedia, "pick", flag switch
        {
            ImageFlag.Picked => "1",
            ImageFlag.Rejected => "-1",
            _ => "0"
        });
        if (flag == ImageFlag.Unflagged)
        {
            RemoveValue(document, XmpDynamicMedia, "good");
            return;
        }
        SetValue(document, XmpDynamicMedia, "good",
            flag == ImageFlag.Picked ? "True" : "False");
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
            if (!unsupported) SetValue(document, Xmp, "Label", string.Empty);
            return false;
        }
        var value = labelNames.GetValueOrDefault(label, label.ToString());
        SetValue(document, Xmp, "Label", value);
        return unsupported;
    }

    private static void SetValue(
        XDocument document,
        XNamespace xmlNamespace,
        string localName,
        string value)
    {
        var description = Description(document);
        if (xmlNamespace == XmpDynamicMedia &&
            description.GetPrefixOfNamespace(XmpDynamicMedia) != "xmpDM")
        {
            description.SetAttributeValue(
                XNamespace.Xmlns + "xmpDM", XmpDynamicMedia.NamespaceName);
        }
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
        description.SetAttributeValue(name, value);
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
