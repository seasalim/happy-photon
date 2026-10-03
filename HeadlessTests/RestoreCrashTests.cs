using System.Diagnostics;
using System.IO.Compression;
using Avalonia.Headless.XUnit;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class RestoreCrashTests(BackupCatalogFixtures fixtures, ITestOutputHelper output)
{
    private const int BatchCount = 16;

    public static IEnumerable<object[]> OperationBatches()
    {
        foreach (var corrupt in new[] { false, true })
        {
            foreach (var errorScreen in new[] { false, true })
            {
                for (var batch = 0; batch < BatchCount; batch++)
                {
                    yield return [corrupt, errorScreen, batch];
                }
            }
        }
    }

    [AvaloniaTheory]
    [MemberData(nameof(OperationBatches))]
    public Task G2_KillEveryOperation_RecoversACompleteGeneration(bool corrupt, bool errorScreen, int batch) =>
        RunMatrix(corrupt, errorScreen, matchingPreset: true, batch);

    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public Task G2_KillCaseOnlyReplacementOfDifferentBytes_RecoversACompleteGeneration(bool corrupt, bool errorScreen) =>
        RunMatrix(corrupt, errorScreen, matchingPreset: false);

    private async Task RunMatrix(bool corrupt, bool errorScreen, bool matchingPreset, int? batch = null)
    {
        var fixture = await fixtures.Everyday;
        using var probe = new TemporaryDirectory();
        var sample = await Prepare(probe.Path);
        var points = new List<string>();
        var store = new CatalogLocationMigrator(sample.Service) { RestoreStep = points.Add };
        await new CatalogRestoreExecutor(store) { Step = points.Add }.StageAsync(sample.Locations, sample.Backup, true);
        await store.ExecutePendingAsync();
        var boundaries = points.Where(p => p.StartsWith("before:") || p.StartsWith("after:")).Distinct().ToArray();
        Assert.Contains("before:replace:catalog.db", boundaries);
        Assert.Contains("after:replace:.catalog-identity", boundaries);
        Assert.Contains("after:journal-delete", boundaries);
        if (!matchingPreset)
        {
            // The other theory covers shared operations; exercise this destructive branch separately.
            boundaries = boundaries.Where(point => point.Contains(":case-delete:")).ToArray();
            if (OperatingSystem.IsWindows()) Assert.Equal(2, boundaries.Length);
        }

        if (batch.HasValue)
        {
            // Report progress between bounded cases without dropping any discovered kill point.
            boundaries = boundaries.Order(StringComparer.Ordinal)
                .Where((_, index) => index % BatchCount == batch.Value).ToArray();
            Assert.NotEmpty(boundaries);
        }

        foreach (var point in boundaries)
        {
            using var directory = new TemporaryDirectory();
            var f = await Prepare(directory.Path);
            var original = RestoreTestSupport.Generation(f.Locations.CatalogRoot);
            var restored = RestoreTestSupport.Payload(f.Backup);
            await KillChild(directory.Path, f.Backup, point, errorScreen);
            // P-1: committed replacement must not need the external archive anymore.
            var journal = new CatalogLocationMigrator(f.Service);
            var state = File.Exists(journal.JournalPath) ? (await journal.ReadJournalAsync()).Restore : null;
            if (state?.Phase >= CatalogRestorePhase.Replacing)
                File.Move(f.Backup, f.Backup + ".unavailable");
            using var catalog = new CatalogService();
            var vm = new MainWindowViewModel(catalog);
            var window = new MainWindow();
            using var scope = TestUiScope.ForMainWindow(window, vm);
            window.BeforeStartupSettingsLoad = () => throw new IOException("Keep recovery at startup for byte comparison.");
            try
            {
                await window.InitializeApplicationAsync(vm, catalog, f.Service, journal, Path.Combine(directory.Path, "pictures"))
                    .WaitAsync(TestWaits.Condition);
                catalog.Dispose();
                var actual = RestoreTestSupport.Generation(f.Locations.CatalogRoot);
                var changed = !original.SequenceEqual(actual);
                Assert.Equal(changed ? restored : original, actual);
                if (changed) RestoreTestSupport.AssertPreserved(f, original, corrupt);
                using var listingCatalog = new CatalogService(f.Locations.CatalogRoot);
                var backupFolder = new CatalogBackupService(listingCatalog).Folder;
                Assert.Empty(Directory.GetFiles(backupFolder, "*.partial.*"));
                if (point == "after:journal-delete")
                    Assert.NotNull(CatalogRestoreExecutor.ReadNotice(f.Locations.CatalogRoot));
            }
            finally { scope.Dispose(); await vm.DisposeAsync(); }
            output.WriteLine($"G2 corrupt={corrupt} error_screen={errorScreen} {point}: complete bytes/identity/presets; preservation passed");
        }
        output.WriteLine($"G2: {boundaries.Length} real process terminations passed for this path/generation.");

        async Task<RestoreFixture> Prepare(string root)
        {
            var f = await RestoreTestSupport.CreateAsync(root, fixture);
            if (matchingPreset)
            {
                using var zip = ZipFile.OpenRead(f.Backup);
                zip.GetEntry("presets/first.json")!.ExtractToFile(
                    Path.Combine(f.Locations.PresetsRoot, "first.json"), overwrite: true);
            }
            File.Move(Path.Combine(f.Locations.PresetsRoot, "first.json"),
                Path.Combine(f.Locations.PresetsRoot, "FIRST.json"));
            if (corrupt) File.WriteAllText(f.Locations.DatabasePath, "genuinely corrupt database");
            File.WriteAllText(Path.Combine(f.Locations.CatalogRoot, ".catalog-identity"),
                $$"""{"version":1,"catalogId":"{{Guid.NewGuid()}}"}""");
            return f;
        }
    }

    private static async Task KillChild(string root, string backup, string point, bool errorScreen)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        foreach (var argument in new[] { typeof(RestoreCrashTests).Assembly.Location,
                     "-method", "HappyPhoton.Tests.RestoreCrashTests.Child", "-showLiveOutput", "-noColor" })
            start.ArgumentList.Add(argument);
        var temporary = Directory.CreateDirectory(Path.Combine(root, "temporary")).FullName;
        start.Environment["TMP"] = temporary;
        start.Environment["TEMP"] = temporary;
        start.Environment["HP_RESTORE_CHILD"] = root;
        start.Environment["HP_RESTORE_BACKUP"] = backup;
        start.Environment["HP_RESTORE_POINT"] = point;
        start.Environment["HP_RESTORE_ERROR_SCREEN"] = errorScreen.ToString();
        using var process = Process.Start(start)!;
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TestWaits.Condition);
        var signalled = false;
        var lines = new List<string>();
        try
        {
            while (await process.StandardOutput.ReadLineAsync(timeout.Token) is { } line)
            {
                lines.Add(line);
                if (line != "RESTORE_KILL_READY") continue;
                signalled = true;
                break;
            }
        }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
        }
        Assert.True(signalled, $"Missing kill point {point}: {string.Join("\n", lines)}\n{await stderr}");
        Assert.NotEqual(0, process.ExitCode);
    }

    [AvaloniaFact]
    public async Task Child()
    {
        var root = Environment.GetEnvironmentVariable("HP_RESTORE_CHILD");
        Assert.SkipWhen(root == null, "Only the process-termination parent invokes this test.");
        var service = RestoreTestSupport.Service(root!);
        var locations = (await service.ResolveAsync())!;
        var journal = new CatalogLocationMigrator(service) { RestoreStep = KillAt };
        var backup = Environment.GetEnvironmentVariable("HP_RESTORE_BACKUP")!;
        if (Environment.GetEnvironmentVariable("HP_RESTORE_ERROR_SCREEN") == "True")
        {
            using var catalog = new CatalogService();
            var vm = new MainWindowViewModel(catalog);
            var window = new MainWindow();
            using var scope = TestUiScope.ForMainWindow(window, vm);
            window.BeforeStartupSettingsLoad = () => throw new IOException("Settings failed after open");
            window.ShowRestoreNotice = _ => Task.CompletedTask;
            await window.InitializeApplicationAsync(vm, catalog, service, journal, Path.Combine(root!, "pictures"));
            Assert.True(vm.IsStartupError);
            window.BeforeStartupSettingsLoad = null;
            await window.RestoreCatalogAsync(vm, backup, true);
        }
        else
        {
            await new CatalogRestoreExecutor(journal) { Step = KillAt }.StageAsync(locations, backup, true);
            // The production next-launch executor runs before any catalog connection is opened.
            await journal.ExecutePendingAsync();
        }
        Assert.Fail("The requested kill point was not reached.");

        void KillAt(string point)
        {
            if (point != Environment.GetEnvironmentVariable("HP_RESTORE_POINT")) return;
            using var signal = new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true };
            signal.WriteLine("RESTORE_KILL_READY");
            using var wait = new ManualResetEventSlim();
            Assert.True(wait.Wait(TestWaits.Condition), "Parent did not terminate this process.");
        }
    }
}
