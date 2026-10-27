using System.Text.Json;
using Avalonia.Headless;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

internal static class RestoreCrashChild
{
    internal static async Task<int> RunAsync()
    {
        try
        {
            var root = Environment.GetEnvironmentVariable("HP_RESTORE_CHILD")!;
            Assert.True(AppDataRootOwnership.IsSameOrDescendant(
                AppDataRootOwnership.ResolveRealPath(root), AppDataRootOwnership.ResolveRealPath(Path.GetTempPath())),
                "Restore child TEMP must be contained by its fixture root.");

            if (Environment.GetEnvironmentVariable("HP_RESTORE_ERROR_SCREEN") == "True")
            {
                using var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RestoreCrashChild).Assembly);
                await session.Dispatch(async () =>
                {
                    await RestoreAsync(root, errorScreen: true);

                    return 0;
                }, CancellationToken.None);
            }
            else
            {
                await RestoreAsync(root, errorScreen: false);
            }

            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static async Task RestoreAsync(string root, bool errorScreen)
    {
        var service = RestoreTestSupport.Service(root);
        var locations = (await service.ResolveAsync())!;
        var backup = Environment.GetEnvironmentVariable("HP_RESTORE_BACKUP")!;
        var target = Environment.GetEnvironmentVariable("HP_RESTORE_POINT")!;
        var pauses = JsonSerializer.Deserialize<string[]>(
            Environment.GetEnvironmentVariable("HP_RESTORE_PAUSES") ?? "[]")!;
        using var signal = new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true };
        var journal = new CatalogLocationMigrator(service) { RestoreStep = KillAt };

        if (errorScreen)
        {
            using var catalog = new CatalogService();
            var vm = new MainWindowViewModel(catalog);
            var window = new MainWindow();
            using var scope = TestUiScope.ForMainWindow(window, vm);
            Assert.Equal(nint.Zero, window.TryGetPlatformHandle()?.Handle ?? nint.Zero);
            window.BeforeStartupSettingsLoad = () => throw new IOException("Settings failed after open");
            window.ShowRestoreNotice = _ => Task.CompletedTask;
            await window.InitializeApplicationAsync(vm, catalog, service, journal, Path.Combine(root, "pictures"));
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
            if (point == target)
            {
                signal.WriteLine("RESTORE_KILL_READY");
                using var wait = new ManualResetEventSlim();
                Assert.True(wait.Wait(TestWaits.Condition), "Parent did not terminate this process.");
            }
            else if (pauses.Contains(point, StringComparer.Ordinal))
            {
                signal.WriteLine("RESTORE_PAUSE:" + point);
                var reply = Console.In.ReadLineAsync().WaitAsync(TestWaits.Condition).GetAwaiter().GetResult();
                Assert.Equal("CONTINUE", reply);
            }
        }
    }
}
