using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class LoupeViewHeadlessTests
{
    [AvaloniaFact]
    public async Task WindowBindingsUseTheLightroomLettersAndLeaveOldKeysUnbound()
    {
        await using var fixture = await Fixture.CreateAsync(1);
        var gestures = fixture.Window.KeyBindings
            .Select(binding => binding.Gesture.ToString())
            .ToArray();

        Assert.Contains("Ctrl+Shift+E", gestures);
        Assert.Contains("Shift+R", gestures);
        Assert.Contains("L", gestures);
        Assert.Contains("C", gestures);
        Assert.Contains("E", gestures);
        Assert.Contains("Z", gestures);
        Assert.Contains(gestures, gesture => gesture is "OemTilde" or "Oem3");
        Assert.DoesNotContain("Ctrl+E", gestures);
        Assert.DoesNotContain("Ctrl+B", gestures);
        Assert.DoesNotContain("B", gestures);

        fixture.Window.FindControl<BrowseGridView>("BrowseGridView")!.Focus();
        Press(fixture.Window, Key.Space, RawInputModifiers.Control);
        Drain();
        Assert.True(fixture.ViewModel.SelectedImage!.IsSelected);

        Press(fixture.Window, Key.E);
        Drain();
        Press(fixture.Window, Key.C);
        Drain();
        Assert.True(fixture.ViewModel.IsLoupeMode);
        Assert.False(fixture.ViewModel.IsCompareMode);

        Press(fixture.Window, Key.G);
        Press(fixture.Window, Key.D);
        Press(fixture.Window, Key.E);
        Drain();
        Assert.True(fixture.ViewModel.IsDevelopMode);
        Press(fixture.Window, Key.R);
        Drain();
        Assert.True(fixture.ViewModel.IsCropMode);
        Press(fixture.Window, Key.Escape);
        Press(fixture.Window, Key.L);
        Drain();
        Assert.True(fixture.ViewModel.IsColorAssessmentMode);
    }

    [AvaloniaTheory]
    [InlineData(Key.E)]
    [InlineData(Key.Enter)]
    [InlineData(Key.Space)]
    public async Task BrowseEntryAndEscape_KeepChromeActiveImageAndFocus(Key key)
    {
        await using var fixture = await Fixture.CreateAsync(2);
        var active = fixture.ViewModel.SelectedImage;
        var browse = fixture.Window.FindControl<BrowseGridView>("BrowseGridView")!;
        Assert.True(browse.Focus());

        Press(fixture.Window, key);
        Drain();
        Drain();

        Assert.True(fixture.ViewModel.IsLoupeMode);
        Assert.Same(active, fixture.ViewModel.SelectedImage);
        Assert.True(fixture.Window.FindControl<FolderTreePanel>("FolderTreePanel")!
            .IsEffectivelyVisible);
        Assert.True(fixture.Window.FindControl<BrowseReviewPane>("BrowseReviewPane")!
            .IsEffectivelyVisible);
        Assert.True(browse.FindControl<LoupeView>("LoupeView")!
            .IsKeyboardFocusWithin);

        Press(fixture.Window, Key.Escape);
        Drain();
        Drain();

        Assert.False(fixture.ViewModel.IsLoupeMode);
        Assert.True(fixture.ViewModel.IsBrowseGridVisible);
        Assert.Same(active, fixture.ViewModel.SelectedImage);
        Assert.True(browse.IsKeyboardFocusWithin);
    }

    [AvaloniaFact]
    public async Task GReturnsToGridAndSelectionScopedArrowsCanEnterCompare()
    {
        await using var fixture = await Fixture.CreateAsync(3);
        var vm = fixture.ViewModel;
        var images = vm.Browse.VisibleImages.ToArray();
        vm.ToggleImageSelection(images[0]);
        vm.ToggleImageSelection(images[1]);
        fixture.Window.FindControl<BrowseGridView>("BrowseGridView")!.Focus();

        Press(fixture.Window, Key.E);
        Drain();
        Press(fixture.Window, Key.Right);
        Drain();

        Assert.Same(images[1], vm.SelectedImage);
        Assert.Equal(images[..2], vm.Browse.GetSelectedImages());

        Press(fixture.Window, Key.C);
        Drain();
        Assert.False(vm.IsLoupeMode);
        Assert.True(vm.IsCompareMode);
        Assert.Equal(images[..2], vm.Browse.GetSelectedImages());

        Press(fixture.Window, Key.Escape);
        Drain();
        Press(fixture.Window, Key.E);
        Drain();
        Press(fixture.Window, Key.G);
        Drain();
        Drain();

        Assert.False(vm.IsLoupeMode);
        Assert.True(vm.IsBrowseGridVisible);
        Assert.Same(images[0], vm.SelectedImage);

        Press(fixture.Window, Key.E);
        Drain();
        Assert.True(vm.IsLoupeMode);
        Press(fixture.Window, Key.E);
        Drain();
        Drain();

        Assert.False(vm.IsLoupeMode);
        Assert.True(vm.IsBrowseGridVisible);
        Assert.Same(images[0], vm.SelectedImage);
    }

    [AvaloniaFact]
    public async Task RatingFilteredActivePhotoAdvancesAndKeepsLoupeNavigationLive()
    {
        await using var fixture = await Fixture.CreateAsync(4);
        var vm = fixture.ViewModel;
        var images = vm.Browse.VisibleImages.ToArray();
        foreach (var image in images) image.Rating = 2;
        images[1].Rating = 1;
        vm.Browse.MinimumRating = 1;
        vm.SelectedImage = images[1];
        fixture.Window.FindControl<BrowseGridView>("BrowseGridView")!.Focus();

        Press(fixture.Window, Key.E);
        Drain();
        Press(fixture.Window, Key.D1);
        await TestWaits.UntilAsync(() =>
            ReferenceEquals(vm.SelectedImage, images[2]));

        Assert.Equal(0, images[1].Rating);
        Assert.DoesNotContain(images[1], vm.Browse.VisibleImages);
        Assert.True(vm.SelectPreviousImageCommand.CanExecute(null));
        Assert.True(vm.SelectNextImageCommand.CanExecute(null));

        Press(fixture.Window, Key.Left);
        Drain();
        Assert.Same(images[0], vm.SelectedImage);
        Press(fixture.Window, Key.Right);
        Drain();
        Assert.Same(images[2], vm.SelectedImage);
    }

    [AvaloniaFact]
    public async Task EntryAnchorsRestrictedLoupeOnFirstSelectedPhoto()
    {
        await using var fixture = await Fixture.CreateAsync(3);
        var vm = fixture.ViewModel;
        var images = vm.Browse.VisibleImages.ToArray();
        vm.ToggleImageSelection(images[0]);
        vm.ToggleImageSelection(images[1]);
        vm.SelectedImage = images[2];
        fixture.Window.FindControl<BrowseGridView>("BrowseGridView")!.Focus();

        Press(fixture.Window, Key.E);
        Drain();

        Assert.True(vm.IsLoupeMode);
        Assert.Same(images[0], vm.SelectedImage);
        Assert.Equal(images[..2], vm.Browse.GetSelectedImages());

        Press(fixture.Window, Key.Right);
        Drain();
        Assert.Same(images[1], vm.SelectedImage);
    }

    [AvaloniaFact]
    public async Task UnrestrictedArrowsCarrySelectionAndKeepManualZoomAfterEntryFit()
    {
        await using var fixture = await Fixture.CreateAsync(3);
        var vm = fixture.ViewModel;
        var images = vm.Browse.VisibleImages.ToArray();
        vm.ToggleImageSelection(images[0]);
        vm.SwitchToDevelopCommand.Execute(null);
        vm.ApplyManualZoom(1.0);
        vm.SwitchToBrowseCommand.Execute(null);
        fixture.Window.FindControl<BrowseGridView>("BrowseGridView")!.Focus();

        Press(fixture.Window, Key.E);
        Drain();
        Drain();
        Assert.True(vm.IsZoomFitMode);

        Press(fixture.Window, Key.Space);
        Drain();
        Assert.False(vm.IsZoomFitMode);

        Press(fixture.Window, Key.Right);
        Press(fixture.Window, Key.Right);
        Drain();

        Assert.Same(images[2], vm.SelectedImage);
        Assert.Same(images[2], Assert.Single(vm.Browse.GetSelectedImages()));
        Assert.False(vm.IsZoomFitMode);

        DeleteConfirmationRequest? deleteRequest = null;
        vm.ConfirmDeleteAsync = request =>
        {
            deleteRequest = request;
            return Task.FromResult(false);
        };
        Press(fixture.Window, Key.Delete);
        await TestWaits.UntilAsync(() => deleteRequest != null);
        Assert.Same(images[2], Assert.Single(deleteRequest!.Primaries));

        Press(fixture.Window, Key.Escape);
        Drain();
        Drain();

        Assert.False(vm.IsLoupeMode);
        Assert.Same(images[2], vm.SelectedImage);
        Assert.Same(images[2], Assert.Single(vm.Browse.GetSelectedImages()));
    }

    [AvaloniaFact]
    public async Task LoupeRoutesAssessmentZoomFullscreenDevelopAndCropNoOp()
    {
        await using var fixture = await Fixture.CreateAsync(2);
        var vm = fixture.ViewModel;
        var active = vm.SelectedImage!;
        var peer = vm.Browse.VisibleImages[1];
        vm.ToggleImageSelection(active);
        vm.ToggleImageSelection(peer);
        fixture.Window.FindControl<BrowseGridView>("BrowseGridView")!.Focus();
        Press(fixture.Window, Key.E);
        Drain();

        Press(fixture.Window, Key.P);
        await TestWaits.UntilAsync(() => active.Flag == ImageFlag.Picked);
        Assert.Equal(ImageFlag.Unflagged, peer.Flag);
        Assert.Equal("Set flag: Picked", vm.AssessmentFeedback);

        Press(fixture.Window, Key.P);
        await TestWaits.UntilAsync(() => active.Flag == ImageFlag.Picked);
        Press(fixture.Window, Key.X);
        await TestWaits.UntilAsync(() => active.Flag == ImageFlag.Rejected);
        Assert.Equal(ImageFlag.Unflagged, peer.Flag);
        Press(fixture.Window, Key.U);
        await TestWaits.UntilAsync(() => active.Flag == ImageFlag.Unflagged);

        var ratingKeys = new[] { Key.D1, Key.D2, Key.D3, Key.D4, Key.D5 };
        for (var index = 0; index < ratingKeys.Length; index++)
        {
            Press(fixture.Window, ratingKeys[index]);
            var rating = index + 1;
            await TestWaits.UntilAsync(() => active.Rating == rating);
            Assert.Equal(0, peer.Rating);
        }
        Press(fixture.Window, Key.D5);
        await TestWaits.UntilAsync(() => active.Rating == 0);
        Press(fixture.Window, Key.D0);

        var labelKeys = new[] { Key.D6, Key.D7, Key.D8, Key.D9 };
        var labels = new[]
        {
            ColorLabel.Red,
            ColorLabel.Yellow,
            ColorLabel.Green,
            ColorLabel.Blue
        };
        for (var index = 0; index < labelKeys.Length; index++)
        {
            Press(fixture.Window, labelKeys[index]);
            var label = labels[index];
            await TestWaits.UntilAsync(() => active.ColorLabel == label);
            Assert.Equal(ColorLabel.None, peer.ColorLabel);
        }

        Press(fixture.Window, Key.R);
        Drain();
        Assert.False(vm.IsCropMode);
        Assert.True(vm.IsLoupeMode);

        Press(fixture.Window, Key.Space);
        Drain();
        Assert.False(vm.IsZoomFitMode);
        Press(fixture.Window, Key.Z);
        Drain();
        Assert.True(vm.IsZoomFitMode);

        Press(fixture.Window, Key.F);
        Drain();
        Assert.True(vm.IsFullScreenMode);
        Assert.False(vm.IsLoupeMode);
        Press(fixture.Window, Key.F);
        Drain();
        Press(fixture.Window, Key.E);
        Drain();
        Press(fixture.Window, Key.D);
        Drain();

        Assert.True(vm.IsDevelopMode);
        Assert.False(vm.IsLoupeMode);
    }

    private static void Press(
        MainWindow window,
        Key key,
        RawInputModifiers modifiers = RawInputModifiers.None)
    {
        var physical = key switch
        {
            Key.E => PhysicalKey.E,
            Key.G => PhysicalKey.G,
            Key.D => PhysicalKey.D,
            Key.C => PhysicalKey.C,
            Key.F => PhysicalKey.F,
            Key.P => PhysicalKey.P,
            Key.X => PhysicalKey.X,
            Key.U => PhysicalKey.U,
            Key.L => PhysicalKey.L,
            Key.R => PhysicalKey.R,
            Key.Z => PhysicalKey.Z,
            Key.D0 => PhysicalKey.Digit0,
            Key.D1 => PhysicalKey.Digit1,
            Key.D2 => PhysicalKey.Digit2,
            Key.D3 => PhysicalKey.Digit3,
            Key.D4 => PhysicalKey.Digit4,
            Key.D5 => PhysicalKey.Digit5,
            Key.D6 => PhysicalKey.Digit6,
            Key.D7 => PhysicalKey.Digit7,
            Key.D8 => PhysicalKey.Digit8,
            Key.D9 => PhysicalKey.Digit9,
            Key.Left => PhysicalKey.ArrowLeft,
            Key.Right => PhysicalKey.ArrowRight,
            Key.Space => PhysicalKey.Space,
            Key.Enter => PhysicalKey.Enter,
            Key.Escape => PhysicalKey.Escape,
            Key.Delete => PhysicalKey.Delete,
            _ => PhysicalKey.None
        };
        var text = key is >= Key.A and <= Key.Z
            ? key.ToString().ToLowerInvariant()
            : key == Key.Space ? " " : null;
        window.KeyPress(key, modifiers, physical, text);
        window.KeyRelease(key, modifiers, physical, text);
    }

    private static void Drain() => Dispatcher.UIThread.RunJobs();

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly TemporaryDirectory _root;
        private readonly CatalogService _catalog;

        public MainWindowViewModel ViewModel { get; }
        public MainWindow Window { get; }

        private Fixture(
            TemporaryDirectory root,
            CatalogService catalog,
            MainWindowViewModel viewModel,
            MainWindow window)
        {
            _root = root;
            _catalog = catalog;
            ViewModel = viewModel;
            Window = window;
        }

        public static async Task<Fixture> CreateAsync(int count)
        {
            var root = new TemporaryDirectory();
            var catalog = new CatalogService(Path.Combine(root.Path, "catalog"));
            await catalog.InitializeAsync();
            var images = Enumerable.Range(0, count)
                .Select(index => new ImageFile(root.Path + $"\\{index}.jpg"))
                .ToArray();
            var states = await catalog.LoadOrCreateImageStatesAsync(
                images.Select(image => image.FilePath).ToArray());
            foreach (var image in images)
                image.CatalogId = states[image.FilePath].Single().CatalogId;
            var vm = new MainWindowViewModel(
                catalog,
                new NullBaseLoader(),
                _ => Task.CompletedTask);
            vm.ShowWorkspaceReady(MainWindowViewModel.CurrentFirstRunExperienceVersion);
            vm.Browse.SetImages(images);
            vm.SelectedImage = images[0];
            var window = new MainWindow { DataContext = vm };
            window.Show();
            Drain();
            return new Fixture(root, catalog, vm, window);
        }

        public async ValueTask DisposeAsync()
        {
            Window.DataContext = null;
            Window.Close();
            await ViewModel.DisposeAsync();
            _catalog.Dispose();
            _root.Dispose();
        }
    }
}
