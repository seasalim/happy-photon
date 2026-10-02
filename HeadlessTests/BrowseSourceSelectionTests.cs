using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class BrowseSourceSelectionTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PointerRules_KeepTheFirstSelectedSource(bool loupe)
    {
        using var fixture = new CatalogVmFixture("browse-source-pointer");
        using var catalog = await fixture.CreateCatalogAsync();
        await using var vm = CreateVm(fixture, catalog);
        var photos = Photos(fixture);
        vm.Browse.SetImages(photos);
        var window = new MainWindow { Width = 1400, Height = 800 };
        using var scope = TestUiScope.ForMainWindow(window, vm);

        Click(0);
        Check(0, 0);

        if (loupe)
        {
            vm.EnterLoupeCommand.Execute(null);
        }

        Click(2, RawInputModifiers.Control);
        Check(0, 0, 2);
        Click(1, RawInputModifiers.Control);
        Check(0, 0, 1, 2);
        Click(2, RawInputModifiers.Control);
        Check(0, 0, 1);
        Click(3, RawInputModifiers.Shift);
        Check(0, 0, 1, 2, 3);
        Click(2, RawInputModifiers.Shift);
        Check(0, 0, 1, 2, 3);
        Click(0, RawInputModifiers.Control);
        Check(1, 1, 2, 3);
        Click(2, RawInputModifiers.Alt);
        Check(2, 1, 2, 3);
        Click(3, RawInputModifiers.Control);
        Check(2, 1, 2);
        Click(3, RawInputModifiers.Alt);
        Check(3, 3);
        Click(1, RawInputModifiers.Control);
        Check(3, 1, 3);
        Click(1, button: MouseButton.Right);
        Check(1, 1, 3);
        Click(0);
        Check(0, 0);
        Click(1);
        Check(1, 1);
        Click(1, RawInputModifiers.Control);
        Check(1);
        // Rebuilding an emptied selection puts the ring on its first photo (review C-1).
        Click(2, RawInputModifiers.Control);
        Check(2, 2);
        Click(3, RawInputModifiers.Control);
        Check(2, 2, 3);
        vm.DeselectAllCommand.Execute(null);
        Click(0, RawInputModifiers.Control);
        Check(0, 0);
        Click(1, RawInputModifiers.Control);
        Check(0, 0, 1);

        void Click(int index, RawInputModifiers modifiers = RawInputModifiers.None,
            MouseButton button = MouseButton.Left) => ClickPhoto(window, photos[index], modifiers, button);

        void Check(int ring, params int[] selected)
        {
            Assert.Same(photos[ring], vm.SelectedImage);
            Assert.Equal(selected.Select(index => photos[index]), vm.Browse.GetSelectedImages());
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CtrlSpace_ReanchorsInGridAndLoupe_AndDevelopOpensTheRing(bool loupe)
    {
        using var fixture = new CatalogVmFixture("browse-source-keys");
        using var catalog = await fixture.CreateCatalogAsync();
        await using var vm = CreateVm(fixture, catalog);
        var photos = Photos(fixture);
        vm.Browse.SetImages(photos);
        var window = new MainWindow { Width = 1400, Height = 800 };
        using var scope = TestUiScope.ForMainWindow(window, vm);
        ClickPhoto(window, photos[0]);
        ClickPhoto(window, photos[2], RawInputModifiers.Control);
        ClickPhoto(window, photos[3], RawInputModifiers.Control);

        if (loupe)
        {
            vm.EnterLoupeCommand.Execute(null);
            Assert.True(vm.IsLoupeMode);
        }

        // The earliest remaining photo differs from the nearest one to C.
        ClickPhoto(window, photos[2], RawInputModifiers.Alt);
        window.FindControl<BrowseGridView>("BrowseGridView")!.Focus();
        Press(window, Key.Space, RawInputModifiers.Control);
        Assert.Same(photos[0], vm.SelectedImage);
        Assert.Equal([photos[0], photos[3]], vm.Browse.GetSelectedImages());
        ClickPhoto(window, photos[2], RawInputModifiers.Control);
        Press(window, Key.Space, RawInputModifiers.Control);
        Assert.Same(photos[2], vm.SelectedImage);
        Assert.Equal(photos[2..4], vm.Browse.GetSelectedImages());
        Press(window, Key.Right);
        Assert.Same(photos[3], vm.SelectedImage);
        Assert.Equal(loupe ? photos[2..4] : [photos[3]], vm.Browse.GetSelectedImages());
        Press(window, Key.D);
        Assert.True(vm.IsDevelopMode);
        Assert.Same(photos[3], vm.SelectedImage);
    }

    [AvaloniaTheory]
    [InlineData(false, false, false, 0)]
    [InlineData(false, true, false, 2)]
    [InlineData(false, false, true, 1)]
    [InlineData(false, true, true, 2)]
    [InlineData(true, false, false, 0)]
    [InlineData(true, true, false, 2)]
    [InlineData(true, false, true, 1)]
    [InlineData(true, true, true, 2)]
    public async Task SyncHeader_NamesTheRing(bool loupe, bool repick, bool removeFirst, int source)
    {
        using var fixture = new CatalogVmFixture("browse-source-sync");
        using var catalog = await fixture.CreateCatalogAsync();
        await using var vm = CreateVm(fixture, catalog);
        var photos = Photos(fixture);
        vm.Browse.SetImages(photos);
        var window = new MainWindow { Width = 1400, Height = 800 };
        using var scope = TestUiScope.ForMainWindow(window, vm);
        ClickPhoto(window, photos[0]);

        if (loupe)
        {
            vm.EnterLoupeCommand.Execute(null);
        }

        foreach (var photo in photos.Skip(1))
        {
            ClickPhoto(window, photo, RawInputModifiers.Control);
        }

        if (repick)
        {
            ClickPhoto(window, photos[2], RawInputModifiers.Alt);
        }

        if (removeFirst)
        {
            ClickPhoto(window, photos[0], RawInputModifiers.Control);
        }

        Assert.Same(photos[source], vm.SelectedImage);
        Assert.Equal(removeFirst ? photos[1..] : photos, vm.Browse.GetSelectedImages());
        var shown = false;
        vm.ShowPasteSettingsAsync = model =>
        {
            shown = true;
            Assert.Equal(PasteSettingsMode.Sync, model.Mode);
            var dialog = new PasteSettingsDialog(model);
            using var dialogScope = new TestUiScope(dialog);
            Assert.Equal($"From {photos[source].FileName} to {(removeFirst ? 2 : 3)} photos", model.Summary);
            Assert.Contains(dialog.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == model.Summary);

            return Task.FromResult(false);
        };
        await vm.SyncSettingsCommand.ExecuteAsync(null);
        Assert.True(shown);
    }

    [AvaloniaFact]
    public async Task EnteringLoupe_KeepsTheFirstSelectedSource()
    {
        // Review C-2: the source is C, which is not the earliest selected photo in grid order.
        using var fixture = new CatalogVmFixture("browse-source-loupe");
        using var catalog = await fixture.CreateCatalogAsync();
        await using var vm = CreateVm(fixture, catalog);
        var photos = Photos(fixture);
        vm.Browse.SetImages(photos);
        var window = new MainWindow { Width = 1400, Height = 800 };
        using var scope = TestUiScope.ForMainWindow(window, vm);

        ClickPhoto(window, photos[2]);
        ClickPhoto(window, photos[0], RawInputModifiers.Control);
        ClickPhoto(window, photos[3], RawInputModifiers.Control);
        Assert.Same(photos[2], vm.SelectedImage);

        vm.EnterLoupeCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();

        Assert.True(vm.IsLoupeMode);
        Assert.Same(photos[2], vm.SelectedImage);
        Assert.Equal([photos[0], photos[2], photos[3]], vm.Browse.GetSelectedImages());
    }

    private static MainWindowViewModel CreateVm(CatalogVmFixture fixture, CatalogService catalog)
    {
        var vm = fixture.CreateViewModel(catalog, new NullBaseLoader(), _ => Task.CompletedTask,
            new TestSourceAvailabilityService(SourceAvailability.AvailableLocally),
            timeProvider: new TestTimeProvider());
        vm.ShowWorkspaceReady(MainWindowViewModel.CurrentFirstRunExperienceVersion);
        vm.ImageService.Previews.AdjacentWarmEnabled = false;

        return vm;
    }

    private static ImageFile[] Photos(CatalogVmFixture fixture) =>
        Enumerable.Range(0, 4).Select(index => new ImageFile(fixture.Path($"{(char)('A' + index)}.jpg"))
        {
            MetadataLoaded = true
        }).ToArray();

    private static void ClickPhoto(Window window, ImageFile photo,
        RawInputModifiers modifiers = RawInputModifiers.None, MouseButton button = MouseButton.Left)
    {
        Dispatcher.UIThread.RunJobs();
        var tile = window.GetVisualDescendants().OfType<Border>()
            .Single(border => border.Name == "ThumbnailTile" && ReferenceEquals(border.DataContext, photo));

        if (window.DataContext is MainWindowViewModel { IsLoupeMode: true })
        {
            // Loupe covers the grid: dispatch to its retained tile to exercise
            // the shared pointer handler with the real Loupe workspace state.
            var keys = modifiers.HasFlag(RawInputModifiers.Control) ? KeyModifiers.Control :
                modifiers.HasFlag(RawInputModifiers.Shift) ? KeyModifiers.Shift :
                modifiers.HasFlag(RawInputModifiers.Alt) ? KeyModifiers.Alt : KeyModifiers.None;
            var right = button == MouseButton.Right;
            tile.RaiseEvent(new PointerPressedEventArgs(tile, new Pointer(1, PointerType.Mouse, true),
                tile, new Point(30, 30), 1,
                new PointerPointProperties(right ? RawInputModifiers.RightMouseButton : RawInputModifiers.LeftMouseButton,
                    right ? PointerUpdateKind.RightButtonPressed : PointerUpdateKind.LeftButtonPressed), keys, 1));
            tile.ContextMenu?.Close();
            Dispatcher.UIThread.RunJobs();

            return;
        }

        // Distinct click positions keep successive modifier gestures from
        // becoming a double-tap that opens Develop.
        var x = modifiers.HasFlag(RawInputModifiers.Alt) ? 70 : 30;
        var y = modifiers == RawInputModifiers.None ? 70 : 30;
        var point = tile.TranslatePoint(new Point(x, y), window)!.Value;
        window.MouseDown(point, button, modifiers);
        window.MouseUp(point, button, modifiers);
        tile.ContextMenu?.Close();
        Dispatcher.UIThread.RunJobs();
    }

    private static void Press(Window window, Key key, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        window.KeyPress(key, modifiers, PhysicalKey.None, null);
        window.KeyRelease(key, modifiers, PhysicalKey.None, null);
        Dispatcher.UIThread.RunJobs();
    }
}
