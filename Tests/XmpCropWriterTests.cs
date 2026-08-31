using System.Xml.Linq;
using HappyPhoton.Models;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class XmpCropWriterTests : IDisposable
{
    private readonly TemporaryDirectory _root = new();

    [Fact]
    public async Task CropWrite_WritesPortableCrsTuple()
    {
        using var catalog = await CreateCatalogAsync();
        var snapshot = await PendingCropAsync(
            catalog, "portable.cr3", new EditSettings { Crop = Crop(.1) });
        await using var writer = Writer(catalog);
        writer.Start();

        Assert.True(writer.TryEnqueue(
            snapshot, AssessmentAxes.Crop, [snapshot.FilePath],
            XmpSidecarNaming.FullName));
        await writer.DrainAsync();

        var facts = XmpSidecarDocument.ReadFacts(
            XDocument.Load(snapshot.FilePath + ".xmp"),
            ColorLabelNames.Defaults);
        Assert.Equal(XmpFactKind.Matched, facts.Crop.Kind);
        Assert.Equal(.1, facts.Crop.Value.Left);
        Assert.Equal(AssessmentAxes.None,
            Assert.Single(await catalog.LoadAssessmentSnapshotsAsync(
                [snapshot.ImageId])).PendingAxes);
    }

    [Fact]
    public async Task RatingOnlyFreshSidecar_DoesNotPublishExistingLocalCrop()
    {
        using var catalog = await CreateCatalogAsync();
        var path = Path("rating-only.cr3");
        var id = await catalog.GetOrCreateImageAsync(path);
        await catalog.SaveEditSettingsAsync(
            id, new EditSettings { Crop = Crop(.2) });
        var snapshot = Assert.Single(await catalog.MutateAssessmentsAsync([
            new AssessmentMutation(id, AssessmentAxes.Rating, Rating: 4,
                PendingAxes: AssessmentAxes.Rating)
        ]));
        await using var writer = Writer(catalog);
        writer.Start();

        writer.TryEnqueue(snapshot, AssessmentAxes.Rating, [path],
            XmpSidecarNaming.FullName);
        await writer.DrainAsync();

        var document = XDocument.Load(path + ".xmp");
        Assert.DoesNotContain(document.Descendants().Attributes(), attribute =>
            attribute.Name.Namespace == XmpSidecarDocument.CameraRaw);
    }

    [Fact]
    public async Task RevisionRequeue_ReloadsNewestPersistedCrop()
    {
        using var catalog = await CreateCatalogAsync();
        var original = await PendingCropAsync(
            catalog, "requeue.cr3", new EditSettings { Crop = Crop(.1) });
        var promotions = 0;
        await using var writer = Writer(catalog);
        writer.BeforePromotionAsync = async (_, _) =>
        {
            if (Interlocked.Increment(ref promotions) != 1) return;
            await catalog.SaveEditSettingsAsync(
                original.ImageId,
                new EditSettings { Crop = Crop(.3) });
            await catalog.MutateAssessmentsAsync([
                new AssessmentMutation(original.ImageId, AssessmentAxes.Crop,
                    PendingAxes: AssessmentAxes.Crop)
            ]);
        };
        writer.Start();

        writer.TryEnqueue(original, AssessmentAxes.Crop, [original.FilePath],
            XmpSidecarNaming.FullName);
        await writer.DrainAsync();

        var crop = XmpSidecarDocument.ReadFacts(
            XDocument.Load(original.FilePath + ".xmp"),
            ColorLabelNames.Defaults).Crop;
        Assert.Equal(.3, crop.Value.Left);
        Assert.True(promotions >= 2);
    }

    [Fact]
    public async Task NonPortableGeometry_RemovesManagedCropAndReportsSkip()
    {
        using var catalog = await CreateCatalogAsync();
        var path = Path("rotated.cr3");
        var document = XmpSidecarDocument.Create();
        var description = document.Descendants(XmpSidecarDocument.Rdf + "Description")
            .Single();
        description.SetAttributeValue(
            XmpSidecarDocument.CameraRaw + "HasCrop", "True");
        description.SetAttributeValue(
            XmpSidecarDocument.CameraRaw + "CropLeft", ".1");
        description.SetAttributeValue(
            XmpSidecarDocument.CameraRaw + "CropTop", ".2");
        description.SetAttributeValue(
            XmpSidecarDocument.CameraRaw + "CropRight", ".8");
        description.SetAttributeValue(
            XmpSidecarDocument.CameraRaw + "CropBottom", ".9");
        description.SetAttributeValue(
            XmpSidecarDocument.CameraRaw + "Exposure2012", "1");
        document.Save(path + ".xmp");
        var snapshot = await PendingCropAsync(
            catalog, "rotated.cr3", new EditSettings { Rotation = 90 });
        var reports = new List<string>();
        await using var writer = Writer(catalog);
        writer.Report = reports.Add;
        writer.Start();

        writer.TryEnqueue(snapshot, AssessmentAxes.Crop, [path],
            XmpSidecarNaming.FullName);
        await writer.DrainAsync();

        document = XDocument.Load(path + ".xmp");
        description = document.Descendants(XmpSidecarDocument.Rdf + "Description")
            .Single();
        Assert.Null(description.Attribute(
            XmpSidecarDocument.CameraRaw + "HasCrop"));
        Assert.Equal("1", description.Attribute(
            XmpSidecarDocument.CameraRaw + "Exposure2012")?.Value);
        Assert.Contains(reports, report => report.Contains(
            "crop skipped", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task OrientedSource_RemovesManagedCropAndReportsSkip()
    {
        using var catalog = await CreateCatalogAsync();
        var path = Path("orientation-6.jpg");
        File.Copy(System.IO.Path.Combine(
            GoldenTestPaths.RepositoryRoot, "Tests", "assets",
            "srgb-exif-gps-orientation-6.jpg"), path);
        var document = XmpSidecarDocument.Create();
        var description = document.Descendants(XmpSidecarDocument.Rdf + "Description")
            .Single();
        description.SetAttributeValue(
            XmpSidecarDocument.CameraRaw + "HasCrop", "True");
        description.SetAttributeValue(
            XmpSidecarDocument.CameraRaw + "CropLeft", ".1");
        description.SetAttributeValue(
            XmpSidecarDocument.CameraRaw + "CropTop", ".2");
        description.SetAttributeValue(
            XmpSidecarDocument.CameraRaw + "CropRight", ".8");
        description.SetAttributeValue(
            XmpSidecarDocument.CameraRaw + "CropBottom", ".9");
        document.Save(path + ".xmp");
        var snapshot = await PendingCropAsync(
            catalog, "orientation-6.jpg",
            new EditSettings { Crop = Crop(.2) });
        var reports = new List<string>();
        await using var writer = new XmpSidecarWriter(
            catalog, ColorLabelNames.Defaults, new AlwaysAvailable());
        writer.Report = reports.Add;
        writer.Start();

        writer.TryEnqueue(snapshot, AssessmentAxes.Crop, [path],
            XmpSidecarNaming.FullName);
        await writer.DrainAsync();

        document = XDocument.Load(path + ".xmp");
        Assert.Null(document.Descendants(XmpSidecarDocument.Rdf + "Description")
            .Single().Attribute(XmpSidecarDocument.CameraRaw + "HasCrop"));
        Assert.Contains(reports, report => report.Contains(
            "orientation is not 1", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task TemporarilyUnavailableCrop_StaysPendingWithoutSidecarChurn()
    {
        using var catalog = await CreateCatalogAsync();
        var snapshot = await PendingCropAsync(
            catalog, "offline.cr3", new EditSettings { Crop = Crop(.2) });
        var sidecar = snapshot.FilePath + ".xmp";
        XmpSidecarDocument.Create().Save(sidecar);
        var timestamp = new DateTime(2026, 8, 20, 12, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(sidecar, timestamp);
        var before = File.ReadAllBytes(sidecar);
        var reports = new List<string>();
        await using var writer = new XmpSidecarWriter(
            catalog, ColorLabelNames.Defaults,
            new OriginalUnavailable(snapshot.FilePath),
            readOrientation: TryOrientation);
        writer.Report = reports.Add;
        writer.Start();

        writer.TryEnqueue(snapshot, AssessmentAxes.Crop, [snapshot.FilePath],
            XmpSidecarNaming.FullName);
        await writer.DrainAsync();

        Assert.Equal(before, File.ReadAllBytes(sidecar));
        Assert.Equal(timestamp, File.GetLastWriteTimeUtc(sidecar));
        Assert.Equal(AssessmentAxes.Crop,
            Assert.Single(await catalog.LoadAssessmentSnapshotsAsync(
                [snapshot.ImageId])).PendingAxes);
        Assert.Contains(reports, report => report.Contains(
            "orientation is unavailable", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task OrientationReadFailure_StaysPendingWithoutSidecarChurn()
    {
        using var catalog = await CreateCatalogAsync();
        var snapshot = await PendingCropAsync(
            catalog, "unreadable.cr3", new EditSettings { Crop = Crop(.2) });
        var sidecar = snapshot.FilePath + ".xmp";
        XmpSidecarDocument.Create().Save(sidecar);
        var timestamp = new DateTime(2026, 8, 21, 12, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(sidecar, timestamp);
        var before = File.ReadAllBytes(sidecar);
        var reports = new List<string>();
        await using var writer = new XmpSidecarWriter(
            catalog, ColorLabelNames.Defaults, new AlwaysAvailable(),
            readOrientation: FailOrientation);
        writer.Report = reports.Add;
        writer.Start();

        writer.TryEnqueue(snapshot, AssessmentAxes.Crop, [snapshot.FilePath],
            XmpSidecarNaming.FullName);
        await writer.DrainAsync();

        Assert.Equal(before, File.ReadAllBytes(sidecar));
        Assert.Equal(timestamp, File.GetLastWriteTimeUtc(sidecar));
        Assert.Equal(AssessmentAxes.Crop,
            Assert.Single(await catalog.LoadAssessmentSnapshotsAsync(
                [snapshot.ImageId])).PendingAxes);
        Assert.Contains(reports, report => report.Contains(
            "could not be read", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task AvailableAxisWritesWhileUnavailableCropRemainsPending()
    {
        using var catalog = await CreateCatalogAsync();
        var path = Path("mixed.cr3");
        var id = await catalog.GetOrCreateImageAsync(path);
        await catalog.SaveEditSettingsAsync(
            id, new EditSettings { Crop = Crop(.2) });
        var snapshot = Assert.Single(await catalog.MutateAssessmentsAsync([
            new AssessmentMutation(
                id, AssessmentAxes.Rating | AssessmentAxes.Crop,
                Rating: 4,
                PendingAxes: AssessmentAxes.Rating | AssessmentAxes.Crop)
        ]));
        var document = XmpSidecarDocument.Create();
        document.Descendants(XmpSidecarDocument.Rdf + "Description").Single()
            .SetAttributeValue(XmpSidecarDocument.Xmp + "Rating", "1");
        document.Save(path + ".xmp");
        await using var writer = new XmpSidecarWriter(
            catalog, ColorLabelNames.Defaults,
            new OriginalUnavailable(path),
            readOrientation: TryOrientation);
        writer.Start();

        writer.TryEnqueue(
            snapshot, AssessmentAxes.Rating | AssessmentAxes.Crop, [path],
            XmpSidecarNaming.FullName);
        await writer.DrainAsync();

        document = XDocument.Load(path + ".xmp");
        Assert.Equal("4", XmpSidecarDocument.ReadValue(
            document, XmpSidecarDocument.Xmp, "Rating"));
        Assert.Equal(AssessmentAxes.Crop,
            Assert.Single(await catalog.LoadAssessmentSnapshotsAsync([id]))
                .PendingAxes);
    }

    [Fact]
    public async Task ConflictingCropDescriptions_AreReportedAndResolvedWithoutChurn()
    {
        using var catalog = await CreateCatalogAsync();
        var snapshot = await PendingCropAsync(
            catalog, "conflict.cr3", new EditSettings { Crop = Crop(.2) });
        var sidecar = snapshot.FilePath + ".xmp";
        var document = XmpSidecarDocument.Create();
        var rdf = document.Descendants(XmpSidecarDocument.Rdf + "RDF").Single();
        SetCrop(rdf.Elements().Single(), ".1");
        var second = new XElement(XmpSidecarDocument.Rdf + "Description");
        SetCrop(second, ".3");
        rdf.Add(second);
        document.Save(sidecar);
        var timestamp = new DateTime(2026, 8, 22, 12, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(sidecar, timestamp);
        var before = File.ReadAllBytes(sidecar);
        var reports = new List<string>();
        await using var writer = Writer(catalog);
        writer.Report = reports.Add;
        writer.Start();

        writer.TryEnqueue(snapshot, AssessmentAxes.Crop, [snapshot.FilePath],
            XmpSidecarNaming.FullName);
        await writer.DrainAsync();

        Assert.Equal(before, File.ReadAllBytes(sidecar));
        Assert.Equal(timestamp, File.GetLastWriteTimeUtc(sidecar));
        Assert.Equal(AssessmentAxes.None,
            Assert.Single(await catalog.LoadAssessmentSnapshotsAsync(
                [snapshot.ImageId])).PendingAxes);
        Assert.Contains(reports, report => report.Contains(
            "conflicting crop tuples", StringComparison.OrdinalIgnoreCase));
    }

    private async Task<AssessmentSnapshot> PendingCropAsync(
        CatalogService catalog,
        string name,
        EditSettings settings)
    {
        var path = Path(name);
        var id = await catalog.GetOrCreateImageAsync(path);
        await catalog.SaveEditSettingsAsync(id, settings);
        return Assert.Single(await catalog.MutateAssessmentsAsync([
            new AssessmentMutation(id, AssessmentAxes.Crop,
                PendingAxes: AssessmentAxes.Crop)
        ]));
    }

    private XmpSidecarWriter Writer(CatalogService catalog) => new(
        catalog, ColorLabelNames.Defaults, new AlwaysAvailable(),
        readOrientation: TryOrientation);

    private static bool TryOrientation(string path, out int orientation)
    {
        orientation = 1;
        return true;
    }

    private static bool FailOrientation(string path, out int orientation)
    {
        orientation = 0;
        return false;
    }

    private static void SetCrop(XElement description, string left)
    {
        description.SetAttributeValue(
            XmpSidecarDocument.CameraRaw + "HasCrop", "True");
        description.SetAttributeValue(
            XmpSidecarDocument.CameraRaw + "CropLeft", left);
        description.SetAttributeValue(
            XmpSidecarDocument.CameraRaw + "CropTop", ".2");
        description.SetAttributeValue(
            XmpSidecarDocument.CameraRaw + "CropRight", ".8");
        description.SetAttributeValue(
            XmpSidecarDocument.CameraRaw + "CropBottom", ".9");
    }

    private async Task<CatalogService> CreateCatalogAsync()
    {
        var catalog = new CatalogService(Path($"catalog-{Guid.NewGuid():N}"));
        await catalog.InitializeAsync();
        return catalog;
    }

    private static CropRegion Crop(double left) => new()
    {
        Left = left,
        Top = .2,
        Right = .8,
        Bottom = .9
    };

    private string Path(string name) => System.IO.Path.Combine(_root.Path, name);

    public void Dispose() => _root.Dispose();

    private sealed class AlwaysAvailable : ISourceAvailabilityService
    {
        public SourceAvailability GetAvailability(string path) =>
            SourceAvailability.AvailableLocally;
    }

    private sealed class OriginalUnavailable(string originalPath)
        : ISourceAvailabilityService
    {
        public SourceAvailability GetAvailability(string path) =>
            string.Equals(path, originalPath, StringComparison.OrdinalIgnoreCase)
                ? SourceAvailability.RequiresHydration
                : SourceAvailability.AvailableLocally;
    }
}
