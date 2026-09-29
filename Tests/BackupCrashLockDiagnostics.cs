using System.Diagnostics;
using System.Text.RegularExpressions;
using Xunit;

namespace HappyPhoton.Tests;

internal static class BackupCrashLockDiagnostics
{
    [ThreadStatic]
    private static bool _probing;

    public static bool IsSharingViolation(Exception exception) =>
        exception.HResult is unchecked((int)0x80070020) or unchecked((int)0x80070021);

    public static string Capture(Exception exception, string root, int childPid)
    {
        if (_probing || !IsSharingViolation(exception)) return "lock_capture=not-applicable";

        _probing = true;

        try
        {
            var match = Regex.Match(exception.Message, """['"](?<path>[^'"]+)['"]""");
            if (!match.Success) return "lock_capture=no-named-file ownership=unproven";

            var namedPath = match.Groups["path"].Value;
            var path = Path.GetFullPath(namedPath, root);

            // Recursive Directory.Delete reports only a basename, including for nested backups.
            if (!Path.IsPathRooted(namedPath) && !File.Exists(path))
            {
                var matches = Directory.GetFiles(root, Path.GetFileName(namedPath), SearchOption.AllDirectories);
                if (matches.Length != 1) return $"lock_capture=ambiguous-relative-file matches={matches.Length} ownership=unproven";

                path = matches[0];
            }
            var prefix = Path.GetFullPath(root) + Path.DirectorySeparatorChar;
            if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return "lock_capture=outside-test-root";

            var timer = Stopwatch.StartNew();
            var utc = DateTimeOffset.UtcNow;
            // Start owner discovery at the throw. Probe concurrently so RM latency cannot hide short locks.
            var owners = Task.Factory.StartNew(() =>
            {
                var started = timer.Elapsed.TotalMilliseconds;
                var result = BackupCrashRestartManager.Owners(path, childPid);

                return $"owner_query_start_ms={started:F3} owner_query_end_ms={timer.Elapsed.TotalMilliseconds:F3}\n{result}";
            }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
            var lines = new List<string>
            {
                $"LOCK_CAPTURE utc={utc:O} file={path} hresult=0x{exception.HResult:X8} " +
                    $"test_pid={Environment.ProcessId} child_pid={childPid}"
            };
            var opened = false;

            foreach (var target in new[] { 0, 25, 50, 100, 250, 500, 1000, 2000 })
            {
                // Diagnostic sampling schedule, not a wait that makes an assertion pass.
                var remaining = target - timer.Elapsed.TotalMilliseconds;
                if (remaining > 0) Thread.Sleep(TimeSpan.FromMilliseconds(remaining));

                try
                {
                    using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
                    lines.Add($"exclusive_probe=opened target_ms={target} elapsed_ms={timer.Elapsed.TotalMilliseconds:F3}");
                    opened = true;
                    break;
                }
                catch (Exception ex)
                {
                    lines.Add($"exclusive_probe=failed target_ms={target} elapsed_ms={timer.Elapsed.TotalMilliseconds:F3} " +
                        $"hresult=0x{ex.HResult:X8} error={ex.GetType().Name}");
                    if (!IsSharingViolation(ex)) break;
                }
            }

            lines.Add($"lock_duration={(opened ? "first-success-above" : "no-success-observed")} " +
                $"observation_ms={timer.Elapsed.TotalMilliseconds:F3}");
            // Bound native discovery separately; the harness also bounds the entire test process.
            lines.Add(owners.Wait(TestWaits.Condition) ? owners.GetAwaiter().GetResult() :
                "restart_manager=timed-out ownership=unproven");

            return string.Join("\n", lines);
        }
        catch (Exception ex)
        {
            return $"lock_capture=failed error={ex.GetType().Name}: {ex.Message} ownership=unproven";
        }
        finally
        {
            _probing = false;
        }
    }

    public static void Cleanup(Exception exception, string root, int childPid, ITestOutputHelper output)
    {
        output.WriteLine($"BACKUP_DIAGNOSTIC trigger=cleanup root={root} message={exception.Message}");
        var poolState = BackupCrashSqliteDiagnostics.Snapshot(Path.Combine(root, "catalog.db"));
        output.WriteLine(Capture(exception, root, childPid));
        output.WriteLine(poolState);
    }
}
