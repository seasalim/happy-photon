using HappyPhoton.Models;
using HappyPhoton.Services;
using Microsoft.Data.Sqlite;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class CatalogVersionTests : IDisposable
{
    private readonly TemporaryDirectory _root = new();

    [Fact]
    public async Task Versions_CopySettings_IsolateAssessments_AndReuseNumbers()
    {
        using var catalog = new CatalogService(_root.Path);
        await catalog.InitializeAsync();
        var path = Path.Combine(_root.Path, "photo.jpg");
        var primary = await catalog.GetOrCreateImageAsync(path);
        await catalog.SaveEditSettingsAsync(primary, new EditSettings { Exposure = 0 });
        var sourceAssessment = Assert.Single(await catalog.MutateAssessmentsAsync([
            new AssessmentMutation(
                primary,
                AssessmentAxes.Rating | AssessmentAxes.Flag | AssessmentAxes.Label,
                ImageFlag.Picked,
                5,
                ColorLabel.Red,
                AssessmentAxes.Rating | AssessmentAxes.Flag | AssessmentAxes.Label)
        ]));

        var second = (await catalog.CreateVersionAsync(primary))!;
        Assert.NotNull(second);
        Assert.Equal(ImageFlag.Picked, second.Flag);
        Assert.Equal(5, second.Rating);
        Assert.Equal(ColorLabel.Red, second.ColorLabel);
        Assert.Equal(sourceAssessment.Revision, second.AssessmentRevision);
        Assert.Equal(sourceAssessment.AssessedUtc, second.AssessedUtc);
        Assert.Equal(AssessmentAxes.None, second.PendingAxes);
        await catalog.SaveEditSettingsAsync(second.CatalogId,
            new EditSettings { Exposure = 1 });
        await catalog.MutateAssessmentsAsync([
            new AssessmentMutation(second.CatalogId, AssessmentAxes.Rating, Rating: 4)
        ]);

        var versions = (await catalog.LoadImageStatesAsync([path]))[path];
        Assert.Equal([1, 2], versions.Select(version => version.Version));
        Assert.Equal(0, versions[0].EditSettings.Exposure);
        Assert.Equal(5, versions[0].Rating);
        Assert.Equal(1, versions[1].EditSettings.Exposure);
        Assert.Equal(4, versions[1].Rating);
        Assert.All(versions, version => Assert.Equal(ImageFlag.Picked, version.Flag));
        Assert.All(versions, version => Assert.Equal(ColorLabel.Red, version.ColorLabel));
        Assert.Equal(
            AssessmentAxes.Rating | AssessmentAxes.Flag | AssessmentAxes.Label,
            versions[0].PendingAxes);
        Assert.Equal(AssessmentAxes.None, versions[1].PendingAxes);

        for (var expected = 3; expected <= 8; expected++)
            Assert.Equal(expected,
                (await catalog.CreateVersionAsync(primary))?.Version);
        Assert.Null(await catalog.CreateVersionAsync(primary));
        Assert.True(await catalog.DeleteVersionAsync(versions[1].CatalogId));
        Assert.Equal(2, (await catalog.CreateVersionAsync(primary))?.Version);
    }

    [Fact]
    public async Task DeleteVersion_ProtectsPrimaryAndRemovesOnlySiblingAssets()
    {
        using var catalog = new CatalogService(_root.Path);
        await catalog.InitializeAsync();
        var path = Path.Combine(_root.Path, "source.jpg");
        await File.WriteAllTextAsync(path, "original");
        var primary = await catalog.GetOrCreateImageAsync(path);
        var second = (await catalog.CreateVersionAsync(primary))!;
        Assert.NotNull(second);
        foreach (var asset in new[]
                 {
                     catalog.GetThumbnailPath(primary),
                     catalog.GetPreviewPath(primary),
                     catalog.GetRenderedThumbnailPath(primary),
                     catalog.GetThumbnailPath(second.CatalogId),
                     catalog.GetPreviewPath(second.CatalogId),
                     catalog.GetRenderedThumbnailPath(second.CatalogId)
                 })
        {
            Directory.CreateDirectory(Path.GetDirectoryName(asset)!);
            await File.WriteAllTextAsync(asset, "cache");
        }

        Assert.False(await catalog.DeleteVersionAsync(primary));
        Assert.True(await catalog.DeleteVersionAsync(second.CatalogId));

        Assert.True(File.Exists(path));
        Assert.True(File.Exists(catalog.GetThumbnailPath(primary)));
        Assert.True(File.Exists(catalog.GetPreviewPath(primary)));
        Assert.True(File.Exists(catalog.GetRenderedThumbnailPath(primary)));
        Assert.False(File.Exists(catalog.GetThumbnailPath(second.CatalogId)));
        Assert.False(File.Exists(catalog.GetPreviewPath(second.CatalogId)));
        Assert.False(File.Exists(catalog.GetRenderedThumbnailPath(second.CatalogId)));
        Assert.Single((await catalog.LoadImageStatesAsync([path]))[path]);
    }

    [Fact]
    public async Task Rename_BlankFallsBackAndMixedVersionPendingIsPerMutation()
    {
        using var catalog = new CatalogService(_root.Path);
        await catalog.InitializeAsync();
        var path = Path.Combine(_root.Path, "photo.jpg");
        var primary = await catalog.GetOrCreateImageAsync(path);
        var second = (await catalog.CreateVersionAsync(primary))!;
        Assert.NotNull(second);
        await catalog.RenameVersionAsync(second.CatalogId, "Monochrome");
        await catalog.MutateAssessmentsAsync([
            new AssessmentMutation(primary, AssessmentAxes.Rating, Rating: 3,
                PendingAxes: AssessmentAxes.Rating),
            new AssessmentMutation(second.CatalogId, AssessmentAxes.Rating, Rating: 5)
        ]);

        var named = (await catalog.LoadImageStatesAsync([path]))[path];
        Assert.Equal("Monochrome", named[1].VersionLabel);
        Assert.Equal(AssessmentAxes.Rating, named[0].PendingAxes);
        Assert.Equal(AssessmentAxes.None, named[1].PendingAxes);

        await catalog.RenameVersionAsync(second.CatalogId, "  ");
        Assert.Null((await catalog.LoadImageStatesAsync([path]))[path][1].VersionLabel);
    }

    [Fact]
    public void ExportNaming_IsJobScopedAndUsesStableVersionNumbers()
    {
        var path = Path.Combine(_root.Path, "IMG.cr3");
        var first = new ImageFile(path) { Version = 1 };
        var second = new ImageFile(path) { Version = 2, VersionLabel = "B&W" };
        var settings = new ExportSettings
        {
            OutputFolder = _root.Path,
            NamingPattern = "{name}"
        };
        var recipe = new[] { new ExportVariant("hi-res", null) };

        Assert.Equal("IMG.jpg", Path.GetFileName(
            ExportJob.Create([first], settings, recipe, false).Targets.Single().ResolvedPath));
        Assert.Equal("IMG.jpg", Path.GetFileName(
            ExportJob.Create([second], settings, recipe, false).Targets.Single().ResolvedPath));
        var together = ExportJob.Create([first, second], settings, recipe, false);
        Assert.Equal(["IMG-V1.jpg", "IMG-V2.jpg"],
            together.Targets.Select(target => Path.GetFileName(target.ResolvedPath)));
        Assert.False(together.HasPathCollisions);
        Assert.Equal("V1", first.VersionReportLabel);
        Assert.Equal("V2 · B&W", second.VersionReportLabel);
    }

    public void Dispose() => _root.Dispose();
}

public sealed class CatalogVersionMigrationTests : IDisposable
{
    private readonly TemporaryDirectory _root = new();

    [Fact]
    public async Task VersionTwo_MigratesIdsAssessmentsSequenceAndCreatesExactBackup()
    {
        var database = await SeedVersionTwoAsync(failMigration: false);
        var before = await File.ReadAllBytesAsync(database);

        using (var catalog = new CatalogService(_root.Path))
            await catalog.InitializeAsync();

        var backup = database + ".pre-versions-backup";
        Assert.Equal(before, await File.ReadAllBytesAsync(backup));
        await using (var backupConnection = await OpenAsync(backup))
        {
            using var backupCommand = backupConnection.CreateCommand();
            backupCommand.CommandText =
                "SELECT value FROM app_settings WHERE key='schema_version';";
            Assert.Equal("2", await backupCommand.ExecuteScalarAsync());
            backupCommand.CommandText =
                "SELECT id, file_path, edit_settings FROM images ORDER BY id;";
            using var backupRows = await backupCommand.ExecuteReaderAsync();
            var backupIds = new List<long>();
            while (await backupRows.ReadAsync())
            {
                backupIds.Add(backupRows.GetInt64(0));
                Assert.Equal(EditSettingsJson.Serialize(
                    new EditSettings { Exposure = 1.25 }), backupRows.GetString(2));
            }
            Assert.Equal([4L, 5L, 6L], backupIds);
        }
        await using var connection = await OpenAsync(database);
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT id, version, edit_settings FROM images ORDER BY id;";
        var rows = new List<(long Id, long Version)>();
        using (var reader = await command.ExecuteReaderAsync())
            while (await reader.ReadAsync())
            {
                rows.Add((reader.GetInt64(0), reader.GetInt64(1)));
                Assert.Equal(EditSettingsJson.Serialize(
                    new EditSettings { Exposure = 1.25 }), reader.GetString(2));
            }
        Assert.Equal([(4L, 1L), (5L, 1L), (6L, 1L)], rows);
        command.CommandText = "SELECT seq FROM sqlite_sequence WHERE name='images';";
        Assert.True((long)(await command.ExecuteScalarAsync())! >= 12);
        command.CommandText = "PRAGMA foreign_key_check;";
        Assert.Null(await command.ExecuteScalarAsync());
        command.CommandText =
            "SELECT revision, assessed_utc, pending_axes FROM image_assessments;";
        using var assessment = await command.ExecuteReaderAsync();
        Assert.True(await assessment.ReadAsync());
        Assert.Equal(7, assessment.GetInt64(0));
        Assert.Equal("2026-08-17T12:00:00.0000000Z", assessment.GetString(1));
        Assert.Equal(5, assessment.GetInt64(2));
    }

    [Fact]
    public async Task FailedMigration_LeavesExactBackupIntact()
    {
        var database = await SeedVersionTwoAsync(failMigration: true);
        var before = await File.ReadAllBytesAsync(database);
        using var catalog = new CatalogService(_root.Path);

        await Assert.ThrowsAnyAsync<Exception>(catalog.InitializeAsync);

        Assert.Equal(before, await File.ReadAllBytesAsync(
            database + ".pre-versions-backup"));
    }

    private async Task<string> SeedVersionTwoAsync(bool failMigration)
    {
        var database = Path.Combine(_root.Path, "catalog.db");
        await using var connection = await OpenAsync(database);
        using var command = connection.CreateCommand();
        var json = EditSettingsJson.Serialize(new EditSettings { Exposure = 1.25 });
        command.CommandText = """
            CREATE TABLE images (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                file_path TEXT NOT NULL COLLATE NOCASE UNIQUE,
                file_name TEXT NOT NULL, edit_settings TEXT NOT NULL,
                edit_version INTEGER NOT NULL, flag_state INTEGER NOT NULL DEFAULT 0,
                rating INTEGER NOT NULL DEFAULT 0, color_label INTEGER NOT NULL DEFAULT 0,
                updated_utc TEXT);
            CREATE TABLE app_settings (key TEXT PRIMARY KEY, value TEXT);
            CREATE TABLE image_assessments (
                image_id INTEGER PRIMARY KEY, revision INTEGER NOT NULL,
                assessed_utc TEXT NOT NULL, pending_axes INTEGER NOT NULL DEFAULT 0,
                FOREIGN KEY (image_id) REFERENCES images(id) ON DELETE CASCADE);
            INSERT INTO app_settings VALUES ('schema_version', '2');
            INSERT INTO images (id, file_path, file_name, edit_settings, edit_version,
                flag_state, rating, color_label) VALUES
                (4, 'a.jpg', 'a.jpg', @json, 3, 0, 0, 0),
                (5, 'b.jpg', 'b.jpg', @json, 3, 1, 5, 1),
                (6, 'c.jpg', 'c.jpg', @json, 3, 0, 0, 0);
            UPDATE sqlite_sequence SET seq = 12 WHERE name = 'images';
            INSERT INTO image_assessments VALUES (
                5, 7, '2026-08-17T12:00:00.0000000Z', 5);
            """;
        command.Parameters.AddWithValue("@json", json);
        await command.ExecuteNonQueryAsync();
        if (failMigration)
        {
            command.Parameters.Clear();
            command.CommandText = """
                CREATE TRIGGER reject_version_three
                BEFORE UPDATE ON app_settings
                WHEN NEW.key = 'schema_version' AND NEW.value = '3'
                BEGIN SELECT RAISE(FAIL, 'reject version'); END;
                """;
            await command.ExecuteNonQueryAsync();
        }
        return database;
    }

    private static async Task<SqliteConnection> OpenAsync(string path)
    {
        var connection = new SqliteConnection($"Data Source={path};Pooling=False");
        await connection.OpenAsync();
        return connection;
    }

    public void Dispose() => _root.Dispose();
}

public sealed class VersionViewModelTests : IDisposable
{
    private readonly CatalogVmFixture _fixture = new("versions-vm");

    [Fact]
    public async Task FolderLoadFansOutAndCommandSelectsSiblingThenToastsInDevelop()
    {
        using var catalog = await _fixture.CreateCatalogAsync("catalog");
        var folder = Directory.CreateDirectory(_fixture.Path("photos")).FullName;
        var path = Path.Combine(folder, "photo.jpg");
        await File.WriteAllTextAsync(path, "source");
        var primary = await catalog.GetOrCreateImageAsync(path);
        await catalog.SaveEditSettingsAsync(primary,
            new EditSettings { Exposure = 0.75 });
        Assert.NotNull(await catalog.CreateVersionAsync(primary));
        await using var viewModel = _fixture.CreateViewModel(
            catalog,
            loadMetadataAsync: _ => Task.CompletedTask,
            availabilityService: new TestSourceAvailabilityService(
                SourceAvailability.RequiresHydration),
            postSelection: action => action());

        await viewModel.LoadFolderAsync(folder);

        Assert.Equal([1, 2], viewModel.Browse.AllImages
            .Select(image => image.Version));
        Assert.All(viewModel.Browse.AllImages,
            image => Assert.Equal(0.75, image.EditSettings.Exposure));
        var second = viewModel.Browse.AllImages[1];
        viewModel.SelectedImage = second;
        viewModel.Browse.SelectOnly(second);
        Assert.True(viewModel.NewVersionFromCurrentCommand.CanExecute(null));
        await viewModel.NewVersionFromCurrentCommand.ExecuteAsync(null);

        Assert.Equal(3, viewModel.Browse.AllImages.Count);
        Assert.Equal(3, viewModel.SelectedImage?.Version);
        Assert.True(viewModel.SelectedImage?.IsSelected);

        viewModel.WorkspaceMode = WorkspaceMode.Develop;
        await viewModel.NewVersionFromCurrentCommand.ExecuteAsync(null);
        Assert.Equal("Version 4", viewModel.AssessmentFeedback);
        Assert.True(viewModel.IsAssessmentFeedbackVisible);
        Assert.Contains("V4", viewModel.ActiveFileName);
    }

    [Fact]
    public async Task ShortcutScopeAndCapExposeDisabledReason()
    {
        using var catalog = await _fixture.CreateCatalogAsync("scope-catalog");
        await using var viewModel = _fixture.CreateViewModel(catalog,
            availabilityService: new TestSourceAvailabilityService(
                SourceAvailability.RequiresHydration));
        var image = new ImageFile(_fixture.Path("photo.jpg"))
        {
            CatalogId = await catalog.GetOrCreateImageAsync(
                _fixture.Path("photo.jpg"))
        };
        viewModel.Browse.SetImages([image]);
        viewModel.SelectedImage = image;

        Assert.True(viewModel.NewVersionFromCurrentCommand.CanExecute(null));
        viewModel.WorkspaceMode = WorkspaceMode.Develop;
        Assert.True(viewModel.NewVersionFromCurrentCommand.CanExecute(null));
        viewModel.WorkspaceMode = WorkspaceMode.Export;
        Assert.False(viewModel.NewVersionFromCurrentCommand.CanExecute(null));
        viewModel.WorkspaceMode = WorkspaceMode.Browse;
        viewModel.IsFullScreenMode = true;
        Assert.False(viewModel.NewVersionFromCurrentCommand.CanExecute(null));
        viewModel.IsFullScreenMode = false;
        image.VersionCount = 8;
        Assert.False(viewModel.NewVersionFromCurrentCommand.CanExecute(null));
        Assert.Equal("A file can have at most 8 versions.",
            image.CreateVersionToolTip);
    }

    [Fact]
    public async Task MixedVersionBrowseAssessment_LeavesSiblingWithoutPendingXmpAxes()
    {
        using var catalog = await _fixture.CreateCatalogAsync("mixed-xmp-catalog");
        var folder = Directory.CreateDirectory(_fixture.Path("mixed-xmp-photos")).FullName;
        var path = Path.Combine(folder, "photo.jpg");
        await File.WriteAllTextAsync(path, "source");
        var primaryId = await catalog.GetOrCreateImageAsync(path);
        Assert.NotNull(await catalog.CreateVersionAsync(primaryId));
        await using var viewModel = _fixture.CreateViewModel(
            catalog,
            loadMetadataAsync: _ => Task.CompletedTask,
            availabilityService: new TestSourceAvailabilityService(
                SourceAvailability.RequiresHydration),
            postSelection: action => action());
        await viewModel.LoadFolderAsync(folder);
        viewModel.XmpSidecarMode = XmpSidecarMode.ReadWrite;
        var primary = viewModel.Browse.AllImages.Single(image => image.Version == 1);
        var sibling = viewModel.Browse.AllImages.Single(image => image.Version == 2);
        viewModel.Browse.SelectOnly(primary);
        viewModel.Browse.ToggleSelection(sibling);
        viewModel.SelectedImage = primary;

        await viewModel.SetRatingCommand.ExecuteAsync(5);

        var states = (await catalog.LoadImageStatesAsync([path]))[path];
        Assert.All(states, state => Assert.Equal(5, state.Rating));
        Assert.Equal(AssessmentAxes.Rating, states[0].PendingAxes);
        Assert.Equal(AssessmentAxes.None, states[1].PendingAxes);
    }

    public void Dispose() => _fixture.Dispose();
}
