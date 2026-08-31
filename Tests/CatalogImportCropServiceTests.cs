using HappyPhoton.Models;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public sealed partial class CatalogImportServiceTests
{
    [Fact]
    public async Task V3Gate_ImportsPinnedCropsAndSkipsUnsupportedAndVirtualRows()
    {
        var photos = Directory.CreateDirectory(Path.Combine(_root.Path, "photos")).FullName;
        using var fixture = new LightroomCatalogFixture(version: 1504001);
        var rows = LightroomV3CropFixture.LoadRows();
        LightroomV3CropFixture.Populate(fixture, photos, rows);
        fixture.CloseWriter();
        var source = await new LightroomCatalogReader().ReadAsync(fixture.CatalogPath);
        var catalog = await CreateCatalogAsync();
        var import = CropImport(catalog);

        Assert.Equal(21, rows.Count);
        Assert.Equal(7, rows.Count(row => row.MasterImage == null &&
            LightroomV3CropFixture.ParseCrop(row).Kind == XmpFactKind.Matched));
        Assert.Equal(12, rows.Count(row => row.MasterImage == null &&
            LightroomV3CropFixture.ParseCrop(row).Kind == XmpFactKind.Empty));
        Assert.Single(rows, row => row.MasterImage == null &&
            LightroomV3CropFixture.ParseCrop(row).Kind == XmpFactKind.Unsupported);
        Assert.Single(rows, row => row.MasterImage != null);
        Assert.Equal(7, source.Records.Count(record =>
            record.Crop?.Kind == XmpFactKind.Matched));
        Assert.Equal(12, source.Records.Count(record =>
            record.Crop?.Kind == XmpFactKind.Empty));
        Assert.Single(source.Records, record =>
            record.Crop?.Kind == XmpFactKind.Unsupported);

        var mappings = source.Roots.ToDictionary(root => root.SourcePath,
            root => root.SourcePath);
        var preview = await import.CreatePreviewAsync(source, mappings,
            CatalogImportPolicy.LightroomWins, importCrops: true);
        var result = await import.ApplyAsync(preview);

        Assert.Equal(7, preview.Report.Crop!.Written);
        Assert.Equal(11, preview.Report.Crop.NotImported);
        Assert.Equal(1, preview.Report.Crop.Unsupported);
        Assert.Equal(1, preview.Report.VirtualCopyPhotos);
        Assert.Equal(7, result.Adoptions.Count(adoption => adoption.AdoptedCrop != null));
        var path = Path.Combine(photos, "roundtrip", "rt-plain.raf");
        var crop = (await catalog.LoadImageStatesAsync([path]))[path]
            .Single().EditSettings.Crop!;
        Assert.Equal(0.018746, crop.Left, 6);
        Assert.Equal(0.153254, crop.Top, 6);
        Assert.Equal(0.784155, crop.Right, 6);
        Assert.Equal(0.918663, crop.Bottom, 6);
        var warpPath = Path.Combine(photos, "roundtrip", "rt-warpflag.raf");
        Assert.Null((await catalog.LoadImageStatesAsync([warpPath]))[warpPath]
            .Single().EditSettings.Crop);
    }

    [Fact]
    public async Task AngledCrop_IsUnsupportedAndAdoptsNothing()
    {
        var photos = Directory.CreateDirectory(Path.Combine(_root.Path, "photos")).FullName;
        using var fixture = new LightroomCatalogFixture(version: 1504001);
        fixture.AddPhoto(1, photos, "", "rt-angle.raf");
        fixture.AddDevelopSettings(1, ReadCropAsset("lrcrop-v3-rt-plain.lua").Replace(
                "CropTop =", "CropAngle = 1,\nCropTop ="),
            fileWidth: 4000, fileHeight: 3000,
            croppedWidth: 3062, croppedHeight: 2296);
        fixture.CloseWriter();
        var source = await new LightroomCatalogReader().ReadAsync(fixture.CatalogPath);
        var catalog = await CreateCatalogAsync();
        var import = CropImport(catalog);

        Assert.Equal(XmpFactKind.Unsupported, Assert.Single(source.Records).Crop!.Kind);
        var preview = await import.CreatePreviewAsync(source, Map(source, photos),
            CatalogImportPolicy.LightroomWins, importCrops: true);
        var result = await import.ApplyAsync(preview);

        Assert.Equal(1, preview.Report.Crop!.Unsupported);
        Assert.Empty(result.Adoptions);
        Assert.Equal(0, result.DatabaseWrites);
        Assert.Equal(0, await CountImagesAsync());
    }

    [Fact]
    public async Task Crops_AreOptInAndAdoptOnlyIntoEmptyGeometry()
    {
        var photos = Directory.CreateDirectory(Path.Combine(_root.Path, "photos")).FullName;
        var photo = Path.Combine(photos, "keeper.jpg");
        var catalog = await CreateCatalogAsync();
        var id = await catalog.GetOrCreateImageAsync(photo);
        var source = Source(photos, "keeper.jpg", crop: CropFact());
        var pings = 0;
        var import = CropImport(catalog,
            (string _, out int orientation) =>
            { pings++; orientation = 1; return true; });

        var automatic = await import.CreatePreviewAsync(
            source, Map(source, photos), CatalogImportPolicy.LightroomWins);
        Assert.Equal(0, automatic.Report.Crop!.Written);
        Assert.Equal(0, pings);

        var preview = await import.CreatePreviewAsync(source, Map(source, photos),
            CatalogImportPolicy.LightroomWins, importCrops: true);
        var result = await import.ApplyAsync(preview);

        Assert.Equal(1, pings);
        Assert.Equal(1, preview.Report.Crop!.Written);
        Assert.NotNull(Assert.Single(result.Adoptions).AdoptedCrop);
        var state = (await catalog.LoadImageStatesAsync([photo]))[photo].Single();
        Assert.Equal(.1, state.EditSettings.Crop!.Left, 6);
        Assert.Equal("Crop from Lightroom",
            Assert.Single((await catalog.LoadEditHistoryAsync(id)).Entries,
                entry => entry.Sequence == 1).Label);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task CropOnlyNonActionableRows_DoNotReachApply(
        bool importCrops,
        bool unsupported)
    {
        var photos = Directory.CreateDirectory(Path.Combine(_root.Path, "photos")).FullName;
        var source = Source(photos, "crop-only.jpg",
            crop: unsupported ? LightroomCropFact.Unsupported : CropFact());
        var catalog = await CreateCatalogAsync();
        var import = CropImport(catalog);

        var preview = await import.CreatePreviewAsync(source, Map(source, photos),
            CatalogImportPolicy.LightroomWins, importCrops);
        var result = await import.ApplyAsync(preview);

        Assert.Empty(preview.Changes);
        Assert.Equal(0, preview.Report.MatchedPhotos);
        Assert.Equal(0, preview.Report.NewlyStoredPaths);
        Assert.Equal(0, result.DatabaseWrites);
        Assert.Equal(0, await CountImagesAsync());
        Assert.Null(await catalog.GetAppSettingAsync(preview.SettingsKey));
    }

    [Fact]
    public async Task CropOnlyApply_LeavesAssessmentRecencyUntouched()
    {
        var photos = Directory.CreateDirectory(Path.Combine(_root.Path, "photos")).FullName;
        var photo = Path.Combine(photos, "keeper.jpg");
        var catalog = await CreateCatalogAsync();
        var id = await catalog.GetOrCreateImageAsync(photo);
        await catalog.MutateAssessmentsAsync([
            new AssessmentMutation(id, AssessmentAxes.Rating, Rating: 3)
        ]);
        var before = (await catalog.LoadImageStatesAsync([photo]))[photo].Single();
        var source = Source(photos, "keeper.jpg", crop: CropFact());
        var import = CropImport(catalog);

        var preview = await import.CreatePreviewAsync(source, Map(source, photos),
            CatalogImportPolicy.LightroomWins, importCrops: true);
        var result = await import.ApplyAsync(preview);
        var after = (await catalog.LoadImageStatesAsync([photo]))[photo].Single();

        Assert.Equal(AssessmentAxes.None, Assert.Single(preview.Changes).Axes);
        Assert.Equal(before.AssessmentRevision, after.AssessmentRevision);
        Assert.Equal(before.AssessedUtc, after.AssessedUtc);
        var adoption = Assert.Single(result.Adoptions);
        Assert.Equal(before.AssessmentRevision, adoption.Snapshot.Revision);
        Assert.Equal(before.AssessedUtc, adoption.Snapshot.AssessedUtc);
    }

    [Fact]
    public async Task CropPreview_ReportsMatchPreservedAndUnsupportedBuckets()
    {
        var photos = Directory.CreateDirectory(Path.Combine(_root.Path, "photos")).FullName;
        var catalog = await CreateCatalogAsync();
        var paths = new[] { "match.jpg", "geometry.jpg", "mismatch.jpg" }
            .Select(name => Path.Combine(photos, name)).ToArray();
        var states = await catalog.LoadOrCreateImageStatesAsync(paths);
        await catalog.SaveEditSettingsAsync(states[paths[0]].Single().CatalogId,
            new EditSettings { Crop = Crop() });
        await catalog.SaveEditSettingsAsync(states[paths[1]].Single().CatalogId,
            new EditSettings { Rotation = 90 });
        var source = new LightroomCatalogContents(_root.Path + ".lrcat", 1504001, 15,
            false, AssessmentAxes.All, [new CatalogSourceRoot("D:/", 3)],
            paths.Select((path, index) => Record("D:/", Path.GetFileName(path),
                crop: CropFact(index == 2 ? "BC" : "AB"))).ToArray(), []);
        var import = CropImport(catalog);

        var report = (await import.CreatePreviewAsync(source, Map(source, photos),
            CatalogImportPolicy.LightroomWins, importCrops: true)).Report;

        Assert.Equal(1, report.Crop!.Unchanged);
        Assert.Equal(1, report.Crop.PreservedByPolicy);
        Assert.Equal(1, report.Crop.Unsupported);
        Assert.Contains(report.InformationalOutcomes, text => text.Contains("example:"));
    }

    [Fact]
    public async Task CropPreview_UnavailableSourceIsUnsupportedWithoutPing()
    {
        var photos = Directory.CreateDirectory(Path.Combine(_root.Path, "photos")).FullName;
        var catalog = await CreateCatalogAsync();
        var source = Source(photos, "online.jpg", crop: CropFact());
        var pings = 0;
        var import = new CatalogImportService(catalog, _ => true,
            new TestSourceAvailabilityService(SourceAvailability.RequiresHydration),
            (string _, out int orientation) =>
            { pings++; orientation = 1; return true; });

        var report = (await import.CreatePreviewAsync(source, Map(source, photos),
            CatalogImportPolicy.LightroomWins, importCrops: true)).Report;

        Assert.Equal(1, report.Crop!.Unsupported);
        Assert.Equal(0, pings);
    }

    [Fact]
    public async Task CropApply_RereadsSettingsSoToneSurvivesAndGeometryRaceSkips()
    {
        var photos = Directory.CreateDirectory(Path.Combine(_root.Path, "photos")).FullName;
        var tonalPath = Path.Combine(photos, "tonal.jpg");
        var racedPaths = new[] { "crop.jpg", "rotation.jpg", "horizon.jpg", "geometry.jpg" }
            .Select(name => Path.Combine(photos, name)).ToArray();
        var catalog = await CreateCatalogAsync();
        var paths = new[] { tonalPath }.Concat(racedPaths).ToArray();
        var states = await catalog.LoadOrCreateImageStatesAsync(paths);
        var source = new LightroomCatalogContents(_root.Path + ".lrcat", 1504001, 15,
            false, AssessmentAxes.All, [new CatalogSourceRoot("D:/", paths.Length)],
            paths.Select(path => Record("D:/", Path.GetFileName(path), crop: CropFact())).ToArray(), []);
        var import = CropImport(catalog);
        var preview = await import.CreatePreviewAsync(source, Map(source, photos),
            CatalogImportPolicy.LightroomWins, importCrops: true);
        await catalog.SaveEditSettingsAsync(states[tonalPath].Single().CatalogId,
            new EditSettings { Exposure = 1.25 });
        await catalog.SaveEditSettingsAsync(states[racedPaths[0]].Single().CatalogId,
            new EditSettings { Crop = new CropRegion { Left = .2, Right = .8 } });
        await catalog.SaveEditSettingsAsync(states[racedPaths[1]].Single().CatalogId,
            new EditSettings { Rotation = 90 });
        await catalog.SaveEditSettingsAsync(states[racedPaths[2]].Single().CatalogId,
            new EditSettings { HorizonRotation = 1 });
        await catalog.SaveEditSettingsAsync(states[racedPaths[3]].Single().CatalogId,
            new EditSettings { Geometry = new GeometrySettings { Vertical = 1 } });

        var result = await import.ApplyAsync(preview);
        var after = await catalog.LoadImageStatesAsync(paths);

        Assert.Equal(1.25, after[tonalPath].Single().EditSettings.Exposure);
        Assert.NotNull(after[tonalPath].Single().EditSettings.Crop);
        Assert.Equal(.2, after[racedPaths[0]].Single().EditSettings.Crop!.Left);
        foreach (var path in racedPaths.Skip(1))
            Assert.Null(after[path].Single().EditSettings.Crop);
        Assert.Single(result.Adoptions, adoption => adoption.AdoptedCrop != null);
    }

    [Fact]
    public async Task RawHiddenOrientation_StillAdoptsAbCropAndMismatchRejects()
    {
        // RAW containers hide EXIF from the header ping (orientation 0):
        // AB crops still adopt; a contradicting known value rejects.
        var photos = Directory.CreateDirectory(Path.Combine(_root.Path, "photos")).FullName;
        var catalog = await CreateCatalogAsync();
        await catalog.GetOrCreateImageAsync(Path.Combine(photos, "raw.raf"));
        await catalog.GetOrCreateImageAsync(Path.Combine(photos, "rotated.jpg"));
        var source = new LightroomCatalogContents(
            Path.Combine(_root.Path, "source.lrcat"), 1303001, 13, true,
            AssessmentAxes.All, [new CatalogSourceRoot(photos, 2)],
            [Record(photos, "raw.raf", crop: CropFact()),
             Record(photos, "rotated.jpg", crop: CropFact())], []);
        var import = CropImport(catalog, (string path, out int orientation) =>
        {
            orientation = Path.GetFileName(path) == "raw.raf" ? 0 : 6;
            return true;
        });

        var preview = await import.CreatePreviewAsync(source, Map(source, photos),
            CatalogImportPolicy.LightroomWins, importCrops: true);
        var result = await import.ApplyAsync(preview);

        Assert.Equal(1, preview.Report.Crop!.Written);
        Assert.Equal(1, preview.Report.Crop.Unsupported);
        var adopted = Assert.Single(result.Adoptions, a => a.AdoptedCrop != null);
        var rawPath = Path.Combine(photos, "raw.raf");
        Assert.NotNull((await catalog.LoadImageStatesAsync([rawPath]))[rawPath]
            .Single().EditSettings.Crop);
        var jpgPath = Path.Combine(photos, "rotated.jpg");
        Assert.Null((await catalog.LoadImageStatesAsync([jpgPath]))[jpgPath]
            .Single().EditSettings.Crop);
    }

    private static CatalogImportService CropImport(
        CatalogService catalog,
        TryReadExifOrientation? readOrientation = null) =>
        new(catalog, _ => true,
            new TestSourceAvailabilityService(SourceAvailability.AvailableLocally),
            readOrientation ?? ((string _, out int orientation) =>
            { orientation = 1; return true; }));
}
