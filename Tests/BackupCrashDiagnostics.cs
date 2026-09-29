using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

// First-chance observation is test-only: List intentionally swallows read errors.
// Never rethrow or await catalog work from that callback, which can run under a gate.
internal sealed class BackupCrashDiagnostics : IDisposable
{
    private readonly CatalogService _catalog;
    private readonly CatalogBackupService _service;
    private readonly Process _child;
    private readonly ITestOutputHelper _output;
    private readonly ConcurrentQueue<string> _evidence = new();
    private readonly List<string> _steps = [];
    private DateTimeOffset? _start;
    private string _lastStep = "not-started";
    private string _lastWorkStep = "not-started";

    [ThreadStatic]
    private static bool _capturing;

    public BackupCrashDiagnostics(CatalogService catalog, CatalogBackupService service,
        Process child, ITestOutputHelper output)
    {
        _catalog = catalog;
        _service = service;
        _child = child;
        _output = output;
        service.Step = RecordStep;
        AppDomain.CurrentDomain.FirstChanceException += OnException;
    }

    public void BeginAttempt()
    {
        _steps.Clear();
        _lastStep = _lastWorkStep = "not-started";
        _start = DateTimeOffset.UtcNow;
    }

    private void RecordStep(string step)
    {
        _lastStep = step;
        _steps.Add(step);

        if (step != "list" && !step.EndsWith(":outcome", StringComparison.Ordinal))
        {
            _lastWorkStep = step;
        }
    }

    private void OnException(object? sender, FirstChanceExceptionEventArgs args)
    {
        if (_capturing) return;
        if (args.Exception is not (IOException or UnauthorizedAccessException or JsonException)) return;

        _capturing = true;

        try
        {
            var frames = new StackTrace().GetFrames();
            var inList = frames.Any(frame => frame.GetMethod() is { } method &&
                method.DeclaringType == typeof(CatalogBackupService) && method.Name == "List");
            var inBackup = frames.Any(frame => frame.GetMethod()?.DeclaringType?.FullName?
                .StartsWith(typeof(CatalogBackupService).FullName!, StringComparison.Ordinal) == true);
            if (!inBackup) return;

            // Probe at the throw, before an async outcome read can let a transient lock disappear.
            var exception = args.Exception;
            var probe = BackupCrashLockDiagnostics.IsSharingViolation(exception)
                ? BackupCrashLockDiagnostics.Capture(exception, _catalog.CatalogPath, _child.Id)
                : ProbeNamedFile(exception.Message);
            _evidence.Enqueue($"utc={DateTimeOffset.UtcNow:O} swallowed_list_read={inList} " +
                $"step={_lastStep} work_step={_lastWorkStep} " +
                $"exception={exception.GetType().Name} hresult=0x{exception.HResult:X8} " +
                $"message={exception.Message}\n{probe}\n{Snapshot()}");
        }
        catch (Exception ex)
        {
            _evidence.Enqueue($"diagnostic_capture_failed={ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            _capturing = false;
        }
    }

    public Task ReportReadFailuresAsync() =>
        _evidence.IsEmpty ? Task.CompletedTask : ReportAsync("observed backup/read exception");

    public async Task ReportAsync(string trigger)
    {
        _output.WriteLine($"BACKUP_DIAGNOSTIC trigger={trigger} start_utc={_start:O} " +
            $"last_step={_lastStep} last_work_step={_lastWorkStep}");

        while (_evidence.TryDequeue(out var evidence))
        {
            _output.WriteLine(evidence);
        }

        try
        {
            var raw = await _catalog.GetAppSettingAsync(CatalogBackupService.OutcomeKey);
            _output.WriteLine($"outcome_read_succeeded=true raw={raw ?? "<missing>"}");
            var outcome = raw == null ? null : JsonSerializer.Deserialize<BackupOutcome>(raw);
            var freshness = outcome == null ? "missing" :
                _start.HasValue && outcome.Utc >= _start.Value ? "fresh" : "stale";
            _output.WriteLine($"outcome_freshness={freshness} status={outcome?.Status ?? "<missing>"}");
            _output.WriteLine(ProbeNamedFile(outcome?.Reason));
        }
        catch (JsonException ex)
        {
            _output.WriteLine($"outcome_parse_succeeded=false error={ex.Message}");
        }
        catch (Exception ex)
        {
            _output.WriteLine($"outcome_read_succeeded=false error={ex.GetType().Name}: {ex.Message}");
        }

        _output.WriteLine(Snapshot());
        _output.WriteLine("steps=" + string.Join(",", _steps));
        _output.WriteLine("classification=requires-fresh-failed-outcome; ownership=unproven unless Restart Manager identifies an owner above");
    }

    private string Snapshot()
    {
        var lines = new List<string>();

        try
        {
            _child.Refresh();
            lines.Add($"child_pid={_child.Id} child_alive={!_child.HasExited}");
        }
        catch (Exception ex)
        {
            lines.Add($"child_liveness_read_failed={ex.Message}");
        }

        try
        {
            var names = Directory.GetFiles(_service.Folder).Order(StringComparer.Ordinal).ToArray();
            lines.Add($"backup_folder={_service.Folder} file_count={names.Length}");

            foreach (var path in names)
            {
                lines.Add("file=" + Path.GetFileName(path));
            }
        }
        catch (Exception ex)
        {
            lines.Add($"folder_listing_failed={ex.GetType().Name}: {ex.Message}");
        }

        return string.Join("\n", lines);
    }

    private string ProbeNamedFile(string? message)
    {
        if (message == null || !(message.Contains("being used", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("another process", StringComparison.OrdinalIgnoreCase))) return "exclusive_probe=not-applicable";

        var match = Regex.Match(message, """['"](?<path>[^'"]+)['"]""");
        if (!match.Success) return "exclusive_probe=unavailable (no quoted file name)";

        var path = Path.GetFullPath(match.Groups["path"].Value);
        var root = Path.GetFullPath(_catalog.CatalogPath) + Path.DirectorySeparatorChar;
        if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return "exclusive_probe=refused (outside test catalog)";

        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);

            return $"exclusive_probe=opened file={path} utc={DateTimeOffset.UtcNow:O}";
        }
        catch (Exception ex)
        {
            return $"exclusive_probe=failed file={path} utc={DateTimeOffset.UtcNow:O} " +
                $"hresult=0x{ex.HResult:X8} error={ex.GetType().Name}: {ex.Message}";
        }
    }

    public void Dispose()
    {
        AppDomain.CurrentDomain.FirstChanceException -= OnException;
        _service.Step = null;
    }
}
