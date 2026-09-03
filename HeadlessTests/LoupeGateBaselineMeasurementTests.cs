using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using HappyPhoton.Views;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class LoupeGateBaselineMeasurementTests(ITestOutputHelper output)
{
    [AvaloniaFact]
    public async Task WarmLoupe_UsesOnlyTheSettingsMatchedCachedPreview()
    {
        await using var fixture = await TraceFixture.CreateAsync(warm: true);

        var loupe = await fixture.EnterLoupeAsync(close: false);
        output.WriteLine($"warm loupe: {loupe}");

        var refinement = await fixture.RefineLoupeAsync();
        output.WriteLine($"warm loupe first 1:1: {refinement}");

        var fullscreen = await fixture.EnterFullscreenAsync();
        output.WriteLine($"warm fullscreen: {fullscreen}");

        var compare = await fixture.EnterCompareAsync();
        output.WriteLine($"warm compare total: {compare}");
        output.WriteLine($"warm compare pane share: {compare.PerPane(2)}");

        Assert.Equal(1, fullscreen.BaseInstalled);
        Assert.Equal(1, fullscreen.FreshPaint);
        // The cached and fresh paths race. A settings-matched cache is present,
        // but only an accepted cached paint is traced; a lookup is not.
        Assert.InRange(fullscreen.CachedPaint, 0, 1);
        Assert.Equal(new TraceCounts(0, 0, 1), loupe);
        Assert.Equal(new TraceCounts(1, 1, 0), refinement);
        Assert.Equal(2, fixture.ViewModel.ComparePanes.Count);
        Assert.All(fixture.ViewModel.ComparePanes, pane => Assert.NotNull(pane.Preview));
    }

    [AvaloniaFact]
    public async Task ColdLoupe_MatchesTheFullscreenDisplayChainShape()
    {
        await using var fixture = await TraceFixture.CreateAsync(warm: false);

        var fullscreen = await fixture.EnterFullscreenAsync();
        output.WriteLine($"cold fullscreen: {fullscreen}");

        var loupe = await fixture.EnterLoupeAsync();
        output.WriteLine($"cold loupe: {loupe}");

        var compare = await fixture.EnterCompareAsync();
        output.WriteLine($"cold compare total: {compare}");
        output.WriteLine($"cold compare pane share: {compare.PerPane(2)}");

        Assert.Equal(1, fullscreen.BaseInstalled);
        Assert.Equal(1, fullscreen.FreshPaint);
        Assert.Equal(0, fullscreen.CachedPaint);
        Assert.Equal(new TraceCounts(1, 1, 0), loupe);
        Assert.Equal(2, fixture.ViewModel.ComparePanes.Count);
        Assert.All(fixture.ViewModel.ComparePanes, pane => Assert.NotNull(pane.Preview));
    }

    [AvaloniaFact]
    public async Task SpaceReachesExportTextAndFocusedBrowseFooterButton()
    {
        using var root = new TemporaryDirectory();
        using var catalog = new CatalogService(Path.Combine(root.Path, "catalog"));
        await catalog.InitializeAsync();
        await using var viewModel = new MainWindowViewModel(
            catalog,
            baseLoader: new NullBaseLoader(),
            loadMetadataAsync: _ => Task.CompletedTask);
        viewModel.ShowWorkspaceReady(
            MainWindowViewModel.CurrentFirstRunExperienceVersion);
        var window = new MainWindow { DataContext = viewModel };

        try
        {
            window.Show();
            Drain();
            viewModel.SwitchToExportCommand.Execute(null);
            Drain();
            var export = window.FindControl<ExportSettingsPane>(
                "ExportSettingsPane")!;
            var naming = export.FindControl<TextBox>(
                "ExportNamingPatternField")!;
            viewModel.ExportSettings.NamingPattern = "base";
            Drain();
            naming.CaretIndex = naming.Text?.Length ?? 0;
            Assert.True(naming.Focus());
            bool? namingSpaceHandled = null;
            naming.AddHandler(
                InputElement.KeyDownEvent,
                (_, args) => namingSpaceHandled = args.Handled,
                RoutingStrategies.Bubble,
                handledEventsToo: true);

            window.KeyPress(
                Key.Space,
                RawInputModifiers.None,
                PhysicalKey.Space,
                " ");
            window.KeyTextInput(" ");
            window.KeyRelease(
                Key.Space,
                RawInputModifiers.None,
                PhysicalKey.Space,
                " ");
            Drain();
            var textResult = viewModel.ExportSettings.NamingPattern;
            output.WriteLine($"export naming after Space: '{textResult}'");

            viewModel.HandleEscapeCommand.Execute(null);
            Drain();
            var browse = window.FindControl<BrowseGridView>("BrowseGridView")!;
            var pairs = browse.FindControl<Button>("PairsButton")!;
            Assert.False(browse.ShowPairs);
            Assert.True(pairs.Focus());
            bool? pairsSpaceHandled = null;
            pairs.AddHandler(
                InputElement.KeyDownEvent,
                (_, args) => pairsSpaceHandled = args.Handled,
                RoutingStrategies.Tunnel,
                handledEventsToo: true);
            window.KeyPress(
                Key.Space,
                RawInputModifiers.None,
                PhysicalKey.Space,
                " ");
            window.KeyRelease(
                Key.Space,
                RawInputModifiers.None,
                PhysicalKey.Space,
                " ");
            Drain();
            output.WriteLine($"browse footer activated: {browse.ShowPairs}");

            Assert.Equal("base ", textResult);
            Assert.False(namingSpaceHandled);
            Assert.True(browse.ShowPairs);
            Assert.False(pairsSpaceHandled);
            Assert.False(viewModel.IsLoupeMode);
        }
        finally
        {
            window.DataContext = null;
            window.Close();
        }
    }

    private static void Drain() => Dispatcher.UIThread.RunJobs();

    private sealed class TraceFixture : IAsyncDisposable
    {
        private readonly TemporaryDirectory _root;
        private readonly CatalogService _catalog;
        private readonly List<string> _lines = [];
        private readonly IDisposable _trace;
        private readonly bool _warm;

        public MainWindowViewModel ViewModel { get; }
        private ImageFile[] Images { get; }

        private TraceFixture(
            TemporaryDirectory root,
            CatalogService catalog,
            MainWindowViewModel viewModel,
            ImageFile[] images,
            bool warm)
        {
            _root = root;
            _catalog = catalog;
            ViewModel = viewModel;
            Images = images;
            _warm = warm;
            _trace = ImageServiceHelpers.OverrideDisplayTraceForTesting(
                enabled: true,
                _lines.Add);
        }

        public static async Task<TraceFixture> CreateAsync(bool warm)
        {
            var root = new TemporaryDirectory();
            var catalog = new CatalogService(Path.Combine(root.Path, "catalog"));
            await catalog.InitializeAsync();
            var images = new[]
            {
                new ImageFile(Path.Combine(root.Path, "first.jpg")),
                new ImageFile(Path.Combine(root.Path, "second.jpg"))
            };
            TestImages.WriteJpeg(images[0].FilePath, MagickColors.Orange, 640, 480);
            TestImages.WriteJpeg(images[1].FilePath, MagickColors.Purple, 640, 480);
            var states = await catalog.LoadOrCreateImageStatesAsync(
                images.Select(image => image.FilePath).ToArray());
            foreach (var image in images)
            {
                image.CatalogId = states[image.FilePath].Single().CatalogId;
            }

            if (warm)
            {
                await using var cache = new PreviewCacheService(catalog);
                foreach (var image in images)
                {
                    using var preview = new MagickImage(
                        MagickColors.DarkSlateGray,
                        48,
                        32);
                    cache.QueueSaveToCache(
                        image,
                        preview,
                        RenderSettingsHash.Compute(image.EditSettings),
                        new PreviewCacheIdentity(
                            new PixelSize(640, 480),
                            new PixelSize(640, 480)));
                }
            }

            var viewModel = new MainWindowViewModel(
                catalog,
                baseLoader: null,
                loadMetadataAsync: _ => Task.CompletedTask);
            viewModel.ShowWorkspaceReady(
                MainWindowViewModel.CurrentFirstRunExperienceVersion);
            viewModel.Browse.SetImages(images);
            viewModel.SelectedImage = images[0];
            return new TraceFixture(root, catalog, viewModel, images, warm);
        }

        public async Task<TraceCounts> EnterFullscreenAsync()
        {
            _lines.Clear();
            ViewModel.ToggleFullScreenCommand.Execute(null);
            await TestWaits.UntilAsync(() =>
                ViewModel.PreviewImage != null &&
                ViewModel.ImageService.Previews.PreviewActivityCount == 0);
            await TestWaits.UntilAsync(() =>
                ViewModel.ImageService.Previews.PendingCacheWrites == 0);
            await TestWaits.UntilAsync(() => _lines.Any(line => line.StartsWith(
                "[DisplayChain] paint source=fresh-render ",
                StringComparison.Ordinal)));
            var result = TraceCounts.From(_lines);
            ViewModel.ToggleFullScreenCommand.Execute(null);
            Drain();
            return result;
        }

        public async Task<TraceCounts> EnterCompareAsync()
        {
            if (!_warm) await ClearPreviewAssetsAsync();
            foreach (var image in Images)
            {
                if (!image.IsSelected) ViewModel.ToggleImageSelection(image);
            }
            _lines.Clear();
            ViewModel.EnterCompareCommand.Execute(null);
            await ViewModel.CompareLoadingTask.WaitAsync(TestWaits.Condition);
            return TraceCounts.From(_lines);
        }

        public async Task<TraceCounts> EnterLoupeAsync(bool close = true)
        {
            if (!_warm) await ClearPreviewAssetsAsync();
            _lines.Clear();
            ViewModel.EnterLoupeCommand.Execute(null);
            await ViewModel.LoupeLoadingTask.WaitAsync(TestWaits.Condition);
            var result = TraceCounts.From(_lines);
            Assert.NotNull(ViewModel.LoupePane?.Preview);
            if (close)
            {
                ViewModel.ExitLoupeCommand.Execute(null);
                Drain();
            }
            return result;
        }

        public async Task<TraceCounts> RefineLoupeAsync()
        {
            _lines.Clear();
            ViewModel.ToggleActualSizeCommand.Execute(null);
            ViewModel.PublishLoupeRequiredDeviceLongEdge(640, false);
            await ViewModel.LoupeLoadingTask.WaitAsync(TestWaits.Condition);
            var result = TraceCounts.From(_lines);
            ViewModel.ExitLoupeCommand.Execute(null);
            Drain();
            return result;
        }

        private async Task ClearPreviewAssetsAsync()
        {
            await TestWaits.UntilAsync(() =>
                ViewModel.ImageService.Previews.PendingCacheWrites == 0);
            foreach (var image in Images)
            {
                var path = _catalog.GetPreviewPath(image.CatalogId);
                if (File.Exists(path)) File.Delete(path);
                var metadata = Path.ChangeExtension(path, ".meta");
                if (File.Exists(metadata)) File.Delete(metadata);
            }
            ViewModel.ImageService.Previews.ClearPreviewCache();
        }

        public async ValueTask DisposeAsync()
        {
            _trace.Dispose();
            await ViewModel.DisposeAsync();
            _catalog.Dispose();
            _root.Dispose();
        }
    }

    private sealed record TraceCounts(
        int BaseInstalled,
        int FreshPaint,
        int CachedPaint)
    {
        public static TraceCounts From(IEnumerable<string> lines) => new(
            lines.Count(line => line.StartsWith(
                "[DisplayChain] base installed ",
                StringComparison.Ordinal)),
            lines.Count(line => line.StartsWith(
                "[DisplayChain] paint source=fresh-render ",
                StringComparison.Ordinal)),
            lines.Count(line => line.StartsWith(
                "[DisplayChain] paint source=cached-jpeg ",
                StringComparison.Ordinal)));

        public TraceCounts PerPane(int panes) => new(
            BaseInstalled / panes,
            FreshPaint / panes,
            CachedPaint / panes);

        public override string ToString() =>
            $"base-installed={BaseInstalled}, fresh-paint={FreshPaint}, " +
            $"cached-paint={CachedPaint}";
    }
}
