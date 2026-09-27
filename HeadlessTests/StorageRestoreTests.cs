using System.Diagnostics;
using System.IO.Compression;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class StorageRestoreTests(BackupCatalogFixtures fixtures, ITestOutputHelper output)
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Confirm_StagesOnlyJournalAndSidecar_ThenCancelSurvivesReopen(bool chooseFile)
    {
        using var directory = new TemporaryDirectory();
        var f = await RestoreTestSupport.CreateAsync(directory.Path);
        File.Delete(Path.ChangeExtension(f.Backup, ".manifest.json"));
        if (chooseFile)
        {
            var external = Path.Combine(directory.Path, "copied-backup.zip");
            File.Move(f.Backup, external);
            f = f with { Backup = external };
        }
        var original = RestoreTestSupport.Generation(f.Locations.CatalogRoot);
        var cache = CacheHashes(f.Locations);
        var archiveOpens = 0;
        var journal = new CatalogLocationMigrator(f.Service)
        {
            RestoreStep = step => { if (step == "archive-open") archiveOpens++; }
        };
        var storage = new StorageSettingsViewModel(f.Locations, journal);
        var chooser = new RestoreBackupViewModel(f.Locations.CatalogRoot, staged: true);
        if (chooseFile) Assert.Empty(chooser.Rows);
        else Assert.Equal("not checked", Assert.Single(chooser.Rows).State);
        var expectedDate = CatalogBackupService.VerifyArchive(f.Backup).Utc.ToString("g");
        chooser.ConfirmAsync = message =>
        {
            Assert.Contains(expectedDate, message);
            Assert.Contains("Changes since then are not included", message);
            Assert.Contains("Before restore backup", message);
            Assert.Contains("next launch", message);
            Assert.Contains("stay unchanged until then", message);
            Assert.Contains("XMP", message);
            return Task.FromResult(true);
        };
        storage.RequestRestoreAsync = () => AcceptAsync(chooser, f.Backup, chooseFile);
        await storage.RestoreBackupCommand.ExecuteAsync(null);
        Assert.Null(storage.CatalogError);
        Assert.Equal(0, archiveOpens); // Staging reused the chooser's accepted check.
        Assert.Equal(CatalogLocationMoveKind.Restore, (await journal.ReadJournalAsync()).Kind);
        Assert.Equal(CatalogRestorePhase.Prepared, (await journal.ReadJournalAsync()).Restore!.Phase);
        Assert.Equal(original, RestoreTestSupport.Generation(f.Locations.CatalogRoot));
        Assert.Equal(cache, CacheHashes(f.Locations));
        Assert.True(File.Exists(Path.ChangeExtension(f.Backup, ".manifest.json")));

        var reopened = new StorageSettingsViewModel(f.Locations, journal);
        await reopened.RefreshPendingAsync();
        Assert.Contains(expectedDate, reopened.CatalogStatus);
        Assert.True(reopened.HasPendingRestore);
        Assert.False(reopened.RestoreBackupCommand.CanExecute(null));
        Assert.False(reopened.ChangeCatalogCommand.CanExecute(null));
        Assert.False(reopened.ChangeCacheCommand.CanExecute(null));
        Assert.False(reopened.MoveCatalogCommand.CanExecute(null));
        Assert.False(reopened.MoveCacheCommand.CanExecute(null));
        await reopened.CancelRestoreCommand.ExecuteAsync(null);
        Assert.False(File.Exists(journal.JournalPath));
        Assert.False(reopened.HasPendingRestore);
        Assert.Null(reopened.CatalogStatus);
        Assert.True(reopened.RestoreBackupCommand.CanExecute(null));
        Assert.True(reopened.ChangeCatalogCommand.CanExecute(null));
        Assert.Equal(original, RestoreTestSupport.Generation(f.Locations.CatalogRoot));
    }

    [AvaloniaTheory]
    [InlineData(CatalogLocationMoveKind.Catalog)]
    [InlineData(CatalogLocationMoveKind.Cache)]
    public async Task PendingMove_DisablesRestoreAndCancelDoesNotDeleteIt(CatalogLocationMoveKind kind)
    {
        using var directory = new TemporaryDirectory();
        var f = await RestoreTestSupport.CreateAsync(directory.Path);
        var destination = Path.Combine(directory.Path, "destination");
        AppDataRootOwnership.ClaimFresh(destination);
        var journal = new CatalogLocationMigrator(f.Service);
        await journal.StageMoveAsync(f.Locations, kind, destination);
        var storage = new StorageSettingsViewModel(f.Locations, journal);
        await storage.RefreshPendingAsync();
        Assert.False(storage.RestoreBackupCommand.CanExecute(null));
        await storage.CancelRestoreCommand.ExecuteAsync(null);
        Assert.Equal(kind, (await journal.ReadJournalAsync()).Kind);
    }

    [AvaloniaFact]
    public async Task Cancel_DoesNotDeleteAnExecutingRestore()
    {
        using var directory = new TemporaryDirectory();
        var f = await RestoreTestSupport.CreateAsync(directory.Path);
        var journal = new CatalogLocationMigrator(f.Service);
        await new CatalogRestoreExecutor(journal).StageAsync(f.Locations, f.Backup);
        var pending = await journal.ReadJournalAsync();
        await journal.CommitAsync(pending with { Restore = pending.Restore! with { Phase = CatalogRestorePhase.Verified } });
        await new StorageSettingsViewModel(f.Locations, journal).CancelRestoreCommand.ExecuteAsync(null);
        Assert.True(File.Exists(journal.JournalPath));
    }

    [AvaloniaFact]
    public async Task StorageButton_OpensExistingChooser()
    {
        using var directory = new TemporaryDirectory();
        var f = await RestoreTestSupport.CreateAsync(directory.Path);
        using var catalog = new CatalogService(f.Locations.CatalogRoot);
        await using var vm = new MainWindowViewModel(catalog);
        vm.SetResolvedDataLocations(f.Locations, new CatalogLocationMigrator(f.Service));
        var dialog = new SettingsDialog(vm);
        dialog.FindControl<TabControl>("SettingsTabs")!.SelectedIndex = 2;
        using var scope = new TestUiScope(dialog);
        Dispatcher.UIThread.RunJobs();
        var button = Assert.Single(dialog.GetVisualDescendants().OfType<Button>(),
            button => Equals(button.Content, "Restore from backup…"));
        var action = vm.StorageSettings!.RestoreBackupCommand.ExecuteAsync(null);
        Dispatcher.UIThread.RunJobs();
        var chooser = Assert.IsType<RestoreBackupDialog>(Assert.Single(dialog.OwnedWindows));
        try
        {
            Assert.Contains(chooser.GetVisualDescendants().OfType<Button>(),
                control => Equals(control.Content, "Choose a backup file…"));
            Assert.Equal(new CatalogBackupService(catalog).ListForRestore(),
                Assert.IsType<RestoreBackupViewModel>(chooser.DataContext).Rows);
            Assert.Same(vm.StorageSettings.RestoreBackupCommand, button.Command);
        }
        finally { chooser.Close(); await action; }
        Assert.False(vm.StorageSettings.HasPendingRestore);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task G1_StorageChooser_ListsThirtyWithoutOpeningArchives(bool archiveFixture)
    {
        Assert.SkipWhen(archiveFixture && Environment.GetEnvironmentVariable("HAPPY_PHOTON_PERF") != "1",
            "Archive measurement runs under Claude's exclusive host measure lock with HAPPY_PHOTON_PERF=1.");
        using var directory = new TemporaryDirectory();
        var f = await RestoreTestSupport.CreateAsync(directory.Path, archiveFixture ? await fixtures.Archive : null);
        using var catalog = new CatalogService(f.Locations.CatalogRoot);
        var service = new CatalogBackupService(catalog);
        var paths = new List<string> { f.Backup };
        for (var i = 1; i < 30; i++)
        {
            var path = Path.Combine(service.Folder, $"hp-backup-{Guid.NewGuid():N}.zip");
            File.Copy(f.Backup, path);
            File.Copy(Path.ChangeExtension(f.Backup, ".manifest.json"), Path.ChangeExtension(path, ".manifest.json"));
            paths.Add(path);
        }
        File.Delete(Path.ChangeExtension(paths[3], ".manifest.json"));
        using (var zip = ZipFile.Open(paths[2], ZipArchiveMode.Update))
        {
            zip.GetEntry("catalog.db")!.Delete();
            using var writer = new StreamWriter(zip.CreateEntry("catalog.db").Open());
            writer.Write("damaged payload");
        }
        var opens = 0;
        service.Step = step => { if (step == "archive-open") opens++; };
        service.Attributes = path => paths.Take(2).Contains(path) ? FileAttributes.Offline : File.GetAttributes(path);
        var journal = new CatalogLocationMigrator(f.Service);
        var storage = new StorageSettingsViewModel(f.Locations, journal);
        RestoreBackupViewModel? chooser = null;
        var lockedArchives = new List<FileStream>();
        storage.RequestRestoreAsync = () =>
        {
            Assert.True(Dispatcher.UIThread.CheckAccess());
            // test-wait-policy: allow - approved G1 measures synchronous UI-thread list population.
            var clock = Stopwatch.StartNew();
            chooser = new RestoreBackupViewModel(f.Locations.CatalogRoot, service, staged: true);
            clock.Stop();
            output.WriteLine($"G1 archive={archiveFixture}: UI list {clock.Elapsed.TotalMilliseconds:F3} ms; archive opens counted {opens}; archives locked {lockedArchives.Count}");
            if (archiveFixture) Assert.InRange(clock.Elapsed.TotalMilliseconds, 0, 50);
            return Task.FromResult<(string, bool, CheckedCatalogBackup)?>(null);
        };
        // Deny content reads for every archive, including reads outside the instrumented check.
        try
        {
            foreach (var path in paths)
                lockedArchives.Add(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None));
            Assert.Equal(30, lockedArchives.Count);
            await storage.RestoreBackupCommand.ExecuteAsync(null);
            Assert.Null(storage.CatalogError);
        }
        finally
        {
            foreach (var archive in lockedArchives) archive.Dispose();
        }
        Assert.NotNull(chooser);
        Assert.Equal(30, chooser.Rows.Count);
        Assert.Equal(0, opens);
        Assert.Equal(2, chooser.Rows.Count(row => row.State.StartsWith("cloud-only")));
        Assert.Equal("not checked", chooser.Rows.Single(row => row.Path == paths[3]).State);
        chooser.Selected = chooser.Rows.Single(row => row.Path == paths[2]);
        chooser.ConfirmAsync = _ => throw new InvalidOperationException("Corruption must be refused before confirmation.");
        await chooser.RestoreCommand.ExecuteAsync(null);
        Assert.Contains("damaged", chooser.Error, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(journal.JournalPath));
        chooser.ConfirmAsync = _ => Task.FromResult(true);
        storage.RequestRestoreAsync = () => AcceptAsync(chooser, paths[3], chooseFile: true);
        await storage.RestoreBackupCommand.ExecuteAsync(null);
        Assert.Null(storage.CatalogError);
        await journal.ExecutePendingAsync();
        Assert.Equal(RestoreTestSupport.Payload(paths[3]), RestoreTestSupport.Generation(f.Locations.CatalogRoot));
    }

    [AvaloniaFact]
    public async Task PendingRestore_RendersShowcase()
    {
        using var directory = new TemporaryDirectory();
        var f = await RestoreTestSupport.CreateAsync(directory.Path);
        var journal = new CatalogLocationMigrator(f.Service);
        await new CatalogRestoreExecutor(journal).StageAsync(f.Locations, f.Backup);
        using var catalog = new CatalogService(f.Locations.CatalogRoot);
        await using var vm = new MainWindowViewModel(catalog);
        vm.SetResolvedDataLocations(f.Locations, journal);
        await vm.StorageSettings!.RefreshPendingAsync();
        var dialog = new SettingsDialog(vm);
        dialog.FindControl<TabControl>("SettingsTabs")!.SelectedIndex = 2;
        ShowcaseTestHelper.Capture("storage-restore-pending", dialog, new PixelSize(650, 610), ThemeVariant.Dark,
            shown =>
            {
                var buttons = shown.GetVisualDescendants().OfType<Button>().Where(b => b.IsEffectivelyVisible).ToArray();
                Assert.Contains(buttons, b => Equals(b.Content, "Cancel restore"));
                Assert.All(buttons.Where(b => Equals(b.Content, "Change") || Equals(b.Content, "Move")),
                    button => Assert.False(button.IsEffectivelyEnabled));
            });
    }

    internal static async Task<(string, bool, CheckedCatalogBackup)?> AcceptAsync(
        RestoreBackupViewModel chooser, string path, bool chooseFile)
    {
        (string, bool, CheckedCatalogBackup)? choice = null;
        chooser.Accepted = (accepted, different, check) => choice = (accepted, different, check);
        chooser.ChooseFileAsync = () => Task.FromResult<string?>(path);
        chooser.Selected = chooser.Rows.FirstOrDefault(row => row.Path == path);
        await (chooseFile ? chooser.ChooseFileCommand : chooser.RestoreCommand).ExecuteAsync(null);
        return choice;
    }

    private static string[] CacheHashes(AppDataLocations locations) => Directory.GetFiles(
        locations.CacheRoot, "*", SearchOption.AllDirectories).Order().Select(CatalogBackupService.Hash).ToArray();
}
