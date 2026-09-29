using System.Text.Json;
using HappyPhoton.Services;
using Microsoft.Data.Sqlite;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class BackupSweepTests
{
    [Theory]
    [InlineData("db")]
    [InlineData("zip")]
    [InlineData("json")]
    public async Task LockedStalePartialIsSkippedThenSweptAfterRelease(string extension)
    {
        Assert.SkipWhen(!OperatingSystem.IsWindows(), "Windows file-sharing behavior.");

        using var directory = new TemporaryDirectory();
        using var catalog = new CatalogService(directory.Path);
        await catalog.InitializeAsync();
        var service = new CatalogBackupService(catalog);
        Directory.CreateDirectory(service.Folder);
        var stale = Path.Combine(service.Folder, $"hp-backup-{Guid.NewGuid():N}.partial.{extension}");
        File.WriteAllText(stale, "stale");
        var removable = Path.Combine(service.Folder, $"hp-backup-{Guid.NewGuid():N}.partial.db");
        File.WriteAllText(removable, "stale");

        using (var held = new FileStream(stale, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            await service.BackupAsync();

            Assert.Equal("ok", (await Outcome(catalog)).Status);
            Assert.False(service.IsDue());
            Assert.Equal("stale", File.ReadAllText(stale));
            Assert.Equal(stale, Assert.Single(Directory.GetFiles(service.Folder, "*.partial.*")));
            Assert.False(File.Exists(removable));
            using var restore = new TemporaryDirectory();
            await BackupTestSupport.AssertArchiveAsync(Assert.Single(service.List()).Path + ".zip", restore.Path);
        }

        await service.BackupAsync();

        Assert.Equal("ok", (await Outcome(catalog)).Status);
        Assert.Equal(2, service.List().Count);
        Assert.Empty(Directory.GetFiles(service.Folder, "*.partial.*"));
    }

    [Theory]
    [InlineData("sharing", "ok")]
    [InlineData("lock", "ok")]
    [InlineData("io", "failed")]
    [InlineData("access", "failed")]
    [InlineData("sqlite", "failed")]
    [InlineData("corrupt", "catalog-damaged")]
    public async Task SweepOnlySkipsSharingAndLockViolations(string fault, string status)
    {
        using var directory = new TemporaryDirectory();
        using var catalog = new CatalogService(directory.Path);
        await catalog.InitializeAsync();
        var service = new CatalogBackupService(catalog);
        Directory.CreateDirectory(service.Folder);
        var stale = Path.Combine(service.Folder, $"hp-backup-{Guid.NewGuid():N}.partial.db");
        File.WriteAllText(stale, "stale");
        Exception error = fault switch
        {
            "sharing" => new IOException("sharing", unchecked((int)0x80070020)),
            "lock" => new IOException("lock", unchecked((int)0x80070021)),
            "io" => new IOException("other I/O", unchecked((int)0x8007001D)),
            "access" => new UnauthorizedAccessException("access denied"),
            "sqlite" => new SqliteException("busy", 5),
            _ => new SqliteException("corrupt", 11)
        };
        service.Step = step =>
        {
            if (step == "before:sweep") throw error;
        };

        await service.BackupAsync();

        var outcome = await Outcome(catalog);
        Assert.Equal(status, outcome.Status);
        Assert.Equal("stale", File.ReadAllText(stale));
        Assert.Equal(stale, Assert.Single(Directory.GetFiles(service.Folder, "*.partial.*")));

        if (status == "ok")
        {
            Assert.Null(outcome.Reason);
            Assert.False(service.IsDue());
            using var restore = new TemporaryDirectory();
            await BackupTestSupport.AssertArchiveAsync(Assert.Single(service.List()).Path + ".zip", restore.Path);
        }
        else
        {
            Assert.Equal(error.Message, outcome.Reason);
            Assert.True(service.IsDue());
            Assert.Empty(service.List());
        }
    }

    [Fact]
    public async Task SharingViolationOutsideSweepStillFailsWithoutPublishing()
    {
        using var directory = new TemporaryDirectory();
        using var catalog = new CatalogService(directory.Path);
        await catalog.InitializeAsync();
        var service = new CatalogBackupService(catalog);
        service.Step = step =>
        {
            if (step == "before:publish-zip") throw new IOException("sharing", unchecked((int)0x80070020));
        };

        await service.BackupAsync();

        var outcome = await Outcome(catalog);
        Assert.Equal("failed", outcome.Status);
        Assert.Equal("sharing", outcome.Reason);
        Assert.Empty(service.List());
        Assert.Empty(Directory.GetFiles(service.Folder, "*.manifest.json"));
        Assert.All(Directory.GetFiles(service.Folder), path => Assert.Contains(".partial.", path));
    }

    private static async Task<BackupOutcome> Outcome(CatalogService catalog) =>
        JsonSerializer.Deserialize<BackupOutcome>((await catalog.GetAppSettingAsync(CatalogBackupService.OutcomeKey))!)!;
}
