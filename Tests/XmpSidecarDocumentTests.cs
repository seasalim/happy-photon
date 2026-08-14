using System.Xml.Linq;
using HappyPhoton.Models;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class XmpSidecarDocumentTests
{
    [Fact]
    public void LightroomReject_ReadsDisplacedRatingFlagAndRecognizedLabel()
    {
        var document = LoadFixture("lightroom-reject.xmp");

        var facts = XmpSidecarDocument.ReadFacts(
            document, ColorLabelNames.Defaults);

        Assert.Equal(4, facts.Rating.Value);
        Assert.Equal(ImageFlag.Rejected, facts.Flag.Value);
        Assert.Equal(ColorLabel.Red, facts.Label.Value);
        Assert.Equal(XmpFactKind.Matched, facts.Label.Kind);
    }

    [Fact]
    public void RatingMerge_PreservesForeignXmlAndUnsupportedLabel()
    {
        var document = LoadFixture("darktable-rating.xmp");
        var before = XDocument.Parse(document.ToString(SaveOptions.DisableFormatting));
        var snapshot = new AssessmentSnapshot(
            1, "photo.cr3", ImageFlag.Unflagged, 5, ColorLabel.Blue,
            2, DateTime.UtcNow, AssessmentAxes.Rating);

        XmpSidecarDocument.Merge(
            document, snapshot, AssessmentAxes.Rating,
            ColorLabelNames.Defaults);

        Assert.Equal("5", XmpSidecarDocument.ReadValue(
            document, XmpSidecarDocument.Xmp, "Rating"));
        Assert.Equal("Verde", XmpSidecarDocument.ReadValue(
            document, XmpSidecarDocument.Xmp, "Label"));
        XNamespace darktable = "http://darktable.sf.net/";
        Assert.Equal(
            before.Descendants(darktable + "history_end").Single().Value,
            document.Descendants(darktable + "history_end").Single().Value);
        Assert.Equal("4", document.Descendants().Attributes(
            darktable + "xmp_version").Single().Value);
    }

    [Fact]
    public void RejectRoundTrip_KeepsStarsInPrivateNamespaceAndRestoresThem()
    {
        var document = XmpSidecarDocument.Create();
        document.Root!.Descendants(XmpSidecarDocument.Rdf + "Description")
            .Single().SetAttributeValue(XmpSidecarDocument.Xmp + "Rating", "2");
        var rejected = new AssessmentSnapshot(
            1, "photo.cr3", ImageFlag.Rejected, 4, ColorLabel.None,
            1, DateTime.UtcNow, AssessmentAxes.Flag);
        XmpSidecarDocument.Merge(
            document, rejected, AssessmentAxes.Rating | AssessmentAxes.Flag,
            ColorLabelNames.Defaults);
        Assert.Equal("-1", XmpSidecarDocument.ReadValue(
            document, XmpSidecarDocument.Xmp, "Rating"));
        Assert.Equal("4", XmpSidecarDocument.ReadValue(
            document, XmpSidecarDocument.HappyPhoton, "Rating"));

        var picked = rejected with { Flag = ImageFlag.Picked, Revision = 2 };
        XmpSidecarDocument.Merge(
            document, picked, AssessmentAxes.Flag, ColorLabelNames.Defaults);
        Assert.Equal("4", XmpSidecarDocument.ReadValue(
            document, XmpSidecarDocument.Xmp, "Rating"));
        Assert.Equal("Pick", XmpSidecarDocument.ReadValue(
            document, XmpSidecarDocument.HappyPhoton, "Flag"));
    }

    [Fact]
    public void LabelMerge_ReplacesUnsupportedOnlyWhenSettingAValue()
    {
        var document = XmpSidecarDocument.Create();
        var description = document.Descendants(
            XmpSidecarDocument.Rdf + "Description").Single();
        description.SetAttributeValue(XmpSidecarDocument.Xmp + "Label", "Custom");
        var snapshot = new AssessmentSnapshot(
            1, "photo.cr3", ImageFlag.Unflagged, 0, ColorLabel.Blue,
            1, DateTime.UtcNow, AssessmentAxes.Label);

        var replaced = XmpSidecarDocument.Merge(
            document, snapshot, AssessmentAxes.Label, ColorLabelNames.Defaults);

        Assert.True(replaced.ReplacedUnsupportedLabel);
        Assert.Equal("Blue", XmpSidecarDocument.ReadValue(
            document, XmpSidecarDocument.Xmp, "Label"));

        description.SetAttributeValue(XmpSidecarDocument.Xmp + "Label", "Custom");
        var cleared = XmpSidecarDocument.Merge(
            document, snapshot with { ColorLabel = ColorLabel.None },
            AssessmentAxes.Label, ColorLabelNames.Defaults);
        Assert.False(cleared.ReplacedUnsupportedLabel);
        Assert.Equal("Custom", XmpSidecarDocument.ReadValue(
            document, XmpSidecarDocument.Xmp, "Label"));

        description.SetAttributeValue(XmpSidecarDocument.Xmp + "Label", "Red");
        XmpSidecarDocument.Merge(
            document, snapshot with { ColorLabel = ColorLabel.None },
            AssessmentAxes.Label, ColorLabelNames.Defaults);
        Assert.Null(XmpSidecarDocument.ReadValue(
            document, XmpSidecarDocument.Xmp, "Label"));
    }

    [Theory]
    [InlineData("flag", XmpFactKind.Missing, XmpFactKind.Matched)]
    [InlineData("label", XmpFactKind.Empty, XmpFactKind.Missing)]
    [InlineData("future,flag", XmpFactKind.Missing, XmpFactKind.Matched)]
    public void DeclaredAbsentAxis_IsAnExplicitClear(
        string axes,
        XmpFactKind expectedLabel,
        XmpFactKind expectedFlag)
    {
        var document = XmpSidecarDocument.Create();
        document.Descendants(XmpSidecarDocument.Rdf + "Description").Single()
            .SetAttributeValue(XmpSidecarDocument.HappyPhoton + "Axes", axes);

        var facts = XmpSidecarDocument.ReadFacts(
            document, ColorLabelNames.Defaults);

        Assert.Equal(expectedFlag, facts.Flag.Kind);
        Assert.Equal(expectedLabel, facts.Label.Kind);
        if (facts.Flag.Kind == XmpFactKind.Matched)
            Assert.Equal(ImageFlag.Unflagged, facts.Flag.Value);
        if (facts.Label.Kind == XmpFactKind.Empty)
            Assert.Equal(ColorLabel.None, facts.Label.Value);
    }

    [Fact]
    public void UndeclaredPlainRating_ProvidesOnlyAWeakFlagClear()
    {
        var document = XmpSidecarDocument.Create();
        document.Descendants(XmpSidecarDocument.Rdf + "Description").Single()
            .SetAttributeValue(XmpSidecarDocument.Xmp + "Rating", "3");

        var facts = XmpSidecarDocument.ReadFacts(
            document, ColorLabelNames.Defaults);

        Assert.Equal(XmpFactKind.WeakClear, facts.Flag.Kind);
        Assert.False(facts.Flag.CanAdopt);
        Assert.Equal(ImageFlag.Unflagged, facts.Flag.Value);
    }

    [Fact]
    public void UnpickRoundTrip_UsesDeclaredFlagClear()
    {
        var document = XmpSidecarDocument.Create();
        var picked = new AssessmentSnapshot(
            1, "photo.cr3", ImageFlag.Picked, 2, ColorLabel.None,
            1, DateTime.UtcNow, AssessmentAxes.Flag);
        XmpSidecarDocument.Merge(
            document, picked, AssessmentAxes.All, ColorLabelNames.Defaults);

        XmpSidecarDocument.Merge(
            document, picked with { Flag = ImageFlag.Unflagged, Revision = 2 },
            AssessmentAxes.Flag, ColorLabelNames.Defaults);
        var facts = XmpSidecarDocument.ReadFacts(
            document, ColorLabelNames.Defaults);

        Assert.Equal(XmpFactKind.Matched, facts.Flag.Kind);
        Assert.Equal(ImageFlag.Unflagged, facts.Flag.Value);
        Assert.Null(XmpSidecarDocument.ReadValue(
            document, XmpSidecarDocument.HappyPhoton, "Flag"));
        Assert.Contains("flag", XmpSidecarDocument.ReadValue(
            document, XmpSidecarDocument.HappyPhoton, "Axes")!.Split(','));
    }

    [Fact]
    public void Merge_UnionsAxesAndSetsFreshMetadataDate()
    {
        var before = DateTime.UtcNow;
        var document = XmpSidecarDocument.Create();
        document.Descendants(XmpSidecarDocument.Rdf + "Description").Single()
            .SetAttributeValue(
                XmpSidecarDocument.HappyPhoton + "Axes", "future,flag");
        var snapshot = new AssessmentSnapshot(
            1, "photo.cr3", ImageFlag.Unflagged, 4, ColorLabel.None,
            1, before, AssessmentAxes.Rating);

        XmpSidecarDocument.Merge(
            document, snapshot, AssessmentAxes.Rating, ColorLabelNames.Defaults);

        Assert.Equal("future,flag,rating", XmpSidecarDocument.ReadValue(
            document, XmpSidecarDocument.HappyPhoton, "Axes"));
        var metadataDate = DateTime.Parse(XmpSidecarDocument.ReadValue(
            document, XmpSidecarDocument.Xmp, "MetadataDate")!, null,
            System.Globalization.DateTimeStyles.RoundtripKind);
        Assert.True(metadataDate >= before);
        Assert.Equal(DateTimeKind.Utc, metadataDate.Kind);
    }

    private static XDocument LoadFixture(string name) => XDocument.Load(
        Path.Combine(GoldenTestPaths.RepositoryRoot, "Tests", "assets", "xmp", name),
        LoadOptions.PreserveWhitespace);
}
