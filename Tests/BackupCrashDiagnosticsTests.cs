using System.Diagnostics;
using System.Text.Json;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

[Collection(BackupBaselineCollection.Name)]
public sealed class BackupCrashDiagnosticsTests(ITestOutputHelper output)
{
    [Fact]
    public async Task SwallowedListReadFailure_IsCapturedAtTheThrow()
    {
        Assert.SkipWhen(!OperatingSystem.IsWindows(), "Windows file-sharing diagnostic.");

        using var directory = new TemporaryDirectory();
        using var catalog = new CatalogService(directory.Path);
        await catalog.InitializeAsync();
        var service = new CatalogBackupService(catalog);
        await service.BackupAsync();
        var entry = Assert.Single(service.List());
        using var child = Process.GetCurrentProcess();
        using var diagnostics = new BackupCrashDiagnostics(catalog, service, child, output);
        diagnostics.BeginAttempt();

        using (var locked = new FileStream(entry.Path + ".manifest.json",
            FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert.Empty(service.List());
            await diagnostics.ReportReadFailuresAsync();
        }

        Assert.Contains("swallowed_list_read=True", output.Output);
        Assert.Contains("outcome_read_succeeded=true", output.Output);
        Assert.Contains("outcome_freshness=stale", output.Output);
        Assert.Contains("exclusive_probe=failed", output.Output);
        Assert.Contains("ownership=test-process", output.Output);
        Assert.Contains("target_ms=2000", output.Output);
        Assert.Contains("child_alive=True", output.Output);
        Assert.Contains(Path.GetFileName(entry.Path) + ".manifest.json", output.Output);
    }

    [Fact]
    public async Task FreshIdentityReadFailure_KeepsFailingStepAfterOutcomeMarkers()
    {
        Assert.SkipWhen(!OperatingSystem.IsWindows(), "Windows file-sharing diagnostic.");

        using var directory = new TemporaryDirectory();
        using var catalog = new CatalogService(directory.Path);
        await catalog.InitializeAsync();
        var service = new CatalogBackupService(catalog);
        var path = Path.Combine(directory.Path, ".catalog-identity");
        using var child = Process.GetCurrentProcess();
        using var diagnostics = new BackupCrashDiagnostics(catalog, service, child, output);
        diagnostics.BeginAttempt();

        using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            await service.BackupAsync();
            Assert.True(service.IsDue());
            await diagnostics.ReportAsync("injected test-owned sharing violation");
        }

        Assert.Contains("outcome_freshness=fresh status=failed", output.Output);
        Assert.Contains("last_work_step=after:snapshot", output.Output);
        Assert.Contains("step=after:snapshot", output.Output);
        Assert.Contains("hresult=0x80070020", output.Output);
        Assert.Contains("exclusive_probe=failed", output.Output);
        Assert.Contains("ownership=test-process", output.Output);
        Assert.Contains("target_ms=2000", output.Output);
        Assert.Contains(Path.GetFileName(path), output.Output);
        Assert.Contains("ownership=unproven", output.Output);
    }

    [Theory]
    [InlineData(null, "outcome_freshness=missing")]
    [InlineData("stale", "outcome_freshness=stale")]
    [InlineData("ok", "outcome_freshness=fresh status=ok")]
    public async Task OutcomeFreshness_DoesNotTreatMissingStaleOrOkAsCause(string? status, string expected)
    {
        using var directory = new TemporaryDirectory();
        using var catalog = new CatalogService(directory.Path);
        await catalog.InitializeAsync();
        var service = new CatalogBackupService(catalog);
        Directory.CreateDirectory(service.Folder);
        using var child = Process.GetCurrentProcess();
        using var diagnostics = new BackupCrashDiagnostics(catalog, service, child, output);
        diagnostics.BeginAttempt();

        if (status != null)
        {
            var outcome = new BackupOutcome(status == "stale" ? DateTimeOffset.UtcNow.AddDays(-1) :
                DateTimeOffset.UtcNow, status == "stale" ? "failed" : status);
            await catalog.SetAppSettingAsync(CatalogBackupService.OutcomeKey, JsonSerializer.Serialize(outcome));
        }

        Assert.True(service.IsDue());
        await diagnostics.ReportAsync("freshness control");
        Assert.Contains(expected, output.Output);
        Assert.Contains("ownership=unproven", output.Output);
    }
}
