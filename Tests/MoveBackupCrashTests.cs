using System.Diagnostics;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class MoveBackupCrashTests(BackupCatalogFixtures fixtures, ITestOutputHelper output)
{
    public static IEnumerable<object[]> Points()
    {
        foreach (var phase in new[] { "Prepared", "CatalogCopied", "Verified", "PointerFlipped" })
            yield return ["phase:" + phase];

        foreach (var name in Enumerable.Range(0, 5).Select(i => $"archive-{i}.zip")
                     .Concat(Enumerable.Range(0, 4).Select(i => $"archive-{i}.manifest.json"))
                     .Append("damaged.zip"))
        {
            yield return ["before:copy:" + name];
            yield return ["after:copy:" + name];
        }
    }

    [Theory]
    [MemberData(nameof(Points))]
    public async Task KilledMover_PreservesSourceBeforeFlip_RecoversVerifiedDestination(string point)
    {
        using var directory = new TemporaryDirectory();
        var f = await MoveBackupTestSupport.CreateAsync(directory.Path, await fixtures.Archive);
        var expected = MoveBackupTestSupport.Hashes(f.Locations.CatalogRoot);
        var original = RestoreTestSupport.Generation(f.Locations.CatalogRoot);
        var store = new CatalogLocationMigrator(f.Service);
        await store.StageMoveAsync(f.Locations, CatalogLocationMoveKind.Catalog, f.Destination);
        var start = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        };

        foreach (var argument in new[] { typeof(MoveBackupCrashTests).Assembly.Location, "-method",
                     "HappyPhoton.Tests.MoveBackupCrashTests.Child", "-showLiveOutput", "-noColor" })
            start.ArgumentList.Add(argument);

        start.Environment["HP_MOVE_BACKUP_CHILD"] = directory.Path;
        start.Environment["HP_MOVE_BACKUP_POINT"] = point;
        using var process = Process.Start(start)!;
        var error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TestWaits.Condition);
        var signalled = false;

        try
        {
            while (await process.StandardOutput.ReadLineAsync(timeout.Token) is { } line)
            {
                if (line != "MOVE_READY") continue;

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
        Assert.Equal(expected, MoveBackupTestSupport.Hashes(f.Locations.CatalogRoot));
        Assert.Equal(original, RestoreTestSupport.Generation(f.Locations.CatalogRoot));

        if (point == "phase:PointerFlipped")
        {
            Assert.Equal(f.Destination, (await f.Service.ResolveAsync())!.CatalogRoot);
            Assert.Equal(expected, MoveBackupTestSupport.Hashes(f.Destination));
        }
        else
        {
            Assert.Equal(f.Locations.CatalogRoot, (await f.Service.ResolveAsync())!.CatalogRoot);
        }

        await new CatalogLocationMigrator(f.Service).ExecutePendingAsync();
        Assert.Equal(f.Destination, (await f.Service.ResolveAsync())!.CatalogRoot);
        Assert.Equal(expected, MoveBackupTestSupport.Hashes(f.Destination));
        Assert.Empty(MoveBackupTestSupport.Hashes(f.Locations.CatalogRoot));
        Assert.False(File.Exists(store.JournalPath));
        output.WriteLine($"G3 {point}: terminated child; preserved source and verified destination; recovery passed");
    }

    [Fact]
    public async Task Child()
    {
        var root = Environment.GetEnvironmentVariable("HP_MOVE_BACKUP_CHILD");
        Assert.SkipWhen(root == null, "Only the termination parent invokes this test.");
        var point = Environment.GetEnvironmentVariable("HP_MOVE_BACKUP_POINT");
        var store = new CatalogLocationMigrator(RestoreTestSupport.Service(root!), phase => Signal("phase:" + phase))
        {
            BackupMoveStep = Signal
        };
        await store.ExecutePendingAsync();
        Assert.Fail("Kill point was not reached: " + point);

        void Signal(string current)
        {
            if (current != point) return;

            using var signal = new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true };
            signal.WriteLine("MOVE_READY");
            using var wait = new ManualResetEventSlim();
            Assert.True(wait.Wait(TestWaits.Condition), "Parent failed to terminate child.");
        }
    }
}
