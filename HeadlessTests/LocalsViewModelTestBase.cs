using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

public abstract class LocalsViewModelTestBase : IDisposable
{
    private protected readonly CatalogVmFixture _fixture = new("locals-vm");

    private protected MainWindowViewModel CreateVm(CatalogService catalog, TimeProvider? clock = null,
        bool raw = false, bool mono = false)
    {
        var vm = _fixture.CreateViewModel(catalog, new LocalTestLoader(raw, mono),
            _ => Task.CompletedTask, new TestSourceAvailabilityService(SourceAvailability.AvailableLocally), timeProvider: clock);
        vm.IsDevelopMode = true;

        return vm;
    }

    private protected async Task Prepare(MainWindowViewModel vm, CatalogService catalog)
    {
        var image = new ImageFile(_fixture.Path("photo.jpg"));
        image.CatalogId = await catalog.GetOrCreateImageAsync(image.FilePath);
        vm.Browse.SetImages([image]);
        vm.SelectedImage = image;
        await TestWaits.UntilAsync(() => vm.IsHistoryLoaded && vm.PreviewImage != null);
    }

    public void Dispose() => _fixture.Dispose();

    private protected async Task PrepareShortcutLocal(MainWindowViewModel vm, CatalogService catalog)
    {
        await Prepare(vm, catalog);
        vm.ShowWorkspaceReady(MainWindowViewModel.CurrentFirstRunExperienceVersion);
        await vm.ToggleLocalsModeCommand.ExecuteAsync(null);
        await vm.PlaceLocalAtCenterCommand.ExecuteAsync(null);
    }

    private protected static void ShortcutDown(MainWindow window, Key key, RawInputModifiers modifiers = RawInputModifiers.None) =>
        window.KeyPress(key, modifiers, PhysicalKey.None, null);

    private protected static void ShortcutUp(MainWindow window, Key key, RawInputModifiers modifiers = RawInputModifiers.None) =>
        window.KeyRelease(key, modifiers, PhysicalKey.None, null);

    private protected static void ShortcutPress(MainWindow window, Key key, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        ShortcutDown(window, key, modifiers);
        ShortcutUp(window, key, modifiers);
    }

    private protected static async Task<(int History, string Saved, string Live)> ShortcutSnapshot(MainWindowViewModel vm, CatalogService catalog)
    {
        // Returning to Develop reloads history asynchronously.
        if (vm.PendingHistoryLoadTask is { } historyLoad)
        {
            await historyLoad.WaitAsync(TestWaits.Condition);
        }

        var image = vm.SelectedImage!;
        var saved = (await catalog.LoadImageStatesAsync([image.FilePath]))[image.FilePath].Single().EditSettings;

        return (vm.HistoryEntries.Count, RenderSettingsHash.Compute(saved), RenderSettingsHash.Compute(image.EditSettings));
    }

    private protected static async Task AssertShortcutSnapshot(MainWindowViewModel vm, CatalogService catalog,
        (int History, string Saved, string Live) before) => Assert.Equal(before, await ShortcutSnapshot(vm, catalog));

    private protected static void SettleHit(Window window, Control control) => ShowcaseTestHelper.Settle(() =>
    {
        var center = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
        var target = window.InputHitTest(center) as Visual;

        return ReferenceEquals(target, control) || target?.GetVisualAncestors().Contains(control) == true;
    }, $"{control.Name} expansion and scroll");
}
