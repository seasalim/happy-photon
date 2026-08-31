using System.Xml.Linq;
using HappyPhoton.Models;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class XmpCropDocumentTests
{
    public static TheoryData<string, XmpFactKind> LightroomFixtures => new()
    {
        { "lightroom-baseline-nocrop.xmp", XmpFactKind.Empty },
        { "lightroom-crop-plain.xmp", XmpFactKind.Matched },
        { "lightroom-crop-angled.xmp", XmpFactKind.Unsupported },
        { "lightroom-crop-rotated.xmp", XmpFactKind.Unsupported },
        { "lightroom-crop-warp.xmp", XmpFactKind.Unsupported },
        { "lightroom-cleared-reject.xmp", XmpFactKind.Empty },
        { "lightroom-camera-crop-baseline.xmp", XmpFactKind.Matched },
        { "lightroom-camera-crop-reset.xmp", XmpFactKind.Empty }
    };

    [Theory]
    [MemberData(nameof(LightroomFixtures))]
    public void LightroomFixture_HasPinnedCropFact(
        string fixture,
        XmpFactKind expected)
    {
        var facts = XmpSidecarDocument.ReadFacts(
            LoadFixture(fixture), ColorLabelNames.Defaults);

        Assert.Equal(expected, facts.Crop.Kind);
    }

    [Fact]
    public void PlainFixture_ReadsOnlyLiveDescriptionCrop()
    {
        var crop = XmpSidecarDocument.ReadFacts(
            LoadFixture("lightroom-crop-plain.xmp"),
            ColorLabelNames.Defaults).Crop;

        Assert.Equal(XmpFactKind.Matched, crop.Kind);
        Assert.Equal(0.210989, crop.Value.Left, 6);
        Assert.Equal(0.084919, crop.Value.Top, 6);
        Assert.Equal(0.714286, crop.Value.Right, 6);
        Assert.Equal(0.8653, crop.Value.Bottom, 6);
    }

    [Theory]
    [InlineData("NaN", "0", "1", "1")]
    [InlineData("-0.1", "0", "1", "1")]
    [InlineData("0.8", "0", "0.2", "1")]
    [InlineData("0", "0.9", "1", "0.1")]
    [InlineData("0", "0", "missing", "1")]
    public void InvalidEdges_AreUnsupported(
        string left,
        string top,
        string right,
        string bottom)
    {
        var document = CropDocument(left, top, right, bottom);

        Assert.Equal(XmpFactKind.Unsupported,
            ReadCrop(document).Kind);
    }

    [Theory]
    [InlineData("-3", "0")]
    [InlineData("0", "1")]
    [InlineData("bad", "0")]
    public void AngleAndWarp_AreUnsupported(string angle, string warp)
    {
        var document = CropDocument("0.1", "0.2", "0.8", "0.9");
        var description = Description(document);
        description.SetAttributeValue(
            XmpSidecarDocument.CameraRaw + "CropAngle", angle);
        description.SetAttributeValue(
            XmpSidecarDocument.CameraRaw + "CropConstrainToWarp", warp);

        Assert.Equal(XmpFactKind.Unsupported, ReadCrop(document).Kind);
    }

    [Fact]
    public void MissingOrFalseHasCrop_IsEmptyRegardlessOfEdges()
    {
        var document = CropDocument("bad", "2", "-1", "NaN");
        var description = Description(document);
        description.Attribute(XmpSidecarDocument.CameraRaw + "HasCrop")!.Remove();
        Assert.Equal(XmpFactKind.Empty, ReadCrop(document).Kind);

        description.SetAttributeValue(
            XmpSidecarDocument.CameraRaw + "HasCrop", "False");
        Assert.Equal(XmpFactKind.Empty, ReadCrop(document).Kind);
    }

    [Fact]
    public void FileOrientationOtherThanOne_IsUnsupported()
    {
        var document = CropDocument("0.1", "0.2", "0.8", "0.9");

        var crop = XmpSidecarDocument.ReadFacts(
            document, ColorLabelNames.Defaults, fileExifOrientation: 6).Crop;

        Assert.Equal(XmpFactKind.Unsupported, crop.Kind);
    }

    [Fact]
    public void CropInSecondTopLevelDescription_IsReadAndUpdatedThere()
    {
        var document = XmpSidecarDocument.Create();
        var rdf = document.Descendants(XmpSidecarDocument.Rdf + "RDF").Single();
        var first = rdf.Elements(XmpSidecarDocument.Rdf + "Description").Single();
        var second = new XElement(XmpSidecarDocument.Rdf + "Description",
            new XAttribute(XmpSidecarDocument.Rdf + "about", string.Empty));
        SetCrop(second, ".1", ".2", ".8", ".9");
        rdf.Add(second);

        Assert.Equal(.1, ReadCrop(document).Value.Left);
        XmpSidecarDocument.Merge(
            document, Snapshot(), AssessmentAxes.Crop,
            ColorLabelNames.Defaults,
            new XmpCropProjection(XmpCropProjectionKind.Portable,
                new CropRegion { Left = .3, Top = .2, Right = .8, Bottom = .9 }));

        Assert.Null(first.Attribute(XmpSidecarDocument.CameraRaw + "CropLeft"));
        Assert.Equal("0.3", second.Attribute(
            XmpSidecarDocument.CameraRaw + "CropLeft")?.Value);
    }

    [Fact]
    public void Reset_RemovesManagedCropAcrossTopLevelDescriptions()
    {
        var document = XmpSidecarDocument.Create();
        var rdf = document.Descendants(XmpSidecarDocument.Rdf + "RDF").Single();
        var first = rdf.Elements(XmpSidecarDocument.Rdf + "Description").Single();
        SetCrop(first, ".1", ".2", ".8", ".9");
        var second = new XElement(XmpSidecarDocument.Rdf + "Description",
            new XAttribute(XmpSidecarDocument.CameraRaw + "HasCrop", "True"),
            new XAttribute(XmpSidecarDocument.CameraRaw + "CropLeft", ".1"));
        rdf.Add(second);

        XmpSidecarDocument.Merge(
            document, Snapshot(), AssessmentAxes.Crop,
            ColorLabelNames.Defaults,
            new XmpCropProjection(XmpCropProjectionKind.None, null));

        Assert.DoesNotContain(
            rdf.Elements(XmpSidecarDocument.Rdf + "Description")
                .SelectMany(description => description.Attributes()),
            attribute => ManagedResetNames.Contains(attribute.Name.LocalName));
    }

    [Fact]
    public void ConflictingTopLevelCropTuples_AreUnsupportedAndNotRewritten()
    {
        var document = CropDocument(".1", ".2", ".8", ".9");
        var rdf = document.Descendants(XmpSidecarDocument.Rdf + "RDF").Single();
        var second = new XElement(XmpSidecarDocument.Rdf + "Description");
        SetCrop(second, ".3", ".2", ".8", ".9");
        rdf.Add(second);
        var before = document.ToString(SaveOptions.DisableFormatting);

        Assert.Equal(XmpFactKind.Unsupported, ReadCrop(document).Kind);
        var result = XmpSidecarDocument.Merge(
            document, Snapshot(), AssessmentAxes.Crop,
            ColorLabelNames.Defaults,
            new XmpCropProjection(XmpCropProjectionKind.Portable,
                new CropRegion { Left = .2, Top = .2, Right = .8, Bottom = .9 }));

        Assert.True(result.SkippedCrop);
        Assert.False(result.Changed);
        Assert.Contains("conflicting", result.CropSkipReason,
            StringComparison.OrdinalIgnoreCase);
        Assert.Equal(before, document.ToString(SaveOptions.DisableFormatting));
    }

    [Fact]
    public void Merge_RoundTripsAndPreservesEveryOtherCrsAttribute()
    {
        var document = LoadFixture("lightroom-crop-plain.xmp");
        var before = ForeignCrsAttributes(document);
        var projection = new XmpCropProjection(
            XmpCropProjectionKind.Portable,
            new CropRegion { Left = .1, Top = .2, Right = .8, Bottom = .9 });

        var result = XmpSidecarDocument.Merge(
            document, Snapshot(), AssessmentAxes.Crop,
            ColorLabelNames.Defaults, projection);

        Assert.False(result.SkippedCrop);
        Assert.Equal(before, ForeignCrsAttributes(document));
        var crop = ReadCrop(document);
        Assert.Equal(XmpFactKind.Matched, crop.Kind);
        Assert.Equal(.1, crop.Value.Left);
        Assert.Equal(.2, crop.Value.Top);
        Assert.Equal(.8, crop.Value.Right);
        Assert.Equal(.9, crop.Value.Bottom);
        Assert.Equal("0", Description(document).Attribute(
            XmpSidecarDocument.CameraRaw + "CropAngle")?.Value);
    }

    [Fact]
    public void Reset_RemovesOnlyLiveHasCropAndEdges()
    {
        var document = CropDocument("0.1", "0.2", "0.8", "0.9");
        var description = Description(document);
        description.SetAttributeValue(
            XmpSidecarDocument.CameraRaw + "Exposure2012", "1.25");
        description.SetAttributeValue(
            XmpSidecarDocument.CameraRaw + "CropAngle", "0");

        XmpSidecarDocument.Merge(
            document, Snapshot(), AssessmentAxes.Crop,
            ColorLabelNames.Defaults,
            new XmpCropProjection(XmpCropProjectionKind.None, null));

        foreach (var name in ManagedResetNames)
            Assert.Null(description.Attribute(XmpSidecarDocument.CameraRaw + name));
        Assert.Equal("0", description.Attribute(
            XmpSidecarDocument.CameraRaw + "CropAngle")?.Value);
        Assert.Equal("1.25", description.Attribute(
            XmpSidecarDocument.CameraRaw + "Exposure2012")?.Value);
        Assert.Equal(XmpFactKind.Empty, ReadCrop(document).Kind);
    }

    [Fact]
    public void WarpEntangledCrop_IsLeftUnmodifiedAndReported()
    {
        var document = LoadFixture("lightroom-crop-warp.xmp");
        var before = AllCrsAttributes(document);

        var result = XmpSidecarDocument.Merge(
            document, Snapshot(), AssessmentAxes.Crop,
            ColorLabelNames.Defaults,
            new XmpCropProjection(XmpCropProjectionKind.Portable,
                new CropRegion { Left = .1, Top = .1, Right = .9, Bottom = .9 }));

        Assert.True(result.SkippedCrop);
        Assert.Contains("CropConstrainToWarp=\"1\"",
            document.ToString(SaveOptions.DisableFormatting),
            StringComparison.Ordinal);
        Assert.Equal(before, AllCrsAttributes(document));
    }

    [Theory]
    [InlineData("-3", ".1", ".8")]
    [InlineData("0", ".8", ".2")]
    public void DeliberateCrop_ReplacesNonWarpUnsupportedCrop(
        string angle,
        string left,
        string right)
    {
        var document = CropDocument(left, ".2", right, ".9");
        Description(document).SetAttributeValue(
            XmpSidecarDocument.CameraRaw + "CropAngle", angle);

        var result = XmpSidecarDocument.Merge(
            document, Snapshot(), AssessmentAxes.Crop,
            ColorLabelNames.Defaults,
            new XmpCropProjection(XmpCropProjectionKind.Portable,
                new CropRegion { Left = .1, Top = .1, Right = .9, Bottom = .9 }));

        Assert.True(result.ReplacedUnsupportedCrop);
        Assert.Equal(XmpFactKind.Matched, ReadCrop(document).Kind);
    }

    [Fact]
    public void GeometryProjection_DetectsOnlyFrameChanges()
    {
        var before = new EditSettings();
        var tone = before.Clone();
        tone.Exposure = 1;
        Assert.False(XmpCropProjection.GeometryChanged(before, tone));

        foreach (var after in new[]
                 {
                     WithCrop(before),
                     WithRotation(before),
                     WithHorizon(before),
                     WithGeometry(before)
                 })
        {
            Assert.True(XmpCropProjection.GeometryChanged(before, after));
        }
    }

    private static readonly string[] ManagedResetNames =
        ["HasCrop", "CropLeft", "CropTop", "CropRight", "CropBottom"];

    private static XmpFact<CropRegion> ReadCrop(XDocument document) =>
        XmpSidecarDocument.ReadFacts(
            document, ColorLabelNames.Defaults).Crop;

    private static XDocument CropDocument(
        string left,
        string top,
        string right,
        string bottom)
    {
        var document = XmpSidecarDocument.Create();
        var description = Description(document);
        description.SetAttributeValue(
            XNamespace.Xmlns + "crs", XmpSidecarDocument.CameraRaw.NamespaceName);
        description.SetAttributeValue(
            XmpSidecarDocument.CameraRaw + "HasCrop", "True");
        description.SetAttributeValue(XmpSidecarDocument.CameraRaw + "CropLeft", left);
        description.SetAttributeValue(XmpSidecarDocument.CameraRaw + "CropTop", top);
        description.SetAttributeValue(XmpSidecarDocument.CameraRaw + "CropRight", right);
        description.SetAttributeValue(XmpSidecarDocument.CameraRaw + "CropBottom", bottom);
        return document;
    }

    private static void SetCrop(
        XElement description,
        string left,
        string top,
        string right,
        string bottom)
    {
        description.SetAttributeValue(
            XmpSidecarDocument.CameraRaw + "HasCrop", "True");
        description.SetAttributeValue(
            XmpSidecarDocument.CameraRaw + "CropLeft", left);
        description.SetAttributeValue(
            XmpSidecarDocument.CameraRaw + "CropTop", top);
        description.SetAttributeValue(
            XmpSidecarDocument.CameraRaw + "CropRight", right);
        description.SetAttributeValue(
            XmpSidecarDocument.CameraRaw + "CropBottom", bottom);
    }

    private static IReadOnlyList<string> ForeignCrsAttributes(XDocument document)
    {
        var live = Description(document);
        var managed = new HashSet<string>(ManagedResetNames.Append("CropAngle"));
        return document.Descendants().Attributes()
            .Where(attribute => attribute.Name.Namespace ==
                                XmpSidecarDocument.CameraRaw &&
                (attribute.Parent != live || !managed.Contains(attribute.Name.LocalName)))
            .Select(attribute => $"{attribute.Parent!.Name}|{attribute.Name}|{attribute.Value}")
            .ToArray();
    }

    private static IReadOnlyList<string> AllCrsAttributes(XDocument document) =>
        document.Descendants().Attributes()
            .Where(attribute => attribute.Name.Namespace ==
                                XmpSidecarDocument.CameraRaw)
            .Select(attribute => $"{attribute.Parent!.Name}|{attribute.Name}|{attribute.Value}")
            .ToArray();

    private static EditSettings WithCrop(EditSettings settings)
    {
        settings = settings.Clone();
        settings.Crop = new CropRegion { Left = .1, Right = .9 };
        return settings;
    }

    private static EditSettings WithRotation(EditSettings settings)
    {
        var clone = settings.Clone();
        clone.Rotation = 90;
        return clone;
    }

    private static EditSettings WithHorizon(EditSettings settings)
    {
        var clone = settings.Clone();
        clone.HorizonRotation = 1;
        return clone;
    }

    private static EditSettings WithGeometry(EditSettings settings)
    {
        var clone = settings.Clone();
        clone.Geometry = new GeometrySettings { Vertical = 1 };
        return clone;
    }

    private static AssessmentSnapshot Snapshot() => new(
        1, "photo.cr3", ImageFlag.Unflagged, 0, ColorLabel.None,
        1, DateTime.UtcNow, AssessmentAxes.Crop);

    private static XElement Description(XDocument document) =>
        document.Descendants(XmpSidecarDocument.Rdf + "RDF")
            .Single().Elements(XmpSidecarDocument.Rdf + "Description").Single();

    private static XDocument LoadFixture(string name) => XDocument.Load(
        Path.Combine(GoldenTestPaths.RepositoryRoot,
            "Tests", "assets", "xmp", name), LoadOptions.PreserveWhitespace);
}
