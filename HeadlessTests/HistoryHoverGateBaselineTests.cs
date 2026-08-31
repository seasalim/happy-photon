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
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

/// <summary>
/// Run 219 gates 3–4 instrument: a real pointer sweep over ten History rows that
/// settles on the last one. Counts catalog history writes (must stay 0) and
/// side-surface render-pipeline entries, and pins the main view's bitmap.
/// Reference on 23ccd38: 0 writes, 0 side-surface entries (hover does nothing).
/// Target after slice 3: 0 writes, exactly 1 side-surface entry.
/// </summary>
public sealed class HistoryHoverGateBaselineTests : IDisposable
{
    private const int ExpectedSideSurfaceEntries = 1;
    private readonly CatalogVmFixture _fixture = new("history-hover-gate");

    [AvaloniaFact]
    public async Task HoverSweepWritesNothingAndEntersThePipelineAsExpected()
    {
        using var catalog = await _fixture.CreateCatalogAsync("hover-gate");
        var clock = new TestTimeProvider();
        var vm = _fixture.CreateViewModel(
            catalog,
            new TinyBaseLoader(),
            loadMetadataAsync: _ => Task.CompletedTask,
            availabilityService: new TestSourceAvailabilityService(
                SourceAvailability.AvailableLocally),
            timeProvider: clock);
        vm.ShowWorkspaceReady(MainWindowViewModel.CurrentFirstRunExperienceVersion);
        vm.IsDevelopMode = true;
        var image = await SeedAsync(catalog, "hover.jpg", 20);
        vm.Browse.SetImages([image]);
        vm.SelectedImage = image;
        await TestWaits.UntilAsync(() => vm.IsHistoryLoaded);
        await TestWaits.UntilAsync(() =>
            vm.PreviewImage != null &&
            vm.ImageService.Previews.PreviewActivityCount == 0);

        var writes = 0;
        catalog.EditHistoryWriteGateAsync = () =>
        {
            writes++;
            return Task.CompletedTask;
        };
        var sideSurfaceEntries = 0;
        vm.ImageService.Previews.SideSurfaceRenderGateAsync = () =>
        {
            sideSurfaceEntries++;
            return Task.CompletedTask;
        };

        var window = new MainWindow { Width = 800, Height = 900, DataContext = vm };
        window.Show();
        Dispatcher.UIThread.RunJobs(); Dispatcher.UIThread.RunJobs();
        try
        {
            var history = window.FindControl<EditHistoryPanel>("EditHistoryPanel")!;
            var hoverEnters = 0;
            var hoverLeaves = 0;
            history.HistoryHoverEnter += (_, _) => hoverEnters++;
            history.HistoryHoverLeave += (_, _) => hoverLeaves++;
            var rows = history.GetVisualDescendants().OfType<Button>()
                .Where(button => button.Classes.Contains("history-row"))
                .Take(10)
                .ToArray();
            Assert.Equal(10, rows.Length);
            var mainView = vm.PreviewImage;
            var histogram = vm.Histogram;
            var waveform = vm.EffectiveWaveform;
            var generation = vm.LatestPreviewOutcomeGeneration;
            var restingPaints = vm.RestingPaintCount;
            var adjacentEntries = vm.ImageService.Previews.AdjacentWarmEntryCount;
            var canUndo = vm.CanUndo;
            var canRedo = vm.CanRedo;
            var savedHash = RenderSettingsHash.Compute(LastSavedState(vm)!);
            var position = vm.HistoryEntries.Single(entry => entry.IsCurrent);
            var publications = 0;
            vm.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(vm.NavigatorHoverImage) &&
                    vm.NavigatorHoverImage != null) publications++;
            };

            EditHistoryEntry? settled = null;
            foreach (var row in rows)
            {
                settled = Assert.IsType<EditHistoryEntry>(row.DataContext);
                row.BringIntoView();
                Dispatcher.UIThread.RunJobs();
                var center = row.TranslatePoint(
                    new Point(row.Bounds.Width / 2, row.Bounds.Height / 2), window)!.Value;
                window.MouseMove(center, RawInputModifiers.None);
                Dispatcher.UIThread.RunJobs();
                // Move faster than any hover debounce; only the last row settles.
                clock.Advance(TimeSpan.FromMilliseconds(20));
            }
            clock.Advance(TimeSpan.FromMilliseconds(500));
            Dispatcher.UIThread.RunJobs();
            await TestWaits.UntilAsync(() => vm.NavigatorHoverImage != null);
            Dispatcher.UIThread.RunJobs();

            Console.WriteLine(
                $"gate rows: enters={hoverEnters} leaves={hoverLeaves} " +
                $"writes={writes} sideSurfaceEntries={sideSurfaceEntries}");
            Assert.Equal(10, hoverEnters);
            Assert.True(hoverLeaves < hoverEnters);
            Assert.Equal(0, writes);
            Assert.Equal(ExpectedSideSurfaceEntries, sideSurfaceEntries);
            Assert.Equal(1, publications);
            var hover = Assert.IsAssignableFrom<Avalonia.Media.Imaging.Bitmap>(
                vm.NavigatorHoverImage);
            Assert.Equal(RenderSettingsHash.Compute(settled!.Settings),
                vm.ImageService.Previews.TryGetPreviewRenderIdentity(hover)!.SettingsHash);
            Assert.Same(mainView, vm.PreviewImage);
            Assert.Same(histogram, vm.Histogram);
            Assert.Same(waveform, vm.EffectiveWaveform);
            Assert.Equal(generation, vm.LatestPreviewOutcomeGeneration);
            Assert.Equal(restingPaints, vm.RestingPaintCount);
            Assert.Equal(adjacentEntries,
                vm.ImageService.Previews.AdjacentWarmEntryCount);
            Assert.Equal(canUndo, vm.CanUndo);
            Assert.Equal(canRedo, vm.CanRedo);
            Assert.Equal(savedHash,
                RenderSettingsHash.Compute(LastSavedState(vm)!));
            Assert.Same(position, vm.HistoryEntries.Single(entry => entry.IsCurrent));
            Assert.Equal(20, vm.HistoryEntries.Count - 1);

            var hoverLayer = window.FindControl<Image>("NavigatorHoverImage")!;
            Assert.True(hoverLayer.IsVisible);
            Assert.Same(hover, hoverLayer.Source);
            Assert.NotSame(mainView, hoverLayer.Source);
            window.MouseMove(new Point(10, 10), RawInputModifiers.None);
            await TestWaits.UntilAsync(() => vm.NavigatorHoverImage == null);
            Dispatcher.UIThread.RunJobs();
            Assert.False(hoverLayer.IsVisible);
            Assert.Throws<ObjectDisposedException>(() => _ = hover.PixelSize);
        }
        finally
        {
            window.DataContext = null;
            window.Close();
            await vm.DisposeAsync();
        }
    }

    private static EditSettings? LastSavedState(MainWindowViewModel vm) =>
        (EditSettings?)typeof(MainWindowViewModel).GetField(
            "_lastSavedState",
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.NonPublic)!.GetValue(vm);

    private async Task<ImageFile> SeedAsync(
        CatalogService catalog,
        string name,
        int steps)
    {
        var current = new EditSettings { Exposure = steps / 100d };
        var image = new ImageFile(_fixture.Path(name)) { EditSettings = current };
        image.CatalogId = await catalog.GetOrCreateImageAsync(image.FilePath);
        var entries = Enumerable.Range(0, steps + 1)
            .Select(index => new CatalogEditHistoryEntry(
                index,
                index == 0 ? "Original" : $"Exposure +{index / 100d:0.00}",
                new EditSettings { Exposure = index / 100d }))
            .ToArray();
        await catalog.SaveEditSettingsWithHistoryAsync(
            image.CatalogId,
            current,
            new CatalogEditHistoryMutation(-1, entries, steps));
        return image;
    }

    public void Dispose() => _fixture.Dispose();

    private sealed class TinyBaseLoader : IBaseImageLoader
    {
        public bool CanLoad(ImageFile file) => true;

        public BaseImage LoadPreviewBase(
            ImageFile file,
            BaseDecodeSettings decode,
            CancellationToken cancellationToken) =>
            Create(decode);

        BaseImageLoadOutcome IBaseImageLoader.LoadPreviewBaseWithOutcome(
            ImageFile file,
            BaseDecodeSettings decode,
            CancellationToken cancellationToken) =>
            BaseImageLoadOutcome.Loaded(Create(decode));

        public BaseImage? LoadFullBase(
            ImageFile file,
            BaseDecodeSettings decode,
            CancellationToken cancellationToken) =>
            Create(decode);

        private static BaseImage Create(BaseDecodeSettings decode) =>
            new(
                new MagickImage(MagickColors.Gray, 16, 12)
                {
                    ColorSpace = ColorSpace.RGB
                },
                new BaseImageInfo(
                    BaseSourceKind.Standard,
                    false,
                    decode,
                    null,
                    null,
                    6504,
                    0,
                    false,
                    null,
                    1,
                    16,
                    12));
    }
}
