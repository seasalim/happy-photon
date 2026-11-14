using System.Reflection;
using HappyPhoton.Models;
using HappyPhoton.Services;
using Microsoft.Data.Sqlite;
using SQLitePCL;
using Xunit;

namespace HappyPhoton.Tests;

// SLIM-WP7 G1: counts the SQL statements a settings load sends to app_settings, using
// SQLite's own statement trace so production code carries no counter.
public sealed class AppSettingsLoadQueryCountTests(ITestOutputHelper output) : IDisposable
{
    private readonly string _catalogPath =
        Path.Combine(Path.GetTempPath(), $"happy-photon-settings-count-{Guid.NewGuid():N}");

    [Fact]
    public async Task LoadAsync_ReadsAppSettingsAtMostTwice()
    {
        using var catalog = new CatalogService(_catalogPath);
        await catalog.InitializeAsync();
        var service = new AppSettingsService(catalog);
        await service.SaveAsync(SeededSettings());
        await catalog.SetAppSettingAsync("PasteGroups", "{\"Adjustments\":false}");

        var statements = new List<string>();
        var handle = ConnectionOf(catalog).Handle!;
        raw.sqlite3_trace(handle, (_, sql) => statements.Add(sql.utf8_to_string()), null);
        AppSettings loaded;

        try
        {
            loaded = await service.LoadAsync();
        }
        finally
        {
            raw.sqlite3_trace(handle, (delegate_trace?)null, null);
        }

        var reads = statements.Count(sql => sql.Contains("app_settings", StringComparison.OrdinalIgnoreCase));
        output.WriteLine($"G1 app_settings reads during LoadAsync: {reads}");

        Assert.Equal(ImageFileTypeFilter.Raw, loaded.FileTypeFilter);
        Assert.Equal(OutputSharpeningMode.Print, loaded.OutputSharpening);
        Assert.Equal(@"C:\Photos", loaded.RootFolderPath);
        Assert.True(reads <= 2, $"LoadAsync issued {reads} app_settings statements; G1 allows at most 2.");
        Assert.Equal(1, reads);
    }

    public void Dispose()
    {
        if (Directory.Exists(_catalogPath)) Directory.Delete(_catalogPath, recursive: true);
    }

    private static AppSettings SeededSettings() => new()
    {
        RootFolderPath = @"C:\Photos",
        SelectedFolderPath = @"C:\Photos\Shoot",
        FirstRunExperienceVersion = 1,
        FileTypeFilter = ImageFileTypeFilter.Raw,
        BrowseThumbnailSize = BrowseThumbnailSize.Large,
        ShowCapturePairs = true,
        AppTheme = AppTheme.MidGray,
        StripLocationData = true,
        OutputSharpening = OutputSharpeningMode.Print,
        WatermarkEnabled = true
    };

    private static SqliteConnection ConnectionOf(CatalogService catalog) =>
        (SqliteConnection)typeof(CatalogService)
            .GetField("_connection", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(catalog)!;
}
