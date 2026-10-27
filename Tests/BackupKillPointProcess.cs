using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Xunit;

namespace HappyPhoton.Tests;

internal sealed class BackupKillPointProcess : IDisposable
{
    private static readonly string ReportRoot = Path.Combine(GoldenTestPaths.RepositoryRoot,
        "artifacts", "kill-points", DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N"));

    private readonly BackupKillPointMatrix _matrix;

    private readonly string _root;

    private readonly string _point;

    private readonly string? _predecessor;

    private readonly string? _equivalent;

    private readonly string[] _pauses;

    private readonly ITestOutputHelper _output;

    internal Process Process { get; }

    internal BackupKillPointProcess(BackupKillPointMatrix matrix, string root, string point,
        string[] discovered, ITestOutputHelper output)
    {
        _matrix = matrix;
        _root = root;
        _point = point;
        _output = output;
        var index = Array.IndexOf(discovered, point);
        _predecessor = index > 0 ? discovered[index - 1] : null;
        _equivalent = BackupKillPointMatrix.UseMap ? matrix.Map.Pruned.GetValueOrDefault(point) : null;
        _pauses = BackupKillPointMatrix.FullMatrix
            ? new[] { _predecessor, _equivalent }.OfType<string>().Distinct().ToArray() : [];
        var move = matrix == BackupKillPointMatrix.Move;
        var start = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        start.ArgumentList.Add(typeof(BackupKillPointProcess).Assembly.Location);
        start.ArgumentList.Add(move ? "--move-backup-crash-child" : "--backup-crash-child");
        var temporary = Directory.CreateDirectory(Path.Combine(root, "temporary")).FullName;

        foreach (var name in new[] { "TMP", "TEMP", "TMPDIR" })
        {
            start.Environment[name] = temporary;
        }

        // Keep runtime sockets and debugger FIFOs out of the durable-state proof.
        start.Environment["DOTNET_EnableDiagnostics"] = "0";
        start.Environment[move ? "HP_MOVE_BACKUP_CHILD" : "HAPPY_PHOTON_BACKUP_CHILD"] = root;
        start.Environment[move ? "HP_MOVE_BACKUP_POINT" : "HAPPY_PHOTON_BACKUP_KILL"] = point;
        start.Environment["HP_KILL_PAUSES"] = JsonSerializer.Serialize(_pauses);
        Process = Process.Start(start)!;
    }

    internal async Task<string?> KillAsync()
    {
        var stderr = Process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TestWaits.Condition);
        var snapshots = new Dictionary<string, KillPointFingerprint>(StringComparer.Ordinal);
        var lines = new List<string>();
        KillPointFingerprint? paused = null;
        string? expectedHash = null;

        try
        {
            while (await Process.StandardOutput.ReadLineAsync(timeout.Token) is { } line)
            {
                lines.Add(line);

                if (line.StartsWith("KILL_PAUSE:", StringComparison.Ordinal))
                {
                    var point = line["KILL_PAUSE:".Length..];
                    Assert.Contains(point, _pauses);
                    snapshots.Add(point, KillPointFingerprint.Read(_root));
                    await Process.StandardInput.WriteLineAsync("CONTINUE");
                    await Process.StandardInput.FlushAsync();
                    continue;
                }

                if (!line.StartsWith("KILL_READY:", StringComparison.Ordinal)) continue;

                expectedHash = line["KILL_READY:".Length..];
                if (BackupKillPointMatrix.FullMatrix) paused = KillPointFingerprint.Read(_root);
                break;
            }
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            lines.Add("Timed out waiting for the child signal.");
        }
        finally
        {
            if (!Process.HasExited) Process.Kill(entireProcessTree: true);

            await Process.WaitForExitAsync();

            if (_matrix == BackupKillPointMatrix.Backup)
                await BackupCrashFileRelease.WaitAsync(_root, Process.Id, _output);
        }

        Assert.True(expectedHash != null,
            $"Missing kill point {_matrix.Name}/{_point}: {string.Join("\n", lines)}\n{await stderr}");
        Assert.NotEqual(0, Process.ExitCode);
        _output.WriteLine($"G2 {_matrix.PathName} {_point}: signalled; killed; exit={Process.ExitCode}");
        if (BackupKillPointMatrix.FullMatrix) Report(paused!, snapshots);

        return expectedHash;
    }

    private void Report(KillPointFingerprint paused, Dictionary<string, KillPointFingerprint> snapshots)
    {
        var killed = KillPointFingerprint.Read(_root);
        var previous = _predecessor == null ? null : snapshots.GetValueOrDefault(_predecessor);
        var kept = _equivalent == null ? null : snapshots.GetValueOrDefault(_equivalent);
        var report = new
        {
            Path = _matrix.PathName, Boundary = _point, Predecessor = _predecessor, Equivalent = _equivalent,
            Signalled = true, Process.ExitCode,
            KillInvariant = killed.SameAs(paused),
            EqualsPredecessor = previous != null && killed.SameAs(previous),
            EqualsEquivalent = kept != null && killed.SameAs(kept),
            Paused = paused, PostKill = killed, Pauses = snapshots,
            PostKillDigest = killed.Digest, PredecessorDigest = previous?.Digest, EquivalentDigest = kept?.Digest
        };
        var folder = Directory.CreateDirectory(Path.Combine(ReportRoot, _matrix.Name)).FullName;
        var reportName = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(_point)));
        var reportPath = Path.Combine(folder, reportName + ".json");
        File.WriteAllText(reportPath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        _output.WriteLine($"Kill-point fingerprint: {reportPath}");
        Assert.True(report.KillInvariant, $"Paused/post-kill fingerprint differs on {_matrix.Name}: {_point}");

        foreach (var point in _pauses)
        {
            Assert.True(snapshots.ContainsKey(point), $"Missing fingerprint pause on {_matrix.Name}: {point}");
        }

        if (_equivalent != null)
            Assert.True(report.EqualsEquivalent, $"{_matrix.Name} fingerprints differ: {_point} != {_equivalent}");
    }

    public void Dispose() => Process.Dispose();
}
