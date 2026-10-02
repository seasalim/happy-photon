using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class DialogChromeStartupBaselineTests(ITestOutputHelper output)
{
    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CaptureIsolatedStartupGate(bool pointerRecovery)
    {
        Assert.SkipWhen(Environment.GetEnvironmentVariable("HAPPY_PHOTON_DIALOG_STARTUP_CAPTURE") != "1",
            "Set HAPPY_PHOTON_DIALOG_STARTUP_CAPTURE=1 to write isolated WP9 after-captures.");

        for (var run = 1; run <= 3; run++)
        {
            await CaptureAsync(pointerRecovery, run);
        }
    }

    internal async Task CaptureAsync(bool pointerRecovery, int run)
    {
        using var directory = new TemporaryDirectory();
        using var roots = new StartupEnvironmentRoots(directory.Path);
        var pictures = Directory.CreateDirectory(Path.Combine(directory.Path, "pictures")).FullName;
        var pointer = Directory.CreateDirectory(Path.Combine(directory.Path, "pointer")).FullName;
        var service = new AppDataLocationService(new AppDataPlatformPaths(
            pictures, pointer, roots.Catalog, roots.Cache), Environment.GetEnvironmentVariable);
        var migrator = new CatalogLocationMigrator(service);
        Assert.StartsWith(directory.Path + Path.DirectorySeparatorChar, Path.GetFullPath(service.PointerPath));
        Assert.StartsWith(directory.Path + Path.DirectorySeparatorChar, Path.GetFullPath(migrator.JournalPath));

        if (pointerRecovery)
        {
            await File.WriteAllTextAsync(service.PointerPath, "invalid throwaway pointer");
        }
        else
        {
            Directory.CreateDirectory(roots.Catalog);
            await File.WriteAllTextAsync(Path.Combine(roots.Catalog, "catalog.db"), "invalid throwaway catalog");
        }

        using var catalog = new CatalogService(roots.Catalog);
        await using var vm = new MainWindowViewModel(catalog);
        using var theme = new TestUiScope(theme: ThemeVariant.Dark);
        var window = new MainWindow { Width = 1200, Height = 700 };
        using var scope = TestUiScope.ForMainWindow(window, vm);
        await window.InitializeApplicationAsync(vm, catalog, service,
            migrator, pictures).WaitAsync(TestWaits.Condition);
        Assert.Equal(pointerRecovery ? StartupGateState.PointerRecovery : StartupGateState.Error, vm.StartupGateState);
        DialogChromeBaselineTests.Settle(window);
        using var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        Assert.Equal(new PixelSize(1200, 700), frame.PixelSize);
        var scene = pointerRecovery ? "pointer-recovery" : "catalog-error";
        var shotDirectory = Path.Combine(GoldenTestPaths.RepositoryRoot, "artifacts", "shots");
        var shotPath = Path.Combine(shotDirectory, $"wp9-startup-{scene}-after-{run}.png");
        Directory.CreateDirectory(shotDirectory);
        frame.Save(shotPath);
        output.WriteLine($"StartupGate run={run}: state={vm.StartupGateState}; pixels={frame.PixelSize}; path={shotPath}; " +
            $"catalogOverride={roots.Catalog}; cacheOverride={roots.Cache}; pointer={service.PointerPath}");
        Assert.Equal(pointerRecovery, File.Exists(service.PointerPath));

        if (pointerRecovery)
        {
            Assert.Equal("invalid throwaway pointer", await File.ReadAllTextAsync(service.PointerPath));
        }
        else
        {
            Assert.Equal("invalid throwaway catalog",
                await File.ReadAllTextAsync(Path.Combine(roots.Catalog, "catalog.db")));
        }
    }

    private sealed class StartupEnvironmentRoots : IDisposable
    {
        private readonly string? _priorCatalog = Environment.GetEnvironmentVariable(AppDataLocationService.CatalogEnvironmentVariable);
        private readonly string? _priorCache = Environment.GetEnvironmentVariable(AppDataLocationService.CacheEnvironmentVariable);

        public string Catalog { get; }

        public string Cache { get; }

        public StartupEnvironmentRoots(string root)
        {
            Catalog = Path.Combine(root, "catalog");
            Cache = Path.Combine(root, "cache");
            Environment.SetEnvironmentVariable(AppDataLocationService.CatalogEnvironmentVariable, Catalog);
            Environment.SetEnvironmentVariable(AppDataLocationService.CacheEnvironmentVariable, Cache);
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable(AppDataLocationService.CatalogEnvironmentVariable, _priorCatalog);
            Environment.SetEnvironmentVariable(AppDataLocationService.CacheEnvironmentVariable, _priorCache);
        }
    }
}
