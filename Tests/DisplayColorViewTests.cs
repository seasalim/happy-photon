using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using HappyPhoton.Views;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class DisplayColorViewTests
{
    private static readonly string FixtureDirectory = Path.Combine(
        GoldenTestPaths.AssetDirectory, "softproof");
    private readonly ITestOutputHelper _output;

    public DisplayColorViewTests(ITestOutputHelper output) => _output = output;

    [AvaloniaFact]
    public void IdentitySurface_ShowsCanonicalWithoutAllocatingCopy()
    {
        using var canonical = CreateBitmap(7);
        var image = new DisplayImage
        {
            CanonicalSource = canonical,
            DisplayTransform = DisplayTransformSnapshot.None,
        };
        var window = Show(image);

        Assert.Same(canonical, image.DisplayedBitmap);
        Assert.Null(image.DisplayCopy);
        window.Close();
    }

    [AvaloniaFact]
    public void ProfileChange_RederivesSurfaceExactlyOnce()
    {
        using var canonical = CreateBitmap(11);
        var image = new DisplayImage
        {
            CanonicalSource = canonical,
            DisplayTransform = Resolve("monitor-a"),
        };
        var window = Show(image);
        var previousCopy = Assert.IsAssignableFrom<Avalonia.Media.Imaging.Bitmap>(
            image.DisplayCopy);
        var before = image.DerivationCount;

        image.DisplayTransform = Resolve("monitor-b");

        Assert.Equal(before + 1, image.DerivationCount);
        Assert.NotSame(previousCopy, image.DisplayCopy);
        Assert.Throws<ObjectDisposedException>(() => _ = previousCopy.PixelSize);
        window.Close();
    }

    [AvaloniaFact]
    public void RapidCanonicalAndTransformSwaps_KeepOnlyFinalCopyAlive()
    {
        var canonicals = new List<Avalonia.Media.Imaging.Bitmap>();
        var retiredCopies = new List<Avalonia.Media.Imaging.Bitmap>();
        var image = new DisplayImage { DisplayTransform = Resolve("monitor-0") };
        var window = Show(image);
        try
        {
            for (var index = 0; index < 50; index++)
            {
                var canonical = CreateBitmap((byte)index);
                canonicals.Add(canonical);
                if (image.DisplayCopy != null) retiredCopies.Add(image.DisplayCopy);
                image.CanonicalSource = canonical;
                if (index % 10 == 9)
                {
                    retiredCopies.Add(Assert.IsAssignableFrom<Avalonia.Media.Imaging.Bitmap>(
                        image.DisplayCopy));
                    image.DisplayTransform = Resolve($"monitor-{index / 10 + 1}");
                }
            }

            var lastCanonical = canonicals[^1];
            var finalCopy = Assert.IsAssignableFrom<Avalonia.Media.Imaging.Bitmap>(
                image.DisplayCopy);
            using var expected = image.DisplayTransform.Derive(
                lastCanonical, DisplaySourceColorSpace.Srgb);

            Assert.Equal(
                BitmapConversionService.CopyBgraPixels(expected),
                BitmapConversionService.CopyBgraPixels(finalCopy));
            Assert.Equal(CreateExpectedPixels(49),
                BitmapConversionService.CopyBgraPixels(lastCanonical));
            Assert.All(retiredCopies.Distinct<Avalonia.Media.Imaging.Bitmap>(
                ReferenceEqualityComparer.Instance), copy =>
                Assert.Throws<ObjectDisposedException>(() => _ = copy.PixelSize));
        }
        finally
        {
            image.CanonicalSource = null;
            window.Close();
            foreach (var canonical in canonicals) canonical.Dispose();
        }
    }

    [AvaloniaFact]
    public async Task ViewModelReplacement_RetiresCanonicalAfterBoundCopyIsDerived()
    {
        using var catalog = new CatalogService(Path.Combine(
            Path.GetTempPath(), $"happy-photon-display-retirement-{Guid.NewGuid():N}"));
        await using var viewModel = new MainWindowViewModel(catalog);
        var image = new DisplayImage
        {
            DataContext = viewModel,
            DisplayTransform = Resolve("monitor"),
        };
        image.Bind(
            DisplayImage.CanonicalSourceProperty,
            new Binding(nameof(MainWindowViewModel.PreviewImage)));
        var window = Show(image);
        var first = CreateBitmap(31);
        var second = CreateBitmap(47);

        viewModel.ReplacePreviewImage(first, PreviewPaintSource.FreshRender);
        viewModel.ReplacePreviewImage(second, PreviewPaintSource.FreshRender);
        var copy = Assert.IsAssignableFrom<Avalonia.Media.Imaging.Bitmap>(image.DisplayCopy);
        var expected = BitmapConversionService.CopyBgraPixels(copy);

        Dispatcher.UIThread.RunJobs();

        Assert.Throws<ObjectDisposedException>(() => _ = first.PixelSize);
        Assert.Equal(expected, BitmapConversionService.CopyBgraPixels(copy));
        Assert.Same(second, image.CanonicalSource);
        viewModel.ClearPreviewImage();
        Dispatcher.UIThread.RunJobs();
        window.Close();
    }

    [AvaloniaFact]
    public async Task ViewModelProfileChange_UpdatesEachPaneOnceWithoutComposingImageServices()
    {
        using var catalog = new CatalogService(Path.Combine(
            Path.GetTempPath(), $"happy-photon-display-color-{Guid.NewGuid():N}"));
        var fake = new FakePlatform(new(
            "monitor-a", Profile("softproof-p3-gamma22.icc"), DisplayAcmState.Off));
        await using var viewModel = new MainWindowViewModel(
            catalog,
            baseLoader: null,
            displayColorManagementService: new DisplayColorManagementService(fake));
        var first = new ComparePaneViewModel(new("first.jpg"));
        var second = new ComparePaneViewModel(new("second.jpg"));
        viewModel.ComparePanes.Add(first);
        viewModel.ComparePanes.Add(second);
        var notifications = 0;
        first.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(ComparePaneViewModel.DisplayTransform))
                notifications++;
        };

        Assert.True(viewModel.ResolveDisplayProfile(1));
        Assert.Same(viewModel.DisplayTransform, first.DisplayTransform);
        Assert.Same(viewModel.DisplayTransform, second.DisplayTransform);
        Assert.Equal(1, notifications);
        Assert.False(viewModel.ResolveDisplayProfile(1));
        Assert.Equal(1, notifications);

        fake.Result = new(
            "monitor-b", Profile("softproof-p3-gamma22.icc"), DisplayAcmState.Off);
        Assert.True(viewModel.ResolveDisplayProfile(1));
        Assert.Equal(2, notifications);
    }

    [AvaloniaFact]
    public async Task ComposedWindow_HoldsCopiesOnlyForVisiblePreviewSurfaces()
    {
        using var catalog = new CatalogService(Path.Combine(
            Path.GetTempPath(), $"happy-photon-visible-color-{Guid.NewGuid():N}"));
        var fake = new FakePlatform(new(
            "monitor-a", Profile("softproof-p3-gamma22.icc"), DisplayAcmState.Off));
        await using var viewModel = new MainWindowViewModel(
            catalog,
            baseLoader: null,
            displayColorManagementService: new DisplayColorManagementService(fake));
        viewModel.WorkspaceMode = Models.WorkspaceMode.Develop;
        Assert.True(viewModel.ResolveDisplayProfile(1));
        var renderCount = 0;
        viewModel.ImageService.Previews.RenderStarted += () => renderCount++;
        var window = new MainWindow
        {
            Width = 1000,
            Height = 700,
            DataContext = viewModel,
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var developPane = window.FindControl<DevelopViewerPane>("DevelopViewerPane")!;
        var develop = DisplaySurface(developPane.Viewer);
        var before = DisplaySurface(
            developPane.FindControl<ZoomPanControl>("BeforeZoomPanControl")!);
        var export = window.FindControl<ExportPreviewPane>("ExportPreviewPane")!
            .FindControl<DisplayImage>("ExportPreviewImage")!;
        var fullScreen = DisplaySurface(
            window.FindControl<ZoomPanControl>("FullScreenZoomPanControl")!);
        var canonical = CreateBitmap(59);
        var beforeCanonical = CreateBitmap(71);
        try
        {
            viewModel.ReplacePreviewImage(canonical, PreviewPaintSource.FreshRender);
            Dispatcher.UIThread.RunJobs();
            AssertCopyCounts("Develop publish", develop, before, export, fullScreen,
                1, 0, 0, 0);

            viewModel.WorkspaceMode = Models.WorkspaceMode.Export;
            Dispatcher.UIThread.RunJobs();
            AssertCopyCounts("Export mode", develop, before, export, fullScreen,
                0, 0, 1, 0);

            viewModel.WorkspaceMode = Models.WorkspaceMode.Develop;
            viewModel.IsFullScreenMode = true;
            Dispatcher.UIThread.RunJobs();
            AssertCopyCounts("Fullscreen", develop, before, export, fullScreen,
                0, 0, 0, 1);

            viewModel.IsFullScreenMode = false;
            viewModel.BeforeAfterPreviewImage = beforeCanonical;
            viewModel.IsBeforeAfterSplit = true;
            Dispatcher.UIThread.RunJobs();
            AssertCopyCounts("Before/After split", develop, before, export, fullScreen,
                1, 1, 0, 0);

            var derivations = new[]
            {
                develop.DerivationCount,
                before.DerivationCount,
                export.DerivationCount,
                fullScreen.DerivationCount,
            };
            var cacheWrites = viewModel.ImageService.Previews.PendingCacheWrites;
            fake.Result = new(
                "monitor-b", Profile("softproof-p3-gamma22.icc"), DisplayAcmState.Off);

            Assert.True(viewModel.ResolveDisplayProfile(1));
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(derivations[0] + 1, develop.DerivationCount);
            Assert.Equal(derivations[1] + 1, before.DerivationCount);
            Assert.Equal(derivations[2], export.DerivationCount);
            Assert.Equal(derivations[3], fullScreen.DerivationCount);
            AssertCopyCounts("Monitor change", develop, before, export, fullScreen,
                1, 1, 0, 0);
            Assert.Equal(0, renderCount);
            Assert.Equal(0, viewModel.ImageService.Previews.PreviewActivityCount);
            Assert.Equal(cacheWrites, viewModel.ImageService.Previews.PendingCacheWrites);
        }
        finally
        {
            viewModel.IsBeforeAfterSplit = false;
            viewModel.BeforeAfterPreviewImage = null;
            beforeCanonical.Dispose();
            viewModel.ClearPreviewImage();
            Dispatcher.UIThread.RunJobs();
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task ExportProof_UsesArmedSourceSpaceWithoutChangingCanonicalPixels()
    {
        using var catalog = new CatalogService(Path.Combine(
            Path.GetTempPath(), $"happy-photon-proof-color-{Guid.NewGuid():N}"));
        var fake = new FakePlatform(new(
            "monitor", Profile("softproof-p3-gamma22.icc"), DisplayAcmState.Off));
        await using var viewModel = new MainWindowViewModel(
            catalog,
            baseLoader: null,
            displayColorManagementService: new DisplayColorManagementService(fake));
        Assert.True(viewModel.ResolveDisplayProfile(1));
        var image = new DisplayImage { DataContext = viewModel };
        image.Bind(
            DisplayImage.CanonicalSourceProperty,
            new Binding(nameof(MainWindowViewModel.PreviewImage)));
        image.Bind(
            DisplayImage.DisplayTransformProperty,
            new Binding(nameof(MainWindowViewModel.DisplayTransform)));
        image.Bind(
            DisplayImage.DisplaySourceColorSpaceProperty,
            new Binding(nameof(MainWindowViewModel.PreviewDisplayColorSpace)));
        var window = new Window { Content = image };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var previous = CreateBitmap(17);
        viewModel.ReplacePreviewImage(previous, PreviewPaintSource.FreshRender);
        Dispatcher.UIThread.RunJobs();
        var derivationsBeforeProof = image.DerivationCount;
        var canonical = CreateBitmap(23);
        var expected = BitmapConversionService.CopyBgraPixels(canonical);
        viewModel.ExportSettings.OutputColorSpace = Models.OutputColorSpace.DisplayP3;

        viewModel.ReplacePreviewImage(
            canonical, PreviewPaintSource.FreshRender, isProof: true);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(DisplaySourceColorSpace.DisplayP3,
            viewModel.PreviewDisplayColorSpace);
        Assert.Equal(derivationsBeforeProof + 1, image.DerivationCount);
        Assert.Equal(expected, BitmapConversionService.CopyBgraPixels(canonical));
        viewModel.ClearPreviewImage();
        Dispatcher.UIThread.RunJobs();
        window.Close();
    }

    [AvaloniaFact]
    public async Task AboutLine_NamesUnsupportedProfileAndReason()
    {
        using var catalog = new CatalogService(Path.Combine(
            Path.GetTempPath(), $"happy-photon-about-color-{Guid.NewGuid():N}"));
        var fake = new FakePlatform(new(
            "monitor", Profile("softproof-p3-mhc2.icc"), DisplayAcmState.Off));
        await using var viewModel = new MainWindowViewModel(
            catalog,
            baseLoader: null,
            displayColorManagementService: new DisplayColorManagementService(fake));
        viewModel.ResolveDisplayProfile(1);
        var dialog = new HelpAboutDialog(viewModel);

        var text = dialog.FindControl<TextBlock>("DisplayProfileStatusText")!.Text;
        Assert.Contains("MHC2", text);
        Assert.Contains("HDR (MHC2)", text);
        dialog.Close();
    }

    private static DisplayTransformSnapshot Resolve(string monitor) =>
        new DisplayColorManagementService(new FakePlatform(new(
            monitor, Profile("softproof-p3-gamma22.icc"), DisplayAcmState.Off)))
            .Resolve(1);

    private static string Profile(string name) => Path.Combine(FixtureDirectory, name);

    private static Avalonia.Media.Imaging.Bitmap CreateBitmap(byte seed) =>
        BitmapConversionService.ConvertToBitmap(CreateExpectedPixels(seed), 8, 8);

    private static DisplayImage DisplaySurface(ZoomPanControl viewer) =>
        viewer.FindControl<DisplayImage>("ImageControl")!;

    private static Window Show(DisplayImage image)
    {
        var window = new Window { Content = image };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    private void AssertCopyCounts(
        string state,
        DisplayImage develop,
        DisplayImage before,
        DisplayImage export,
        DisplayImage fullScreen,
        int expectedDevelop,
        int expectedBefore,
        int expectedExport,
        int expectedFullScreen)
    {
        var actual = new[]
        {
            develop.DisplayCopy == null ? 0 : 1,
            before.DisplayCopy == null ? 0 : 1,
            export.DisplayCopy == null ? 0 : 1,
            fullScreen.DisplayCopy == null ? 0 : 1,
        };
        _output.WriteLine(
            $"{state}: Develop={actual[0]}, Before={actual[1]}, " +
            $"Export={actual[2]}, Fullscreen={actual[3]}");
        var expected = new[]
        {
            expectedDevelop, expectedBefore, expectedExport, expectedFullScreen,
        };
        Assert.Equal(expected, actual);
        var surfaces = new[] { develop, before, export, fullScreen };
        for (var index = 0; index < surfaces.Length; index++)
        {
            if (expected[index] == 0) Assert.Null(surfaces[index].DisplayedBitmap);
            else Assert.Same(surfaces[index].DisplayCopy, surfaces[index].DisplayedBitmap);
        }
    }

    private static byte[] CreateExpectedPixels(byte seed)
    {
        var pixels = new byte[8 * 8 * 4];
        for (var offset = 0; offset < pixels.Length; offset += 4)
        {
            pixels[offset] = (byte)(seed + offset);
            pixels[offset + 1] = (byte)(seed * 3 + offset);
            pixels[offset + 2] = (byte)(seed * 5 + offset);
            pixels[offset + 3] = 255;
        }
        return pixels;
    }

    private sealed class FakePlatform(DisplayPlatformResult result) : IDisplayProfilePlatform
    {
        public DisplayPlatformResult Result { get; set; } = result;
        public DisplayPlatformResult Resolve(nint windowHandle) => Result;
    }
}
