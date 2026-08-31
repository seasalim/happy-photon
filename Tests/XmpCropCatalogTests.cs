using System.Reflection;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using Microsoft.Data.Sqlite;
using SQLitePCL;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class XmpCropCatalogTests : IDisposable
{
    private readonly TemporaryDirectory _root = new();

    [Fact]
    public async Task CropMutation_BumpsRevisionAndCarriesPendingBit()
    {
        using var catalog = await CreateCatalogAsync();
        var id = await catalog.GetOrCreateImageAsync(Path("pending.cr3"));

        var snapshot = Assert.Single(await catalog.MutateAssessmentsAsync([
            new AssessmentMutation(id, AssessmentAxes.Crop,
                PendingAxes: AssessmentAxes.Crop)
        ]));

        Assert.Equal(1, snapshot.Revision);
        Assert.Equal(AssessmentAxes.Crop, snapshot.PendingAxes);
        Assert.True(await catalog.ClearPendingAxesAsync(
            id, snapshot.Revision, AssessmentAxes.Crop));
    }

    [Fact]
    public async Task MatchedCrop_AdoptsWithHistoryEvenWhenSidecarIsOlder()
    {
        using var catalog = await CreateCatalogAsync();
        var path = Path("older.cr3");
        var id = await catalog.GetOrCreateImageAsync(path);
        var snapshot = Assert.Single(await catalog.MutateAssessmentsAsync([
            new AssessmentMutation(id, AssessmentAxes.Rating, Rating: 2)
        ]));
        var crop = Crop();

        var adoptions = await catalog.AdoptSidecarFactsAsync([
            Item(snapshot, crop, snapshot.AssessedUtc.AddDays(-1))
        ]);

        var adoption = Assert.Single(adoptions);
        Assert.Equal(AssessmentAxes.Crop, adoption.AdoptedAxes);
        Assert.Equal(crop.Left, adoption.AdoptedCrop!.Left);
        var state = (await catalog.LoadImageStatesAsync([path]))[path].Single();
        Assert.Equal(crop.Left, state.EditSettings.Crop!.Left);
        Assert.Equal(snapshot.Revision + 1, state.AssessmentRevision);
        var history = await catalog.LoadEditHistoryAsync(id);
        Assert.Equal(2, history.Entries.Count);
        Assert.Equal("Crop from XMP", history.Entries[^1].Label);
    }

    [Theory]
    [InlineData("crop")]
    [InlineData("rotation")]
    [InlineData("horizon")]
    [InlineData("geometry")]
    public async Task ExistingGeometry_NeverGetsOverwritten(string kind)
    {
        using var catalog = await CreateCatalogAsync();
        var path = Path($"blocked-{kind}.cr3");
        var id = await catalog.GetOrCreateImageAsync(path);
        var settings = Geometry(kind);
        await catalog.SaveEditSettingsAsync(id, settings);
        var snapshot = Assert.Single(
            await catalog.LoadAssessmentSnapshotsAsync([id]));

        var adoptions = await catalog.AdoptSidecarFactsAsync([
            Item(snapshot, Crop(), snapshot.AssessedUtc.AddDays(1))
        ]);

        Assert.Empty(adoptions);
        var stored = (await catalog.LoadImageStatesAsync([path]))[path]
            .Single().EditSettings;
        Assert.True(XmpCropProjection.HasGeometryEdits(stored));
        if (kind == "crop") Assert.Equal(.25, stored.Crop!.Left);
    }

    [Fact]
    public async Task Reconcile_LocalGeometrySkipsCropAdoptionWork()
    {
        using var catalog = await CreateCatalogAsync();
        var path = Path("local-crop.cr3");
        var id = await catalog.GetOrCreateImageAsync(path);
        await catalog.SaveEditSettingsAsync(
            id, new EditSettings { Crop = Crop(.25) });
        WriteMatched(path + ".xmp");
        var availability = new TestSourceAvailabilityService(
            SourceAvailability.AvailableLocally);
        var orientationCalls = 0;
        var reconciler = new XmpSidecarReconciler(
            catalog, new XmpSidecarReader(availability), availability,
            (string _, out int orientation) =>
            {
                orientationCalls++;
                orientation = 1;
                return true;
            });
        using var statements = new SqlStatementRecorder(catalog);

        var result = await reconciler.ReconcileAsync(
            [path], ColorLabelNames.Defaults, XmpSidecarNaming.FullName);

        Assert.Empty(result.Adoptions);
        Assert.Equal(0, orientationCalls);
        Assert.DoesNotContain(statements.Statements, sql =>
            sql.Contains("SELECT images.edit_settings", StringComparison.Ordinal) &&
            sql.Contains("JOIN image_assessments", StringComparison.Ordinal));
    }

    [Fact]
    public async Task EmptyFact_NeverClearsLocalCrop()
    {
        using var catalog = await CreateCatalogAsync();
        var path = Path("never-clear.cr3");
        var id = await catalog.GetOrCreateImageAsync(path);
        var settings = new EditSettings { Crop = Crop(.25) };
        await catalog.SaveEditSettingsAsync(id, settings);
        var snapshot = Assert.Single(
            await catalog.LoadAssessmentSnapshotsAsync([id]));

        var adoptions = await catalog.AdoptSidecarFactsAsync([
            new XmpReconcileItem(snapshot,
                Candidate(path, snapshot.AssessedUtc.AddDays(1)),
                Facts(XmpFact<CropRegion>.Empty))
        ]);

        Assert.Empty(adoptions);
        var stored = (await catalog.LoadImageStatesAsync([path]))[path]
            .Single().EditSettings;
        Assert.Equal(.25, stored.Crop!.Left);
    }

    [Fact]
    public async Task AdoptionTransaction_RevalidatesConcurrentGeometry()
    {
        using var catalog = await CreateCatalogAsync();
        var path = Path("race.cr3");
        var id = await catalog.GetOrCreateImageAsync(path);
        var snapshot = Assert.Single(
            await catalog.LoadAssessmentSnapshotsAsync([id]));
        await catalog.SaveEditSettingsAsync(
            id, new EditSettings { HorizonRotation = 1 });

        var adoptions = await catalog.AdoptSidecarFactsAsync([
            Item(snapshot, Crop(), snapshot.AssessedUtc.AddDays(1))
        ]);

        Assert.Empty(adoptions);
        var stored = (await catalog.LoadImageStatesAsync([path]))[path]
            .Single().EditSettings;
        Assert.Equal(1, stored.HorizonRotation);
        Assert.Null(stored.Crop);
    }

    [Fact]
    public void ViewModelAdoption_RechecksLiveGeometry()
    {
        var image = new ImageFile(Path("live.cr3"));
        image.EditSettings.Rotation = 90;
        var snapshot = new AssessmentSnapshot(
            1, image.FilePath, ImageFlag.Unflagged, 0, ColorLabel.None,
            1, DateTime.UtcNow, AssessmentAxes.None);

        MainWindowViewModel.ApplyXmpAdoption(
            image, new XmpReconcileAdoption(
                snapshot, AssessmentAxes.Crop, Crop()));

        Assert.Null(image.EditSettings.Crop);
        Assert.Equal(90, image.EditSettings.Rotation);
    }

    [Fact]
    public async Task Reconcile_ReportsUnsupportedCropsOncePerPass()
    {
        using var catalog = await CreateCatalogAsync();
        var first = Path("bad-angle.cr3");
        var second = Path("bad-edge.cr3");
        WriteUnsupported(first + ".xmp", "-3", ".8", ".2");
        WriteUnsupported(second + ".xmp", "0", ".9", ".1");

        var result = await new XmpSidecarReconciler(catalog).ReconcileAsync(
            [first, second], ColorLabelNames.Defaults,
            XmpSidecarNaming.FullName);

        var report = Assert.Single(result.Reports);
        Assert.Contains("Unsupported XMP crops skipped: 2", report,
            StringComparison.Ordinal);
        Assert.Contains(first, report, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Reconcile_RejectsMatchedCropForOrientedSource()
    {
        using var catalog = await CreateCatalogAsync();
        var path = Path("orientation-6.jpg");
        File.Copy(System.IO.Path.Combine(
            GoldenTestPaths.RepositoryRoot, "Tests", "assets",
            "srgb-exif-gps-orientation-6.jpg"), path);
        WriteMatched(path + ".xmp");

        var result = await new XmpSidecarReconciler(catalog).ReconcileAsync(
            [path], ColorLabelNames.Defaults, XmpSidecarNaming.FullName);

        Assert.Empty(result.Adoptions);
        Assert.Contains("Unsupported XMP crops skipped: 1",
            Assert.Single(result.Reports), StringComparison.Ordinal);
        var state = (await catalog.LoadImageStatesAsync([path]))[path].Single();
        Assert.Null(state.EditSettings.Crop);
    }

    [Fact]
    public async Task Reconcile_OrientationReadFailureDoesNotAdoptCrop()
    {
        using var catalog = await CreateCatalogAsync();
        var path = Path("orientation-failure.jpg");
        WriteMatched(path + ".xmp");
        var availability = new TestSourceAvailabilityService(
            SourceAvailability.AvailableLocally);
        var reconciler = new XmpSidecarReconciler(
            catalog, new XmpSidecarReader(availability), availability,
            (string _, out int orientation) =>
            {
                orientation = 0;
                return false;
            });

        var result = await reconciler.ReconcileAsync(
            [path], ColorLabelNames.Defaults, XmpSidecarNaming.FullName);

        Assert.Empty(result.Adoptions);
        var state = (await catalog.LoadImageStatesAsync([path]))[path].Single();
        Assert.Null(state.EditSettings.Crop);
    }

    private async Task<CatalogService> CreateCatalogAsync()
    {
        var catalog = new CatalogService(Path($"catalog-{Guid.NewGuid():N}"));
        await catalog.InitializeAsync();
        return catalog;
    }

    private static XmpReconcileItem Item(
        AssessmentSnapshot snapshot,
        CropRegion crop,
        DateTime sidecarUtc) => new(
        snapshot, Candidate(snapshot.FilePath, sidecarUtc),
        Facts(XmpFact<CropRegion>.Matched(crop)));

    private static XmpSidecarFacts Facts(XmpFact<CropRegion> crop) => new(
        XmpFact<int>.Missing,
        XmpFact<ImageFlag>.Missing,
        XmpFact<ColorLabel>.Missing,
        crop);

    private static XmpSidecarCandidate Candidate(string path, DateTime utc) =>
        new(path + ".xmp", utc, 1, true);

    private static CropRegion Crop(double left = .1) => new()
    {
        Left = left,
        Top = .2,
        Right = .8,
        Bottom = .9
    };

    private static EditSettings Geometry(string kind) => kind switch
    {
        "crop" => new EditSettings { Crop = Crop(.25) },
        "rotation" => new EditSettings { Rotation = 90 },
        "horizon" => new EditSettings { HorizonRotation = 1 },
        "geometry" => new EditSettings
        {
            Geometry = new GeometrySettings { Vertical = 1 }
        },
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    private static void WriteUnsupported(
        string path,
        string angle,
        string left,
        string right)
    {
        var document = XmpSidecarDocument.Create();
        var description = document.Descendants(XmpSidecarDocument.Rdf + "Description")
            .Single();
        description.SetAttributeValue(
            XmpSidecarDocument.CameraRaw + "HasCrop", "True");
        description.SetAttributeValue(
            XmpSidecarDocument.CameraRaw + "CropLeft", left);
        description.SetAttributeValue(
            XmpSidecarDocument.CameraRaw + "CropTop", ".1");
        description.SetAttributeValue(
            XmpSidecarDocument.CameraRaw + "CropRight", right);
        description.SetAttributeValue(
            XmpSidecarDocument.CameraRaw + "CropBottom", ".9");
        description.SetAttributeValue(
            XmpSidecarDocument.CameraRaw + "CropAngle", angle);
        document.Save(path);
    }

    private static void WriteMatched(string path)
    {
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
        document.Save(path);
    }

    private string Path(string name) => System.IO.Path.Combine(_root.Path, name);

    public void Dispose() => _root.Dispose();

    private sealed class SqlStatementRecorder : IDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly strdelegate_trace _callback;

        public SqlStatementRecorder(CatalogService catalog)
        {
            _connection = (SqliteConnection)(typeof(CatalogService)
                .GetField("_connection", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(catalog) ?? throw new InvalidOperationException(
                    "Catalog connection is unavailable."));
            _callback = (_, statement) => Statements.Add(statement);
            raw.sqlite3_trace(_connection.Handle, _callback, null);
        }

        public List<string> Statements { get; } = [];

        public void Dispose() => raw.sqlite3_trace(
            _connection.Handle, (strdelegate_trace?)null!, null);
    }
}
