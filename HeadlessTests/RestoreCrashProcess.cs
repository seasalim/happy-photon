using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Xunit;

namespace HappyPhoton.Tests;

internal static class RestoreCrashProcess
{
    private static readonly string ReportRoot = Path.Combine(GoldenTestPaths.RepositoryRoot,
        "artifacts", "restore-crash", DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N"));

    internal static async Task KillAsync(string root, string backup, string point, bool errorScreen,
        string path, string? predecessor, string? equivalent, ITestOutputHelper output)
    {
        var full = RestoreCrashEquivalence.FullMatrix;
        var pauses = full ? new[] { predecessor, equivalent }.OfType<string>().Distinct().ToArray() : [];
        var start = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        start.ArgumentList.Add(typeof(RestoreCrashProcess).Assembly.Location);
        start.ArgumentList.Add("--restore-crash-child");
        var temporary = Directory.CreateDirectory(Path.Combine(root, "temporary")).FullName;

        foreach (var name in new[] { "TMP", "TEMP", "TMPDIR" })
        {
            start.Environment[name] = temporary;
        }

        // Disable unused runtime diagnostics so Unix sockets and debugger FIFOs cannot enter the fingerprint root.
        start.Environment["DOTNET_EnableDiagnostics"] = "0";
        start.Environment["HP_RESTORE_CHILD"] = root;
        start.Environment["HP_RESTORE_BACKUP"] = backup;
        start.Environment["HP_RESTORE_POINT"] = point;
        start.Environment["HP_RESTORE_ERROR_SCREEN"] = errorScreen.ToString();
        start.Environment["HP_RESTORE_PAUSES"] = JsonSerializer.Serialize(pauses);
        using var process = Process.Start(start)!;
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TestWaits.Condition);
        var signalled = false;
        var lines = new List<string>();
        var snapshots = new Dictionary<string, RestoreCrashFingerprint>(StringComparer.Ordinal);
        RestoreCrashFingerprint? paused = null;

        try
        {
            while (await process.StandardOutput.ReadLineAsync(timeout.Token) is { } line)
            {
                lines.Add(line);

                if (line.StartsWith("RESTORE_PAUSE:", StringComparison.Ordinal))
                {
                    var boundary = line["RESTORE_PAUSE:".Length..];
                    Assert.Contains(boundary, pauses);
                    snapshots.Add(boundary, RestoreCrashFingerprint.Read(root));
                    await process.StandardInput.WriteLineAsync("CONTINUE");
                    await process.StandardInput.FlushAsync();
                    continue;
                }

                if (line != "RESTORE_KILL_READY") continue;

                signalled = true;
                if (full) paused = RestoreCrashFingerprint.Read(root);
                break;
            }
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            lines.Add("Timed out waiting for the child signal.");
        }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
        }

        Assert.True(signalled, $"Missing kill point {point}: {string.Join("\n", lines)}\n{await stderr}");
        Assert.NotEqual(0, process.ExitCode);
        if (!full) return;

        var killed = RestoreCrashFingerprint.Read(root);
        var previous = predecessor == null ? null : snapshots.GetValueOrDefault(predecessor);
        var kept = equivalent == null ? null : snapshots.GetValueOrDefault(equivalent);
        var report = new
        {
            Path = path, Boundary = point, Predecessor = predecessor, Equivalent = equivalent,
            Signalled = signalled, process.ExitCode,
            KillInvariant = killed.SameAs(paused!),
            EqualsPredecessor = previous != null && killed.SameAs(previous),
            EqualsEquivalent = kept != null && killed.SameAs(kept),
            Paused = paused, PostKill = killed, Pauses = snapshots,
            PostKillDigest = killed.Digest, PredecessorDigest = previous?.Digest, EquivalentDigest = kept?.Digest
        };
        var folder = Directory.CreateDirectory(Path.Combine(ReportRoot, path)).FullName;
        var reportName = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(point)));
        var reportPath = Path.Combine(folder, reportName + ".json");
        File.WriteAllText(reportPath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        output.WriteLine($"Restore fingerprint: {reportPath}");
        Assert.True(report.KillInvariant, $"Paused/post-kill fingerprint differs on {path}: {point}");

        foreach (var boundary in pauses)
        {
            Assert.True(snapshots.ContainsKey(boundary), $"Missing fingerprint pause on {path}: {boundary}");
        }

        if (equivalent != null)
            Assert.True(report.EqualsEquivalent, $"Restore fingerprints differ on {path}: {point} != {equivalent}");
    }
}
