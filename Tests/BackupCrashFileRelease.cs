using System.Diagnostics;
using Xunit;

namespace HappyPhoton.Tests;

internal static class BackupCrashFileRelease
{
    public static async Task WaitAsync(string root, int childPid, ITestOutputHelper output)
    {
        // The teardown budget bounds a hang; successful probes end the wait.
        var timer = Stopwatch.StartNew();

        while (FindLockedFile(root) is { } locked)
        {
            if (timer.Elapsed >= TimeSpan.FromSeconds(15))
            {
                output.WriteLine(BackupCrashLockDiagnostics.Capture(locked.Error, root, childPid));
                Assert.Fail($"Child {childPid} file release timed out: {locked.Path}: {locked.Error.Message}");
            }

            await Task.Delay(10);
        }

        output.WriteLine($"child_file_release child_pid={childPid} elapsed_ms={timer.Elapsed.TotalMilliseconds:F3}");
    }

    private static (string Path, IOException Error)? FindLockedFile(string root)
    {
        // Include presets and SQLite journals as well as the catalog and backup folder.
        foreach (var path in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
        {
            try
            {
                using var probe = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
            }
            catch (FileNotFoundException)
            {
                // Windows may finish a pending delete while the child's handles close.
            }
            catch (IOException ex) when (BackupCrashLockDiagnostics.IsSharingViolation(ex))
            {
                return (path, ex);
            }
        }

        return null;
    }
}
