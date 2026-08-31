using System.Xml.Linq;
using System.Globalization;
using HappyPhoton.Models;

namespace HappyPhoton.Services;

public readonly record struct XmpMergeResult(
    bool ReplacedUnsupportedLabel,
    bool ReplacedUnsupportedCrop,
    bool SkippedCrop,
    bool Changed = false,
    string? CropSkipReason = null);

public static class XmpSidecarDocument
{
    public const string XmpDynamicMediaNamespaceUri =
        "http://ns.adobe.com/xmp/1.0/DynamicMedia/";
    public const string CameraRawNamespaceUri =
        "http://ns.adobe.com/camera-raw-settings/1.0/";
    internal static readonly XNamespace Xmp = "http://ns.adobe.com/xap/1.0/";
    internal static readonly XNamespace Rdf =
        "http://www.w3.org/1999/02/22-rdf-syntax-ns#";
    private static readonly XNamespace HappyPhoton =
        "http://happyphoton.app/xmp/1.0/";
    internal static readonly XNamespace XmpDynamicMedia =
        XmpDynamicMediaNamespaceUri;
    internal static readonly XNamespace CameraRaw = CameraRawNamespaceUri;
    internal static readonly XNamespace Tiff = "http://ns.adobe.com/tiff/1.0/";

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
        IReadOnlyDictionary<ColorLabel, string> labelNames,
        int fileExifOrientation = 1)
    {
        var ratingText = ReadValue(document, Xmp, "Rating");
        var pickText = ReadValue(document, XmpDynamicMedia, "pick");
        var labelText = ReadValue(document, Xmp, "Label");

        var rating = ratingText == "-1"
            ? XmpFact<int>.Missing
            : ParseRating(ratingText);
        var flag = ParseFlag(ratingText, pickText);
        var label = ParseLabel(labelText, labelNames);
        return new XmpSidecarFacts(
            rating, flag, label, ParseCrop(document, fileExifOrientation));
    }

    public static XmpMergeResult Merge(
        XDocument document,
        AssessmentSnapshot snapshot,
        AssessmentAxes axes,
        IReadOnlyDictionary<ColorLabel, string> labelNames,
        XmpCropProjection? cropProjection = null)
    {
        var original = new XDocument(document);
        if (axes.HasFlag(AssessmentAxes.Rating))
            MergeRating(document, snapshot);
        if (axes.HasFlag(AssessmentAxes.Flag))
            MergeFlag(document, snapshot);
        var replacedUnsupportedLabel = axes.HasFlag(AssessmentAxes.Label) &&
            MergeLabel(document, snapshot.ColorLabel, labelNames);
        var cropResult = axes.HasFlag(AssessmentAxes.Crop) && cropProjection.HasValue
            ? MergeCrop(document, cropProjection.Value)
            : default;
        var changed = !XNode.DeepEquals(original, document);
        if (changed)
            SetValue(document, Xmp, "MetadataDate", DateTime.UtcNow.ToString("O"));
        return new XmpMergeResult(
            replacedUnsupportedLabel,
            cropResult.ReplacedUnsupportedCrop,
            cropResult.SkippedCrop,
            changed,
            cropResult.CropSkipReason);
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
        var trimmed = text.Trim();
        // Canonical names win over user display names so a sidecar written by
        // another tool keeps its standard meaning even after a rename.
        foreach (var (label, name) in ColorLabelNames.Defaults.Concat(labelNames))
        {
            if (string.Equals(trimmed, name, StringComparison.OrdinalIgnoreCase))
                return XmpFact<ColorLabel>.Matched(label);
        }
        return XmpFact<ColorLabel>.Unsupported;
    }

    private static XmpFact<CropRegion> ParseCrop(
        XDocument document,
        int fileExifOrientation)
    {
        if (!TryReadLiveValue(document, CameraRaw, "HasCrop", out var hasCrop))
            return XmpFact<CropRegion>.Unsupported;
        if (hasCrop == null || string.Equals(
                hasCrop.Trim(), "False", StringComparison.OrdinalIgnoreCase))
        {
            return XmpFact<CropRegion>.Empty;
        }
        if (!string.Equals(
                hasCrop.Trim(), "True", StringComparison.OrdinalIgnoreCase) ||
            HasConflictingCropValues(document) ||
            fileExifOrientation != 1 || !HasPortableOrientation(document) ||
            !IsZero(ReadLiveValue(document, CameraRaw, "CropAngle")) ||
            IsWarpEntangled(document))
        {
            return XmpFact<CropRegion>.Unsupported;
        }

        if (!TryReadEdge(document, "CropLeft", out var left) ||
            !TryReadEdge(document, "CropTop", out var top) ||
            !TryReadEdge(document, "CropRight", out var right) ||
            !TryReadEdge(document, "CropBottom", out var bottom) ||
            left >= right || top >= bottom)
        {
            return XmpFact<CropRegion>.Unsupported;
        }
        return XmpFact<CropRegion>.Matched(new CropRegion
        {
            Left = left,
            Top = top,
            Right = right,
            Bottom = bottom
        });
    }

    private static bool HasPortableOrientation(XDocument document)
    {
        if (!TryReadLiveValue(document, Tiff, "Orientation", out var value))
            return false;
        return value == null || int.TryParse(
            value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture,
            out var orientation) && orientation == 1;
    }

    private static bool TryReadEdge(
        XDocument document,
        string name,
        out double value)
    {
        if (!TryReadLiveValue(document, CameraRaw, name, out var text))
        {
            value = default;
            return false;
        }
        return double.TryParse(text, NumberStyles.Float,
                   CultureInfo.InvariantCulture, out value) &&
               double.IsFinite(value) && value is >= 0 and <= 1;
    }

    private static bool IsZero(string? text) =>
        text == null || double.TryParse(text, NumberStyles.Float,
            CultureInfo.InvariantCulture, out var value) &&
        double.IsFinite(value) && value == 0;

    internal static bool IsWarpEntangled(XDocument document)
    {
        if (!TryReadLiveValue(
                document, CameraRaw, "CropConstrainToWarp", out var text))
        {
            return true;
        }
        return text != null && text.Trim() is not "0" &&
            !string.Equals(text.Trim(), "False", StringComparison.OrdinalIgnoreCase);
    }

    private static XmpMergeResult MergeCrop(
        XDocument document,
        XmpCropProjection projection)
    {
        if (HasConflictingCropValues(document))
            return new(false, false, true, CropSkipReason:
                "sidecar has conflicting crop tuples");
        if (IsWarpEntangled(document) || !HasPortableOrientation(document))
            return new(false, false, true, CropSkipReason:
                "sidecar crop uses unsupported geometry");

        var existing = ParseCrop(document, 1);
        if (projection.Kind != XmpCropProjectionKind.Portable)
        {
            RemoveCrop(document);
            return new(false, false,
                projection.Kind == XmpCropProjectionKind.NotPortable);
        }

        var crop = projection.Crop!;
        SetLiveValue(document, CameraRaw, "HasCrop", "True");
        SetLiveValue(document, CameraRaw, "CropLeft", Format(crop.Left));
        SetLiveValue(document, CameraRaw, "CropTop", Format(crop.Top));
        SetLiveValue(document, CameraRaw, "CropRight", Format(crop.Right));
        SetLiveValue(document, CameraRaw, "CropBottom", Format(crop.Bottom));
        SetLiveValue(document, CameraRaw, "CropAngle", "0");
        return new(false, existing.Kind == XmpFactKind.Unsupported, false);
    }

    private static string Format(double value) =>
        value.ToString("R", CultureInfo.InvariantCulture);

    private static void RemoveCrop(XDocument document)
    {
        foreach (var name in new[]
                 {
                     "HasCrop", "CropLeft", "CropTop", "CropRight", "CropBottom"
                 })
        {
            RemoveLiveValue(document, CameraRaw, name);
        }
    }

    private static string? ReadLiveValue(
        XDocument document,
        XNamespace xmlNamespace,
        string localName) => TryReadLiveValue(
            document, xmlNamespace, localName, out var value) ? value : null;

    private static bool TryReadLiveValue(
        XDocument document,
        XNamespace xmlNamespace,
        string localName,
        out string? value)
    {
        value = null;
        var found = false;
        foreach (var description in LiveDescriptions(document))
        {
            var attribute = description.Attribute(xmlNamespace + localName);
            if (attribute == null) continue;
            if (found && !string.Equals(
                    value, attribute.Value, StringComparison.Ordinal))
            {
                return false;
            }
            value = attribute.Value;
            found = true;
        }
        return true;
    }

    private static bool HasConflictingCropValues(XDocument document) =>
        CropPropertyNames.Any(name => !TryReadLiveValue(
            document, CameraRaw, name, out _));

    private static void SetLiveValue(
        XDocument document,
        XNamespace xmlNamespace,
        string localName,
        string value)
    {
        var description = LiveDescriptions(document).FirstOrDefault(candidate =>
                CropPropertyNames.Any(name => candidate.Attribute(
                    CameraRaw + name) != null)) ?? Description(document);
        if (xmlNamespace == CameraRaw)
        {
            foreach (var other in LiveDescriptions(document).Where(candidate =>
                         !ReferenceEquals(candidate, description)))
                other.Attribute(xmlNamespace + localName)?.Remove();
        }
        if (xmlNamespace == CameraRaw &&
            description.GetPrefixOfNamespace(CameraRaw) == null)
        {
            description.SetAttributeValue(
                XNamespace.Xmlns + "crs", CameraRaw.NamespaceName);
        }
        description.SetAttributeValue(xmlNamespace + localName, value);
    }

    private static void RemoveLiveValue(
        XDocument document,
        XNamespace xmlNamespace,
        string localName)
    {
        foreach (var description in LiveDescriptions(document))
            description.Attribute(xmlNamespace + localName)?.Remove();
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
        var value = ColorLabelNames.Defaults.GetValueOrDefault(
            label, label.ToString());
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
        var description = LiveDescription(document);
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

    private static XElement? LiveDescription(XDocument document) =>
        LiveDescriptions(document).FirstOrDefault();

    private static IEnumerable<XElement> LiveDescriptions(XDocument document) =>
        document.Descendants(Rdf + "RDF")
            .SelectMany(rdf => rdf.Elements(Rdf + "Description"));

    private static readonly string[] CropPropertyNames =
    [
        "HasCrop", "CropLeft", "CropTop", "CropRight", "CropBottom",
        "CropAngle", "CropConstrainToWarp"
    ];
}
