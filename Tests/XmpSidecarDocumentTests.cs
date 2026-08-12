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
            document, snapshot, AssessmentAxes.Rating | AssessmentAxes.Label,
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

    private static XDocument LoadFixture(string name) => XDocument.Load(
        Path.Combine(GoldenTestPaths.RepositoryRoot, "Tests", "assets", "xmp", name),
        LoadOptions.PreserveWhitespace);
}
