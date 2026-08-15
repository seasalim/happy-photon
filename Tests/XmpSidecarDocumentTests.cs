using System.Xml.Linq;
using HappyPhoton.Models;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class XmpSidecarDocumentTests
{
    [Fact]
    public void DarktableReject_LeavesRatingMissingAndReadsRecognizedLabel()
    {
        var document = FactDocument("-1", pick: null);
        Description(document).SetAttributeValue(
            XmpSidecarDocument.Xmp + "Label", "Red");

        var facts = XmpSidecarDocument.ReadFacts(
            document, ColorLabelNames.Defaults);

        Assert.Equal(XmpFactKind.Missing, facts.Rating.Kind);
        Assert.Equal(ImageFlag.Rejected, facts.Flag.Value);
        Assert.Equal(ColorLabel.Red, facts.Label.Value);
        Assert.Equal(XmpFactKind.Matched, facts.Label.Kind);
    }

    [Fact]
    public void RatingMerge_PreservesForeignXmlAndUnsupportedLabel()
    {
        var document = LoadFixture("darktable-rating.xmp");
        var before = XDocument.Parse(document.ToString(SaveOptions.DisableFormatting));
        var snapshot = Snapshot(ImageFlag.Unflagged, rating: 5,
            colorLabel: ColorLabel.Blue, pendingAxes: AssessmentAxes.Rating);

        XmpSidecarDocument.Merge(
            document, snapshot, AssessmentAxes.Rating,
            ColorLabelNames.Defaults);

        Assert.Equal("5", Read(XmpSidecarDocument.Xmp, "Rating", document));
        Assert.Equal("Verde", Read(XmpSidecarDocument.Xmp, "Label", document));
        XNamespace darktable = "http://darktable.sf.net/";
        Assert.Equal(
            before.Descendants(darktable + "history_end").Single().Value,
            document.Descendants(darktable + "history_end").Single().Value);
        Assert.Equal("4", document.Descendants().Attributes(
            darktable + "xmp_version").Single().Value);
    }

    [Theory]
    [InlineData(ImageFlag.Picked, "1", "True")]
    [InlineData(ImageFlag.Unflagged, "0", null)]
    [InlineData(ImageFlag.Rejected, "-1", "False")]
    public void FlagMerge_WritesStandardPickAndRestoresDarktableRating(
        ImageFlag flag,
        string expectedPick,
        string? expectedGood)
    {
        var document = XmpSidecarDocument.Create();
        var description = Description(document);
        description.SetAttributeValue(XmpSidecarDocument.Xmp + "Rating", "-1");

        XmpSidecarDocument.Merge(
            document, Snapshot(flag, rating: 4), AssessmentAxes.Flag,
            ColorLabelNames.Defaults);

        Assert.Equal("4", Read(XmpSidecarDocument.Xmp, "Rating", document));
        Assert.Equal(expectedPick,
            Read(XmpSidecarDocument.XmpDynamicMedia, "pick", document));
        Assert.Equal(expectedGood,
            Read(XmpSidecarDocument.XmpDynamicMedia, "good", document));
        var facts = XmpSidecarDocument.ReadFacts(
            document, ColorLabelNames.Defaults);
        Assert.Equal(4, facts.Rating.Value);
        Assert.Equal(flag, facts.Flag.Value);
    }

    [Fact]
    public void RatingMerge_OnRejectedSnapshot_PreservesRejectInDynamicMedia()
    {
        var document = XmpSidecarDocument.Create();
        var description = Description(document);
        description.SetAttributeValue(XmpSidecarDocument.Xmp + "Rating", "3");
        description.SetAttributeValue(
            XmpSidecarDocument.XmpDynamicMedia + "pick", "1");
        description.SetAttributeValue(
            XmpSidecarDocument.XmpDynamicMedia + "good", "True");
        XNamespace happyPhoton = "http://happyphoton.app/xmp/1.0/";
        description.SetAttributeValue(
            happyPhoton + "Rating", "3");

        XmpSidecarDocument.Merge(
            document, Snapshot(ImageFlag.Rejected, rating: 4),
            AssessmentAxes.Rating, ColorLabelNames.Defaults);

        Assert.Equal("4", Read(XmpSidecarDocument.Xmp, "Rating", document));
        Assert.Equal("-1",
            Read(XmpSidecarDocument.XmpDynamicMedia, "pick", document));
        Assert.Equal("False",
            Read(XmpSidecarDocument.XmpDynamicMedia, "good", document));
        Assert.Null(Read(happyPhoton, "Rating", document));
        var facts = XmpSidecarDocument.ReadFacts(
            document, ColorLabelNames.Defaults);
        Assert.Equal(4, facts.Rating.Value);
        Assert.Equal(ImageFlag.Rejected, facts.Flag.Value);
    }

    [Fact]
    public void LabelMerge_ReplacesUnsupportedOnlyWhenSettingAValue()
    {
        var document = XmpSidecarDocument.Create();
        var description = Description(document);
        description.SetAttributeValue(XmpSidecarDocument.Xmp + "Label", "Custom");
        var snapshot = Snapshot(ImageFlag.Unflagged,
            colorLabel: ColorLabel.Blue, pendingAxes: AssessmentAxes.Label);

        var replaced = XmpSidecarDocument.Merge(
            document, snapshot, AssessmentAxes.Label, ColorLabelNames.Defaults);

        Assert.True(replaced.ReplacedUnsupportedLabel);
        Assert.Equal("Blue", Read(XmpSidecarDocument.Xmp, "Label", document));

        description.SetAttributeValue(XmpSidecarDocument.Xmp + "Label", "Custom");
        var preserved = XmpSidecarDocument.Merge(
            document, snapshot with { ColorLabel = ColorLabel.None },
            AssessmentAxes.Label, ColorLabelNames.Defaults);
        Assert.False(preserved.ReplacedUnsupportedLabel);
        Assert.Equal("Custom", Read(XmpSidecarDocument.Xmp, "Label", document));

        description.SetAttributeValue(XmpSidecarDocument.Xmp + "Label", "Red");
        XmpSidecarDocument.Merge(
            document, snapshot with { ColorLabel = ColorLabel.None },
            AssessmentAxes.Label, ColorLabelNames.Defaults);
        Assert.Equal(string.Empty,
            Read(XmpSidecarDocument.Xmp, "Label", document));
        Assert.Equal(XmpFactKind.Empty, XmpSidecarDocument.ReadFacts(
            document, ColorLabelNames.Defaults).Label.Kind);
    }

    [Fact]
    public void LabelMerge_WithRenamedNames_WritesCanonicalName()
    {
        var document = XmpSidecarDocument.Create();
        var snapshot = Snapshot(ImageFlag.Unflagged,
            colorLabel: ColorLabel.Red, pendingAxes: AssessmentAxes.Label);

        XmpSidecarDocument.Merge(
            document, snapshot, AssessmentAxes.Label, RenamedLabelNames());

        Assert.Equal("Red", Read(XmpSidecarDocument.Xmp, "Label", document));
    }

    [Theory]
    [InlineData("Red", XmpFactKind.Matched, ColorLabel.Red)]
    [InlineData("Client", XmpFactKind.Matched, ColorLabel.Red)]
    [InlineData("Yellow", XmpFactKind.Matched, ColorLabel.Yellow)]
    [InlineData("Rouge", XmpFactKind.Unsupported, ColorLabel.None)]
    public void LabelRead_MatchesCanonicalAndDisplayNames(
        string text,
        XmpFactKind expectedKind,
        ColorLabel expectedLabel)
    {
        var document = XmpSidecarDocument.Create();
        Description(document).SetAttributeValue(
            XmpSidecarDocument.Xmp + "Label", text);

        var fact = XmpSidecarDocument.ReadFacts(
            document, RenamedLabelNames()).Label;

        Assert.Equal(expectedKind, fact.Kind);
        if (expectedKind == XmpFactKind.Matched)
            Assert.Equal(expectedLabel, fact.Value);
    }

    [Fact]
    public void LabelMerge_WithRenamedNames_StillPreservesUnsupportedText()
    {
        var document = XmpSidecarDocument.Create();
        Description(document).SetAttributeValue(
            XmpSidecarDocument.Xmp + "Label", "Foreign");
        var snapshot = Snapshot(ImageFlag.Unflagged,
            pendingAxes: AssessmentAxes.Label);

        var preserved = XmpSidecarDocument.Merge(
            document, snapshot, AssessmentAxes.Label, RenamedLabelNames());

        Assert.False(preserved.ReplacedUnsupportedLabel);
        Assert.Equal("Foreign", Read(XmpSidecarDocument.Xmp, "Label", document));
    }

    [Fact]
    public void PrivateProperties_AreIgnoredOnRead()
    {
        var document = XmpSidecarDocument.Create();
        XNamespace happyPhoton = "http://happyphoton.app/xmp/1.0/";
        var description = Description(document);
        description.SetAttributeValue(happyPhoton + "Rating", "4");
        description.SetAttributeValue(happyPhoton + "Flag", "Pick");
        description.SetAttributeValue(happyPhoton + "Axes", "flag,label");

        var facts = XmpSidecarDocument.ReadFacts(
            document, ColorLabelNames.Defaults);

        Assert.Equal(XmpFactKind.Missing, facts.Rating.Kind);
        Assert.Equal(XmpFactKind.Missing, facts.Flag.Kind);
        Assert.Equal(XmpFactKind.Missing, facts.Label.Kind);
    }

    [Theory]
    [InlineData("-1", "1", XmpFactKind.Matched,
        ImageFlag.Rejected)]
    [InlineData("3", "-1", XmpFactKind.Matched,
        ImageFlag.Rejected)]
    [InlineData("3", "1", XmpFactKind.Matched,
        ImageFlag.Picked)]
    [InlineData("3", "0", XmpFactKind.Matched,
        ImageFlag.Unflagged)]
    [InlineData("3", "2", XmpFactKind.Unsupported,
        ImageFlag.Unflagged)]
    [InlineData("3", null, XmpFactKind.WeakClear,
        ImageFlag.Unflagged)]
    [InlineData(null, null, XmpFactKind.Missing,
        ImageFlag.Unflagged)]
    public void FlagRead_UsesStandardPrecedence(
        string? rating,
        string? pick,
        XmpFactKind expectedKind,
        ImageFlag expectedValue)
    {
        var document = FactDocument(rating, pick);

        var fact = XmpSidecarDocument.ReadFacts(
            document, ColorLabelNames.Defaults).Flag;

        Assert.Equal(expectedKind, fact.Kind);
        if (expectedKind != XmpFactKind.Missing &&
            expectedKind != XmpFactKind.Unsupported)
        {
            Assert.Equal(expectedValue, fact.Value);
        }
    }

    [Fact]
    public void DynamicMediaRead_MatchesUriWithAlternatePrefixAndIgnoresGood()
    {
        var document = XDocument.Parse($$"""
            <x:xmpmeta xmlns:x="adobe:ns:meta/">
              <rdf:RDF xmlns:rdf="{{XmpSidecarDocument.Rdf.NamespaceName}}">
                <rdf:Description rdf:about=""
                  xmlns:xmp="{{XmpSidecarDocument.Xmp.NamespaceName}}"
                  xmlns:alternate="{{XmpSidecarDocument.XmpDynamicMedia.NamespaceName}}"
                  xmp:Rating="3" alternate:pick="1" alternate:good="False" />
              </rdf:RDF>
            </x:xmpmeta>
            """);

        var facts = XmpSidecarDocument.ReadFacts(
            document, ColorLabelNames.Defaults);

        Assert.Equal(3, facts.Rating.Value);
        Assert.Equal(ImageFlag.Picked, facts.Flag.Value);
    }

    [Theory]
    [InlineData("3", XmpFactKind.WeakClear)]
    [InlineData(null, XmpFactKind.Missing)]
    public void OrphanedGood_FallsThroughNormally(
        string? rating,
        XmpFactKind expectedKind)
    {
        var document = FactDocument(rating, pick: null, good: "False");

        var fact = XmpSidecarDocument.ReadFacts(
            document, ColorLabelNames.Defaults).Flag;

        Assert.Equal(expectedKind, fact.Kind);
    }

    [Fact]
    public void Merge_SetsFreshMetadataDate()
    {
        var before = DateTime.UtcNow;
        var document = XmpSidecarDocument.Create();

        XmpSidecarDocument.Merge(
            document, Snapshot(ImageFlag.Unflagged, rating: 4),
            AssessmentAxes.Rating, ColorLabelNames.Defaults);

        var metadataDate = DateTime.Parse(
            Read(XmpSidecarDocument.Xmp, "MetadataDate", document)!, null,
            System.Globalization.DateTimeStyles.RoundtripKind);
        Assert.True(metadataDate >= before);
        Assert.Equal(DateTimeKind.Utc, metadataDate.Kind);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void FlagMerge_SerializesXmpDynamicMediaPrefixWithoutPrivateNamespace(
        bool useFreshDocument)
    {
        var document = useFreshDocument
            ? XmpSidecarDocument.Create()
            : XDocument.Parse($$"""
                <x:xmpmeta xmlns:x="adobe:ns:meta/">
                  <rdf:RDF xmlns:rdf="{{XmpSidecarDocument.Rdf.NamespaceName}}">
                    <rdf:Description rdf:about=""
                      xmlns:xmp="{{XmpSidecarDocument.Xmp.NamespaceName}}" />
                  </rdf:RDF>
                </x:xmpmeta>
                """);

        XmpSidecarDocument.Merge(
            document, Snapshot(ImageFlag.Picked), AssessmentAxes.Flag,
            ColorLabelNames.Defaults);
        var serialized = document.ToString(SaveOptions.DisableFormatting);

        Assert.Contains($"xmlns:xmpDM=\"{XmpSidecarDocument.XmpDynamicMediaNamespaceUri}\"",
            serialized, StringComparison.Ordinal);
        Assert.Contains("xmpDM:pick=\"1\"", serialized, StringComparison.Ordinal);
        Assert.Contains("xmpDM:good=\"True\"", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("happyphoton", serialized,
            StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyDictionary<ColorLabel, string> RenamedLabelNames() =>
        new Dictionary<ColorLabel, string>(ColorLabelNames.Defaults)
        {
            [ColorLabel.Red] = "Client",
            [ColorLabel.Yellow] = "Red"
        };

    private static XDocument FactDocument(
        string? rating,
        string? pick,
        string? good = null)
    {
        var document = XmpSidecarDocument.Create();
        var description = Description(document);
        if (rating != null)
            description.SetAttributeValue(XmpSidecarDocument.Xmp + "Rating", rating);
        if (pick != null)
            description.SetAttributeValue(
                XmpSidecarDocument.XmpDynamicMedia + "pick", pick);
        if (good != null)
            description.SetAttributeValue(
                XmpSidecarDocument.XmpDynamicMedia + "good", good);
        return document;
    }

    private static AssessmentSnapshot Snapshot(
        ImageFlag flag,
        int rating = 2,
        ColorLabel colorLabel = ColorLabel.None,
        AssessmentAxes pendingAxes = AssessmentAxes.Flag) => new(
            1, "photo.cr3", flag, rating, colorLabel,
            1, DateTime.UtcNow, pendingAxes);

    private static XElement Description(XDocument document) =>
        document.Descendants(XmpSidecarDocument.Rdf + "Description").Single();

    private static string? Read(
        XNamespace xmlNamespace,
        string localName,
        XDocument document) =>
        XmpSidecarDocument.ReadValue(document, xmlNamespace, localName);

    private static XDocument LoadFixture(string name) => XDocument.Load(
        Path.Combine(GoldenTestPaths.RepositoryRoot, "Tests", "assets", "xmp", name),
        LoadOptions.PreserveWhitespace);
}
