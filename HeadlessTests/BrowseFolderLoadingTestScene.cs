using System.Reflection;
using Avalonia.Controls;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

internal sealed class BrowseFolderLoadingTestScene : IAsyncDisposable
{
    private readonly CatalogVmFixture _files;

    private readonly CatalogService _catalog;

    private readonly TestUiScope _scope;

    private bool _vmDisposed;

    private BrowseFolderLoadingTestScene(CatalogVmFixture files, CatalogService catalog)
    {
        _files = files;
        _catalog = catalog;
        Vm = files.CreateViewModel(catalog, postSelection: _ => { });
        // test-teardown-policy: allow - ForMainWindow owns binding; DisposeAsync closes its scope before disposing the VM.
        var window = new MainWindow();
        _scope = TestUiScope.ForMainWindow(window, Vm);
        View = window.FindControl<BrowseGridView>("BrowseGridView")!;
        Vm.ShowWorkspaceReady(1);
    }

    internal MainWindowViewModel Vm { get; }

    internal BrowseGridView View { get; }

    internal Control Empty => View.FindControl<Control>("EmptyState")!;

    internal Control FilteredEmpty => View.FindControl<Control>("FilteredEmptyState")!;

    internal Control Grid => View.FindControl<Control>("ThumbnailGrid")!;

    internal static async Task<BrowseFolderLoadingTestScene> CreateAsync()
    {
        var files = new CatalogVmFixture("browse-loading");
        var catalog = await files.CreateCatalogAsync("catalog");

        return new BrowseFolderLoadingTestScene(files, catalog);
    }

    internal string Folder(string name, bool photo = false, bool subfolder = false)
    {
        var path = Directory.CreateDirectory(_files.Path(name)).FullName;

        if (photo)
        {
            TestImages.WriteJpeg(Path.Combine(path, "photo.jpg"), width: 24, height: 16);
        }

        if (subfolder)
        {
            Directory.CreateDirectory(Path.Combine(path, "child"));
        }

        return path;
    }

    internal void AssertBlank()
    {
        Assert.True(Vm.IsBrowseFolderLoading);
        Assert.True(View.IsFolderLoading);
        Assert.False(Empty.IsVisible);
        Assert.False(FilteredEmpty.IsVisible);
        Assert.False(Grid.IsVisible);
    }

    internal static T Field<T>(MainWindowViewModel vm, string name) =>
        (T)typeof(MainWindowViewModel).GetField(name,
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(vm)!;

    internal static void SetField(MainWindowViewModel vm, string name, object value) =>
        typeof(MainWindowViewModel).GetField(name,
            BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(vm, value);

    internal async Task DisposeViewModelAsync()
    {
        if (_vmDisposed) return;

        _vmDisposed = true;
        await Vm.DisposeAsync();
    }

    public async ValueTask DisposeAsync()
    {
        _scope.Dispose();
        await DisposeViewModelAsync();
        _catalog.Dispose();
        _files.Dispose();
    }
}

internal sealed class BrowseScanBarrier : IDisposable
{
    private readonly ManualResetEventSlim _release = new();

    internal TaskCompletionSource Entered { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal FolderScanResult Scan(string path)
    {
        Entered.TrySetResult();
        Assert.True(_release.Wait(TestWaits.Condition), "The test did not release the folder scan.");

        return new FolderService().ScanFolder(path);
    }

    internal void Release() => _release.Set();

    public void Dispose() => _release.Dispose();
}
