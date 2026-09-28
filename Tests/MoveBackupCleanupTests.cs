using System.Diagnostics;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class MoveBackupCleanupTests
{
    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    public async Task InterruptedNestedCleanup_RecoversAndRemovesJournal(int removedDirectories, bool rollback)
    {
        using var directory = new TemporaryDirectory();
        var f = await MoveBackupTestSupport.CreateAsync(directory.Path);
        var folder = CatalogMoveBackupFiles.PathFor(f.Locations.CatalogRoot);
        var nested = Path.Combine(folder, "outer", "inner");
        Directory.CreateDirectory(nested);
        File.Move(Path.Combine(folder, "damaged.zip"), Path.Combine(nested, "damaged.zip"));
        var original = RestoreTestSupport.Generation(f.Locations.CatalogRoot);
        var expected = MoveBackupTestSupport.Hashes(f.Locations.CatalogRoot);
        var store = new CatalogLocationMigrator(f.Service);
        await store.StageMoveAsync(f.Locations, CatalogLocationMoveKind.Catalog, f.Destination);
        await KillAfterCleanup(directory.Path, removedDirectories, rollback);
        Assert.True(File.Exists(store.JournalPath));
        var recovery = new CatalogLocationMigrator(f.Service);

        if (rollback)
        {
            // A pre-flip journal retries verification, then finishes destination cleanup.
            await Assert.ThrowsAnyAsync<IOException>(recovery.ExecutePendingAsync);
            Assert.False(File.Exists(store.JournalPath));
            Assert.Equal(original, RestoreTestSupport.Generation(f.Locations.CatalogRoot));
            Assert.Equal(expected, MoveBackupTestSupport.Hashes(f.Locations.CatalogRoot));
            Assert.Empty(MoveBackupTestSupport.Hashes(f.Destination));
            await recovery.StageMoveAsync(f.Locations, CatalogLocationMoveKind.Catalog, f.Destination);
        }

        await recovery.ExecutePendingAsync();
        Assert.False(File.Exists(store.JournalPath));
        Assert.Equal(f.Destination, (await f.Service.ResolveAsync())!.CatalogRoot);
        Assert.Equal(expected, MoveBackupTestSupport.Hashes(f.Destination));
        Assert.Empty(MoveBackupTestSupport.Hashes(f.Locations.CatalogRoot));
        Assert.False(Directory.Exists(folder));
        await recovery.ExecutePendingAsync();
    }

    private static async Task KillAfterCleanup(string root, int removedDirectories, bool rollback)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        };

        foreach (var argument in new[] { typeof(MoveBackupCleanupTests).Assembly.Location, "-method",
                     "HappyPhoton.Tests.MoveBackupCleanupTests.Child", "-showLiveOutput", "-noColor" })
            start.ArgumentList.Add(argument);

        start.Environment["HP_MOVE_CLEANUP_CHILD"] = root;
        start.Environment["HP_MOVE_CLEANUP_DIRECTORIES"] = removedDirectories.ToString();
        start.Environment["HP_MOVE_CLEANUP_ROLLBACK"] = rollback.ToString();
        using var process = Process.Start(start)!;
        var error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TestWaits.Condition);
        var signalled = false;

        try
        {
            while (await process.StandardOutput.ReadLineAsync(timeout.Token) is { } line)
            {
                if (line != "CLEANUP_READY") continue;

                signalled = true;
                break;
            }
        }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);

            await process.WaitForExitAsync();
        }

        Assert.True(signalled, await error);
        Assert.NotEqual(0, process.ExitCode);
    }

    [Fact]
    public async Task Child()
    {
        var root = Environment.GetEnvironmentVariable("HP_MOVE_CLEANUP_CHILD");
        Assert.SkipWhen(root == null, "Only the termination parent invokes this test.");
        var removedDirectories = int.Parse(Environment.GetEnvironmentVariable("HP_MOVE_CLEANUP_DIRECTORIES")!);
        var rollback = bool.Parse(Environment.GetEnvironmentVariable("HP_MOVE_CLEANUP_ROLLBACK")!);
        var service = RestoreTestSupport.Service(root!);
        var journal = await new CatalogLocationMigrator(service).ReadJournalAsync();
        var store = new CatalogLocationMigrator(service, phase =>
        {
            if (phase != (rollback ? CatalogLocationMovePhase.CatalogCopied : CatalogLocationMovePhase.PointerFlipped))
                return;

            // Reproduce the on-disk checkpoint after cleanup mutations, before journal deletion.
            var target = rollback ? journal.DestinationRoot! : journal.CatalogRoot;
            var path = CatalogMoveBackupFiles.PathFor(target, Path.Combine("outer", "inner", "damaged.zip"));
            File.Delete(path);
            var parent = Path.GetDirectoryName(path)!;

            for (var i = 0; i < removedDirectories; i++)
            {
                Directory.Delete(parent);
                parent = Path.GetDirectoryName(parent)!;
            }

            using var signal = new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true };
            signal.WriteLine("CLEANUP_READY");
            using var wait = new ManualResetEventSlim();
            Assert.True(wait.Wait(TestWaits.Condition), "Parent failed to terminate child.");
        });
        await store.ExecutePendingAsync();
        Assert.Fail("Cleanup checkpoint was not reached.");
    }
}
