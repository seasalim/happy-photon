using HappyPhoton.Models;
using HappyPhoton.Services;
using Microsoft.Data.Sqlite;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class CatalogImportServiceTests : IDisposable
{
    private readonly string _root = Directory.CreateDirectory(Path.Combine(
        Path.GetTempPath(), $"happy-photon-import-tests-{Guid.NewGuid():N}")).FullName;
    private CatalogService? _catalog;

    [Fact]
    public async Task Apply_NormalizesLightroomPathAndFolderScanFindsExactlyOneRow()
    {
        var photos = Directory.CreateDirectory(Path.Combine(_root, "photos")).FullName;
        var photo = Path.Combine(photos, "keeper.JPG");
        File.WriteAllBytes(photo, [1]);
        var catalog = await CreateCatalogAsync();
        var service = new CatalogImportService(catalog);
        var source = Source(photos.Replace('\\', '/') + "/", "keeper.JPG",
            rating: CatalogImportFact<int>.Mapped(5));

        var preview = await service.CreatePreviewAsync(
            source, new Dictionary<string, string>(), CatalogImportPolicy.LightroomWins);
        await service.ApplyAsync(preview);
        await catalog.LoadOrCreateImageStatesAsync([photo]);

        Assert.Equal(1, await CountImagesAsync());
        Assert.Equal(5, (await catalog.LoadImageStatesAsync([photo]))[photo].Rating);
    }

    [Fact]
    public async Task Preview_CaseVariantMatchesExistingCatalogIdentity()
    {
        var photos = Directory.CreateDirectory(Path.Combine(_root, "photos")).FullName;
        var photo = Path.Combine(photos, "keeper.jpg");
        var catalog = await CreateCatalogAsync();
        var id = await catalog.GetOrCreateImageAsync(photo);
        await catalog.MutateAssessmentsAsync([
            new AssessmentMutation(id, AssessmentAxes.Rating, Rating: 2)
        ], AssessmentAxes.None);
        var source = Source(photos, "KEEPER.JPG",
            rating: CatalogImportFact<int>.Mapped(5));
        var import = new CatalogImportService(catalog);

        var preview = await import.CreatePreviewAsync(
            source, Map(source, photos), CatalogImportPolicy.LightroomWins);
        await import.ApplyAsync(preview);

        Assert.Equal(1, preview.Report.ExistingCatalogRows);
        Assert.Equal(0, preview.Report.NewlyStoredPaths);
        Assert.Equal(1, await CountImagesAsync());
        Assert.Equal(5, (await catalog.LoadImageStatesAsync([photo]))[photo].Rating);
    }

    [Fact]
    public async Task Preview_CaseVariantsCollapseToOneCatalogIdentity()
    {
        var photos = Directory.CreateDirectory(Path.Combine(_root, "photos")).FullName;
        const string sourceRoot = "D:/Photos/";
        var source = new LightroomCatalogContents(
            Path.Combine(_root, "source.lrcat"), 1303001, 13, true,
            AssessmentAxes.All, [new CatalogSourceRoot(sourceRoot, 2)],
            [
                Record(sourceRoot, "keeper.jpg", CatalogImportFact<int>.Mapped(3)),
                Record(sourceRoot, "KEEPER.JPG", CatalogImportFact<int>.Mapped(5))
            ], []);
        var catalog = await CreateCatalogAsync();
        var import = new CatalogImportService(catalog);

        var preview = await import.CreatePreviewAsync(
            source, Map(source, photos), CatalogImportPolicy.LightroomWins);
        await import.ApplyAsync(preview);

        var importedPath = Assert.Single(preview.ImportedPaths);
        Assert.Equal(1, preview.Report.MatchedPhotos);
        Assert.Equal(1, await CountImagesAsync());
        Assert.Equal(5,
            (await catalog.LoadImageStatesAsync([importedPath]))[importedPath].Rating);
        Assert.Contains(preview.Report.InformationalOutcomes,
            message => message ==
                "1 additional Lightroom record mapped to 1 destination path already used by another record. The later record was used.");
    }

    [Fact]
    public void NormalizeMappedPath_RewritesMixedSeparatorsAndPreservesCase()
    {
        var actual = CatalogImportService.NormalizeMappedPath(
            _root, "/Year\\Shoot/Keeper.JPG");

        Assert.Equal(
            Path.Combine(_root, "Year", "Shoot", "Keeper.JPG"),
            actual);
    }

    [Theory]
    [InlineData("../outside.jpg")]
    [InlineData("year/../../outside.jpg")]
    [InlineData("C:\\outside.jpg")]
    public void NormalizeMappedPath_RejectsEscapesAndForeignAbsoluteRemainders(
        string relativePath)
    {
        Assert.Throws<InvalidDataException>(() =>
            CatalogImportService.NormalizeMappedPath(_root, relativePath));
    }

    [Fact]
    public async Task Apply_NeverClearsLocalValuesAndUnsupportedLabelIsPreserved()
    {
        var photos = Directory.CreateDirectory(Path.Combine(_root, "photos")).FullName;
        var photo = Path.Combine(photos, "keeper.jpg");
        var catalog = await CreateCatalogAsync();
        var id = await catalog.GetOrCreateImageAsync(photo);
        await catalog.MutateAssessmentsAsync([
            new AssessmentMutation(id, AssessmentAxes.Rating | AssessmentAxes.Label,
                Rating: 4, ColorLabel: ColorLabel.Red)
        ], AssessmentAxes.None);
        var source = Source(photos, "keeper.jpg",
            label: CatalogImportFact<ColorLabel>.Unsupported("Client Pick"));
        var import = new CatalogImportService(catalog);

        foreach (var policy in Enum.GetValues<CatalogImportPolicy>())
        {
            var preview = await import.CreatePreviewAsync(
                source, Map(source, photos), policy);
            var result = await import.ApplyAsync(preview);
            var state = (await catalog.LoadImageStatesAsync([photo]))[photo];
            Assert.Equal(4, state.Rating);
            Assert.Equal(ColorLabel.Red, state.ColorLabel);
            Assert.Equal(1, result.Report.ColorLabel.Unsupported);
        }
    }

    [Fact]
    public async Task Policies_OverwriteOrFillOnlyPerAxis()
    {
        var photos = Directory.CreateDirectory(Path.Combine(_root, "photos")).FullName;
        var first = Path.Combine(photos, "first.jpg");
        var second = Path.Combine(photos, "second.jpg");
        var catalog = await CreateCatalogAsync();
        var states = await catalog.LoadOrCreateImageStatesAsync([first, second]);
        await catalog.MutateAssessmentsAsync([
            new AssessmentMutation(states[first].CatalogId, AssessmentAxes.Rating, Rating: 2)
        ], AssessmentAxes.None);
        var source = new LightroomCatalogContents(
            Path.Combine(_root, "source.lrcat"), 1303001, 13, true,
            AssessmentAxes.All, [new CatalogSourceRoot("D:/Photos/", 2)],
            [
                Record("D:/Photos/", "first.jpg", CatalogImportFact<int>.Mapped(5)),
                Record("D:/Photos/", "second.jpg", CatalogImportFact<int>.Mapped(4))
            ], []);
        var import = new CatalogImportService(catalog);

        var fill = await import.CreatePreviewAsync(
            source, Map(source, photos), CatalogImportPolicy.FillEmptyOnly);
        await import.ApplyAsync(fill);
        var afterFill = await catalog.LoadImageStatesAsync([first, second]);
        Assert.Equal(2, afterFill[first].Rating);
        Assert.Equal(4, afterFill[second].Rating);

        var overwrite = await import.CreatePreviewAsync(
            source, Map(source, photos), CatalogImportPolicy.LightroomWins);
        await import.ApplyAsync(overwrite);
        Assert.Equal(5, (await catalog.LoadImageStatesAsync([first]))[first].Rating);
    }

    [Fact]
    public async Task IdenticalRerun_PerformsZeroWritesAndLeavesSettingsUnchanged()
    {
        var photos = Directory.CreateDirectory(Path.Combine(_root, "photos")).FullName;
        var source = Source(photos, "keeper.jpg",
            flag: CatalogImportFact<ImageFlag>.Mapped(ImageFlag.Picked));
        var catalog = await CreateCatalogAsync();
        var import = new CatalogImportService(catalog);
        var firstPreview = await import.CreatePreviewAsync(
            source, Map(source, photos), CatalogImportPolicy.LightroomWins);
        await import.ApplyAsync(firstPreview);
        var stored = await catalog.GetAppSettingAsync(firstPreview.SettingsKey);
        var firstState = (await catalog.LoadImageStatesAsync(firstPreview.ImportedPaths))
            .Values.Single();

        var secondPreview = await import.CreatePreviewAsync(
            source, Map(source, photos), CatalogImportPolicy.LightroomWins);
        var second = await import.ApplyAsync(secondPreview);
        var secondState = (await catalog.LoadImageStatesAsync(secondPreview.ImportedPaths))
            .Values.Single();

        Assert.Equal(0, second.DatabaseWrites);
        Assert.Equal(stored, await catalog.GetAppSettingAsync(secondPreview.SettingsKey));
        Assert.Equal(firstState.CatalogId, secondState.CatalogId);
        Assert.Equal(firstState.Flag, secondState.Flag);
        Assert.Equal(firstState.Rating, secondState.Rating);
        Assert.Equal(firstState.ColorLabel, secondState.ColorLabel);
        Assert.Equal(firstState.AssessmentRevision, secondState.AssessmentRevision);
        Assert.Equal(firstState.AssessedUtc, secondState.AssessedUtc);
        Assert.Equal(firstState.PendingAxes, secondState.PendingAxes);
    }

    [Fact]
    public async Task PreviewRace_RollsBackEverything()
    {
        var photos = Directory.CreateDirectory(Path.Combine(_root, "photos")).FullName;
        var photo = Path.Combine(photos, "keeper.jpg");
        var catalog = await CreateCatalogAsync();
        var id = await catalog.GetOrCreateImageAsync(photo);
        var source = Source(photos, "keeper.jpg",
            rating: CatalogImportFact<int>.Mapped(5));
        var import = new CatalogImportService(catalog);
        var preview = await import.CreatePreviewAsync(
            source, Map(source, photos), CatalogImportPolicy.LightroomWins);
        await catalog.MutateAssessmentsAsync([
            new AssessmentMutation(id, AssessmentAxes.Rating, Rating: 3)
        ], AssessmentAxes.None);

        await Assert.ThrowsAsync<CatalogImportConflictException>(() =>
            import.ApplyAsync(preview));

        Assert.Equal(3, (await catalog.LoadImageStatesAsync([photo]))[photo].Rating);
        Assert.Null(await catalog.GetAppSettingAsync(preview.SettingsKey));
    }

    [Fact]
    public async Task InjectedFailureAfterInsertAndAssessment_RollsBackAllTablesAndSettings()
    {
        var photos = Directory.CreateDirectory(Path.Combine(_root, "photos")).FullName;
        var catalog = await CreateCatalogAsync();
        var source = Source(photos, "keeper.jpg",
            rating: CatalogImportFact<int>.Mapped(5));
        var import = new CatalogImportService(catalog);
        var preview = await import.CreatePreviewAsync(
            source, Map(source, photos), CatalogImportPolicy.LightroomWins);
        catalog.ImportWriteObserverAsync = (writes, _) => writes == 2
            ? Task.FromException(new IOException("injected"))
            : Task.CompletedTask;

        await Assert.ThrowsAsync<IOException>(() => import.ApplyAsync(preview));

        Assert.Empty(await catalog.LoadImageStatesAsync(preview.ImportedPaths));
        Assert.Null(await catalog.GetAppSettingAsync(preview.SettingsKey));
        Assert.Equal(0, await CountAssessmentsAsync());
    }

    [Fact]
    public async Task CancellationAfterFirstMutation_RollsBackNewPath()
    {
        var photos = Directory.CreateDirectory(Path.Combine(_root, "photos")).FullName;
        var catalog = await CreateCatalogAsync();
        var source = Source(photos, "keeper.jpg",
            rating: CatalogImportFact<int>.Mapped(5));
        var import = new CatalogImportService(catalog);
        var preview = await import.CreatePreviewAsync(
            source, Map(source, photos), CatalogImportPolicy.LightroomWins);
        using var cancellation = new CancellationTokenSource();
        catalog.ImportWriteObserverAsync = (_, _) =>
        {
            cancellation.Cancel();
            return Task.CompletedTask;
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            import.ApplyAsync(preview, cancellation.Token));

        Assert.Empty(await catalog.LoadImageStatesAsync(preview.ImportedPaths));
        Assert.Equal(0, await CountAssessmentsAsync());
    }

    [Fact]
    public async Task Report_DistinguishesThinUnmatchedAndMixedCatalogs()
    {
        var catalog = await CreateCatalogAsync();
        var import = new CatalogImportService(catalog);
        var thin = EmptySource();
        var thinReport = (await import.CreatePreviewAsync(
            thin, new Dictionary<string, string>(), CatalogImportPolicy.LightroomWins)).Report;
        Assert.True(thinReport.NothingToImport);
        Assert.Empty(thinReport.ActionableOutcomes);

        var unmatched = Source("Z:/Missing/", "keeper.jpg",
            rating: CatalogImportFact<int>.Mapped(4));
        var unmatchedReport = (await import.CreatePreviewAsync(
            unmatched, new Dictionary<string, string>(), CatalogImportPolicy.LightroomWins)).Report;
        Assert.True(unmatchedReport.NothingMatched);
        Assert.Contains(unmatchedReport.ActionableOutcomes,
            message => message.Contains("ratings, flags, or color labels"));
        Assert.Contains(unmatchedReport.InformationalOutcomes,
            message => message.Contains("unmapped Lightroom location"));

        var photos = Directory.CreateDirectory(Path.Combine(_root, "photos")).FullName;
        var mixed = new LightroomCatalogContents(
            Path.Combine(_root, "mixed.lrcat"), 1600000, 16, false,
            AssessmentAxes.All, [new CatalogSourceRoot("D:/", 3)],
            [
                Record("D:/", "one.jpg", CatalogImportFact<int>.Mapped(4)),
                Record("D:/", "clip.MOV", CatalogImportFact<int>.Mapped(4)),
                Record("D:/", "copy.jpg", CatalogImportFact<int>.Mapped(4), virtualCopy: true)
            ], []);
        var mixedReport = (await import.CreatePreviewAsync(
            mixed, Map(mixed, photos), CatalogImportPolicy.LightroomWins)).Report;
        Assert.Equal(1, mixedReport.MatchedPhotos);
        Assert.Equal(1, mixedReport.UnsupportedFilePhotos);
        Assert.Equal(1, mixedReport.VirtualCopyPhotos);
        Assert.Contains(mixedReport.InformationalOutcomes,
            message => message.Contains("unverified"));
    }

    [Fact]
    public async Task Preview_UnmappedRootIsAnInformationalSkipWhenAnotherRootMatches()
    {
        var catalog = await CreateCatalogAsync();
        var photos = Directory.CreateDirectory(Path.Combine(_root, "photos")).FullName;
        var source = new LightroomCatalogContents(
            Path.Combine(_root, "partial.lrcat"), 1303001, 13, true,
            AssessmentAxes.All,
            [
                new CatalogSourceRoot("D:/Matched/", 1),
                new CatalogSourceRoot("Z:/Unmapped/", 1)
            ],
            [
                Record("D:/Matched/", "one.jpg", CatalogImportFact<int>.Mapped(4)),
                Record("Z:/Unmapped/", "two.jpg", CatalogImportFact<int>.Mapped(5))
            ], []);
        var import = new CatalogImportService(catalog);

        var preview = await import.CreatePreviewAsync(
            source,
            new Dictionary<string, string> { ["D:/Matched/"] = photos },
            CatalogImportPolicy.LightroomWins);
        var result = await import.ApplyAsync(preview);

        Assert.False(result.Report.NothingMatched);
        Assert.Equal(1, result.Report.MatchedPhotos);
        Assert.Equal(1, result.Report.UnresolvedRootPhotos);
        Assert.Empty(result.Report.ActionableOutcomes);
        Assert.Contains(result.Report.InformationalOutcomes,
            message => message == "1 photo under an unmapped Lightroom location was not imported.");
    }

    [Fact]
    public async Task Preview_FiftyThousandVerdictRowsStaysWithinBudget()
    {
        var catalog = await CreateCatalogAsync();
        const string sourceRoot = "D:/Photos/";
        var records = Enumerable.Range(0, 50_000)
            .Select(index => Record(
                sourceRoot, $"year/photo-{index:D5}.jpg",
                CatalogImportFact<int>.Mapped(index % 5 + 1)))
            .ToArray();
        var source = new LightroomCatalogContents(
            Path.Combine(_root, "large.lrcat"), 1303001, 13, true,
            AssessmentAxes.All,
            [new CatalogSourceRoot(sourceRoot, records.Length)],
            records, []);
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        var preview = await new CatalogImportService(catalog).CreatePreviewAsync(
            source,
            new Dictionary<string, string> { [sourceRoot] = _root },
            CatalogImportPolicy.LightroomWins);

        stopwatch.Stop();
        Assert.Equal(50_000, preview.Report.MatchedPhotos);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(30),
            $"50k preview took {stopwatch.Elapsed}.");
    }

    private async Task<CatalogService> CreateCatalogAsync()
    {
        _catalog = new CatalogService(Path.Combine(_root, "catalog"));
        await _catalog.InitializeAsync();
        return _catalog;
    }

    private async Task<long> CountImagesAsync() =>
        await CountRowsAsync("images");

    private async Task<long> CountAssessmentsAsync() =>
        await CountRowsAsync("image_assessments");

    private async Task<long> CountRowsAsync(string table)
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(_root, "catalog", "catalog.db"),
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false
        };
        using var connection = new SqliteConnection(builder.ToString());
        await connection.OpenAsync();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {table};";
        return (long)(await command.ExecuteScalarAsync())!;
    }

    private LightroomCatalogContents Source(
        string root,
        string relative,
        CatalogImportFact<int>? rating = null,
        CatalogImportFact<ImageFlag>? flag = null,
        CatalogImportFact<ColorLabel>? label = null) =>
        new(Path.Combine(_root, "source.lrcat"), 1303001, 13, true,
            AssessmentAxes.All, [new CatalogSourceRoot(root, 1)],
            [Record(root, relative, rating, flag, label)], []);

    private LightroomCatalogContents EmptySource() =>
        new(Path.Combine(_root, "empty.lrcat"), 1303001, 13, true,
            AssessmentAxes.All, [], [], []);

    private static CatalogImportRecord Record(
        string root,
        string relative,
        CatalogImportFact<int>? rating = null,
        CatalogImportFact<ImageFlag>? flag = null,
        CatalogImportFact<ColorLabel>? label = null,
        bool virtualCopy = false) =>
        new(root, relative,
            rating ?? CatalogImportFact<int>.Empty,
            flag ?? CatalogImportFact<ImageFlag>.Empty,
            label ?? CatalogImportFact<ColorLabel>.Empty,
            virtualCopy);

    private static IReadOnlyDictionary<string, string> Map(
        LightroomCatalogContents source,
        string localRoot) =>
        new Dictionary<string, string> { [source.Roots[0].SourcePath] = localRoot };

    public void Dispose()
    {
        _catalog?.Dispose();
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
