using System.Text.Json;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

internal static class BackupKillPointChild
{
    internal static async Task RunAsync(bool move)
    {
        var root = Environment.GetEnvironmentVariable(move ? "HP_MOVE_BACKUP_CHILD" : "HAPPY_PHOTON_BACKUP_CHILD");
        Assert.NotNull(root);
        var temporary = Path.Combine(root, "temporary");
        Assert.True(CatalogMoveFileOperations.SamePath(
            AppDataRootOwnership.ResolveRealPath(temporary),
            AppDataRootOwnership.ResolveRealPath(Path.GetTempPath())),
            "Kill-point child TEMP must be <root>/temporary.");
        var target = Environment.GetEnvironmentVariable(move ? "HP_MOVE_BACKUP_POINT" : "HAPPY_PHOTON_BACKUP_KILL");
        Assert.NotNull(target);
        var pauses = JsonSerializer.Deserialize<string[]>(
            Environment.GetEnvironmentVariable("HP_KILL_PAUSES") ?? "[]")!;
        using var signal = new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true };
        var observed = new HashSet<string>(StringComparer.Ordinal);

        if (move)
        {
            await MoveBackupCrashDriver.RunAsync(root, point => Signal(point, ""));
        }
        else
        {
            await BackupCrashDriver.RunAsync(root, Signal);
        }

        Assert.Fail("Kill point was not reached: " + target);

        void Signal(string point, string expectedHash)
        {
            // Discovery and every child address the first occurrence of each callback value.
            if (!observed.Add(point)) return;
            if (point != target && !pauses.Contains(point, StringComparer.Ordinal)) return;

            if (!move)
                Assert.Equal(expectedHash, BackupTestSupport.Hash(Path.Combine(root, "catalog.db")));

            if (point == target)
            {
                signal.WriteLine("KILL_READY:" + expectedHash);
                using var wait = new ManualResetEventSlim();
                Assert.True(wait.Wait(TestWaits.Condition), "Parent failed to terminate child.");
            }
            else
            {
                signal.WriteLine("KILL_PAUSE:" + point);
                var reply = Console.In.ReadLineAsync().WaitAsync(TestWaits.Condition).GetAwaiter().GetResult();
                Assert.Equal("CONTINUE", reply);
            }
        }
    }
}
