using System.Diagnostics;
using System.Reflection;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using Microsoft.Data.Sqlite;
using SQLitePCL;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class FolderLoadPerformanceTests
{
    // HAPPY_PHOTON_FOLDER_LOAD_FILES scales the folder (e.g. 5000) to check that
    // statement counts stay batched rather than growing per file.
    private static readonly int FileCount = int.TryParse(
        Environment.GetEnvironmentVariable("HAPPY_PHOTON_FOLDER_LOAD_FILES"),
        out var count) && count > 0 ? count : 200;
    private const int SampleCount = 9;
    private readonly ITestOutputHelper _output;

    public FolderLoadPerformanceTests(ITestOutputHelper output) =>
        _output = output;

    [Fact]
    public async Task SteadyStateFolderLoad_ReportsWallTimeAndSqlStatements()
    {
        Assert.SkipWhen(
            Environment.GetEnvironmentVariable("HAPPY_PHOTON_PERF") != "1",
            "Set HAPPY_PHOTON_PERF=1 to run folder-load performance diagnostics.");

        using var fixture = new CatalogVmFixture("folder-load-perf");
        var photos = fixture.Path("photos");
        Directory.CreateDirectory(photos);
        var paths = CreatePhotos(photos);
        using var catalog = await fixture.CreateCatalogAsync("catalog");
        await catalog.LoadOrCreateImageStatesAsync(paths);

        using var statements = new SqlStatementCounter(catalog);
        await MeasureAsync(fixture, catalog, photos, statements);

        var samples = new FolderLoadSample[SampleCount];
        for (var index = 0; index < samples.Length; index++)
        {
            samples[index] = await MeasureAsync(
                fixture, catalog, photos, statements);
        }

        var elapsed = samples
            .Select(sample => sample.ElapsedMilliseconds)
            .Order()
            .ToArray();
        var counts = samples
            .Select(sample => sample.SqlStatementCount)
            .ToArray();
        _output.WriteLine(
            $"folder_load_{FileCount}_files_ms_samples=[{string.Join(", ", samples.Select(sample => sample.ElapsedMilliseconds.ToString("F3")))}]");
        _output.WriteLine(
            $"folder_load_{FileCount}_files_ms_median={elapsed[elapsed.Length / 2]:F3}; " +
            $"warm_up=1; measured_runs={SampleCount}; configuration=" +
#if DEBUG
            "Debug"
#else
            "Release"
#endif
        );
        _output.WriteLine(
            $"folder_load_{FileCount}_files_sql_statement_counts=[{string.Join(", ", counts)}]; " +
            $"median={counts.Order().ElementAt(counts.Length / 2)}");

        Assert.All(samples, sample => Assert.Equal(FileCount, sample.ImageCount));
        Assert.All(counts, count => Assert.Equal(counts[0], count));
    }

    [Fact]
    public async Task VersionedFolderLoad_ReportsWallTimeStatementsAndMetadataReads()
    {
        Assert.SkipWhen(
            Environment.GetEnvironmentVariable("HAPPY_PHOTON_PERF") != "1",
            "Set HAPPY_PHOTON_PERF=1 to run folder-load performance diagnostics.");

        using var fixture = new CatalogVmFixture("folder-load-perf-versioned");
        var photos = fixture.Path("photos");
        Directory.CreateDirectory(photos);
        var paths = CreatePhotos(photos);
        using var catalog = await fixture.CreateCatalogAsync("catalog");
        var states = await catalog.LoadOrCreateImageStatesAsync(paths);
        foreach (var path in paths)
            await catalog.CreateVersionAsync(states[path].Single().CatalogId);

        using var statements = new SqlStatementCounter(catalog);
        await MeasureAsync(fixture, catalog, photos, statements);

        var samples = new FolderLoadSample[SampleCount];
        for (var index = 0; index < samples.Length; index++)
            samples[index] = await MeasureAsync(fixture, catalog, photos, statements);

        var elapsed = samples.Select(s => s.ElapsedMilliseconds).Order().ToArray();
        var counts = samples.Select(s => s.SqlStatementCount).ToArray();
        _output.WriteLine(
            $"versioned_folder_load_{FileCount * 2}_tiles_ms_median={elapsed[elapsed.Length / 2]:F3}; " +
            $"warm_up=1; measured_runs={SampleCount}");
        _output.WriteLine(
            $"versioned_folder_load_sql_statement_counts=[{string.Join(", ", counts)}]");

        Assert.All(samples, sample => Assert.Equal(FileCount * 2, sample.ImageCount));
        Assert.All(counts, count => Assert.Equal(counts[0], count));

        var metadataReads = 0;
        var viewModel = fixture.CreateViewModel(
            catalog,
            loadMetadataAsync: image =>
            {
                Interlocked.Increment(ref metadataReads);
                image.ApplyMetadata(new HappyPhoton.Models.ImageMetadata
                {
                    DateTaken = DateTime.UtcNow
                });
                return Task.CompletedTask;
            },
            availabilityService: new TestSourceAvailabilityService(
                SourceAvailability.AvailableLocally),
            postSelection: _ => { });
        await viewModel.LoadFolderAsync(photos);
        viewModel.ShowBurstGroups = true;
        await viewModel.WaitForBurstAnalysisAsync();
        _output.WriteLine(
            $"versioned_folder_metadata_reads={metadataReads}; tiles=" +
            $"{viewModel.Browse.AllImages.Count}; distinct_files={FileCount}");
        Assert.Equal(FileCount * 2, viewModel.Browse.AllImages.Count);
        Assert.Equal(FileCount, metadataReads);
        await viewModel.DisposeAsync();
    }

    [Fact]
    public async Task FirstVisitFolderLoad_ReportsWallTimeAndSqlStatements()
    {
        Assert.SkipWhen(
            Environment.GetEnvironmentVariable("HAPPY_PHOTON_PERF") != "1",
            "Set HAPPY_PHOTON_PERF=1 to run folder-load performance diagnostics.");

        using var fixture = new CatalogVmFixture("folder-load-perf-first-visit");
        var photos = fixture.Path("photos");
        Directory.CreateDirectory(photos);
        CreatePhotos(photos);

        // Each sample opens a fresh catalog, so every row is created by this load.
        var samples = new FolderLoadSample[SampleCount + 1];
        for (var index = 0; index < samples.Length; index++)
        {
            using var catalog = await fixture.CreateCatalogAsync($"catalog-{index}");
            using var statements = new SqlStatementCounter(catalog);
            samples[index] = await MeasureAsync(fixture, catalog, photos, statements);
        }

        var measured = samples.Skip(1).ToArray();
        var elapsed = measured.Select(s => s.ElapsedMilliseconds).Order().ToArray();
        var counts = measured.Select(s => s.SqlStatementCount).ToArray();
        _output.WriteLine(
            $"first_visit_folder_load_{FileCount}_files_ms_median={elapsed[elapsed.Length / 2]:F3}; " +
            $"max={elapsed[^1]:F3}; warm_up=1; measured_runs={SampleCount}");
        _output.WriteLine(
            $"first_visit_folder_load_sql_statement_counts=[{string.Join(", ", counts)}]");

        Assert.All(measured, sample => Assert.Equal(FileCount, sample.ImageCount));
    }

    private static async Task<FolderLoadSample> MeasureAsync(
        CatalogVmFixture fixture,
        CatalogService catalog,
        string photos,
        SqlStatementCounter statements)
    {
        var viewModel = fixture.CreateViewModel(
            catalog,
            loadMetadataAsync: _ => Task.CompletedTask,
            availabilityService: new TestSourceAvailabilityService(
                SourceAvailability.AvailableLocally),
            postSelection: _ => { });
        viewModel.IsDevelopMode = true;
        statements.Reset();
        var stopwatch = Stopwatch.StartNew();
        await viewModel.LoadFolderAsync(photos);
        stopwatch.Stop();
        var sample = new FolderLoadSample(
            stopwatch.Elapsed.TotalMilliseconds,
            statements.Count,
            viewModel.Browse.AllImages.Count);
        await viewModel.DisposeAsync();
        return sample;
    }

    private static string[] CreatePhotos(string folder)
    {
        var first = Path.Combine(folder, "image-000.jpg");
        TestImages.WriteJpeg(first);
        var paths = new string[FileCount];
        paths[0] = first;
        for (var index = 1; index < paths.Length; index++)
        {
            var path = Path.Combine(folder, $"image-{index:D3}.jpg");
            File.Copy(first, path);
            paths[index] = path;
        }
        return paths;
    }

    private readonly record struct FolderLoadSample(
        double ElapsedMilliseconds,
        int SqlStatementCount,
        int ImageCount);

    private sealed class SqlStatementCounter : IDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly strdelegate_trace _callback;
        private int _count;

        public SqlStatementCounter(CatalogService catalog)
        {
            _connection = (SqliteConnection)(typeof(CatalogService)
                .GetField("_connection", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(catalog) ?? throw new InvalidOperationException(
                    "Catalog connection is unavailable."));
            _callback = (_, _) => Interlocked.Increment(ref _count);
            raw.sqlite3_trace(_connection.Handle, _callback, null);
        }

        public int Count => Volatile.Read(ref _count);

        public void Reset() => Interlocked.Exchange(ref _count, 0);

        public void Dispose() =>
            raw.sqlite3_trace(
                _connection.Handle,
                (strdelegate_trace?)null!,
                null);
    }
}
