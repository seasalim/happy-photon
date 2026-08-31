using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class BrowseGridDensityGateTests
{
    private const int MeasurementRuns = 3;
    private static readonly double[] Widths = [620, 800, 1000, 777];
    private readonly ITestOutputHelper _output;

    public BrowseGridDensityGateTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [AvaloniaTheory]
    [InlineData(620, 3, 3, 0.885245902)]
    [InlineData(800, 4, 4, 0.911392405)]
    [InlineData(1000, 5, 5, 0.909090909)]
    [InlineData(777, 3, 4, 0.704041721)]
    public void MediumTier_ReportsRealizedDensity(
        double viewportWidth,
        int baselineColumns,
        int expectedColumns,
        double baselineImageRowFraction)
    {
        var samples = Enumerable.Range(0, MeasurementRuns)
            .Select(_ => MeasureDensity(viewportWidth))
            .ToArray();
        var median = Median(samples);

        Assert.All(samples, sample => Assert.Equal(median, sample));
        Assert.Equal(viewportWidth, median.GridViewportWidth, 6);
        Assert.Equal(expectedColumns, median.ColumnCount);
        Assert.True(median.ColumnCount >= baselineColumns);
        Assert.Equal(median.MinimumCellWidth, median.MaximumCellWidth, 6);
        Assert.Equal(median.CellWidth, median.LastRowCellWidth, 6);
        Assert.Equal(4, median.MinimumGap, 6);
        Assert.Equal(4, median.MaximumGap, 6);
        Assert.InRange(median.TrailingSlack, 0, median.ColumnCount);
        Assert.True(median.ImageRowFraction > baselineImageRowFraction);
        Assert.InRange(
            median.ImageViewportWidth / median.ImageViewportHeight, 1.49, 1.51);
        Assert.Equal(median.CellWidth - 10, median.ImageViewportWidth, 6);
        Assert.Equal(median.ImageViewportHeight + 35, median.CellHeight, 6);
        Assert.Equal(median.CellHeight + 4, median.RowPitch, 6);

        _output.WriteLine(
            "width={0:F3}; columns={1}; cell={2:F3}x{3:F3}; " +
            "imageViewport={4:F3}x{5:F3}; gaps={6:F3}..{7:F3}; " +
            "trailingSlack={8:F3}; imageRowFraction={9:F9}; runs={10}",
            median.GridViewportWidth,
            median.ColumnCount,
            median.CellWidth,
            median.CellHeight,
            median.ImageViewportWidth,
            median.ImageViewportHeight,
            median.MinimumGap,
            median.MaximumGap,
            median.TrailingSlack,
            median.ImageRowFraction,
            MeasurementRuns);
    }

    [AvaloniaTheory]
    [InlineData(BrowseThumbnailSize.Small)]
    [InlineData(BrowseThumbnailSize.Medium)]
    [InlineData(BrowseThumbnailSize.Large)]
    public void StretchedWidth_PreservesRatioAndRowPitchAcrossTiers(
        BrowseThumbnailSize size)
    {
        var measurement = MeasureDensity(673, size);

        Assert.True(measurement.CellWidth > BrowseThumbnailGeometry.For(size).ItemWidth);
        Assert.InRange(
            measurement.ImageViewportWidth / measurement.ImageViewportHeight,
            1.49, 1.51);
        Assert.Equal(measurement.ImageViewportHeight + 35,
            measurement.CellHeight, 6);
        Assert.Equal(measurement.CellHeight + 4, measurement.RowPitch, 6);
        Assert.Equal(measurement.CellWidth, measurement.LastRowCellWidth, 6);
    }

    [AvaloniaFact]
    public async Task NonIntegralStretchWidth_KeyboardAndPointerUseRealizedTiles()
    {
        using var root = new TemporaryDirectory();
        using var catalog = new CatalogService(Path.Combine(root.Path, "catalog"));
        await catalog.InitializeAsync();
        await using var viewModel = new MainWindowViewModel(
            catalog,
            new NullBaseLoader(),
            _ => Task.CompletedTask);
        viewModel.ShowWorkspaceReady(
            MainWindowViewModel.CurrentFirstRunExperienceVersion);
        var images = NewImages(12);
        viewModel.Browse.SetImages(images);
        viewModel.SelectedImage = images[0];
        var window = new MainWindow
        {
            Width = 1400,
            Height = 800,
            DataContext = viewModel
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        try
        {
            var grid = window.FindControl<BrowseGridView>("BrowseGridView")!;
            grid.Width = 777;
            Dispatcher.UIThread.RunJobs();
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(777, grid.Bounds.Width, 6);
            Assert.Equal(4, grid.GetItemsPerRow());
            Assert.True(grid.Focus());
            window.KeyPress(
                Key.Down,
                RawInputModifiers.None,
                PhysicalKey.ArrowDown,
                null);
            Dispatcher.UIThread.RunJobs();
            Assert.Same(images[4], viewModel.SelectedImage);

            var tile = window.GetVisualDescendants().OfType<Border>()
                .Single(border => border.Name == "ThumbnailTile" &&
                                  ReferenceEquals(border.DataContext, images[6]));
            var point = tile.TranslatePoint(
                new Point(tile.Bounds.Width / 2, tile.Bounds.Height / 2),
                window)!.Value;
            window.MouseDown(point, MouseButton.Left, RawInputModifiers.None);
            window.MouseUp(point, MouseButton.Left, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();

            Assert.Same(images[6], viewModel.SelectedImage);
            Assert.Same(images[6], Assert.Single(viewModel.Browse.GetSelectedImages()));
        }
        finally
        {
            window.DataContext = null;
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task MediumTier_RepeatedResizeDoesNotReloadSatisfiedImages()
    {
        var samples = new ResizeChurnMeasurement[MeasurementRuns];
        for (var run = 0; run < samples.Length; run++)
        {
            samples[run] = await MeasureResizeChurnAsync();
        }

        var median = Median(samples);
        Assert.All(samples, sample => Assert.Equal(median, sample));
        Assert.Equal(0, median.RepeatRequests);
        Assert.Equal(median.UniqueLoadedImages, median.TotalLoadRequests);

        _output.WriteLine(
            "sequence={0}; cycles={1}; uniqueLoaded={2}; totalLoadRequests={3}; " +
            "repeatRequests={4}; maxRequestsPerImage={5}; runs={6}",
            string.Join(",", Widths.Select(width => width.ToString("F0"))),
            4,
            median.UniqueLoadedImages,
            median.TotalLoadRequests,
            median.RepeatRequests,
            median.MaximumRequestsPerImage,
            MeasurementRuns);
    }

    private static DensityMeasurement MeasureDensity(
        double viewportWidth,
        BrowseThumbnailSize size = BrowseThumbnailSize.Medium)
    {
        var images = NewImages(7);
        var grid = new BrowseGridView
        {
            Width = viewportWidth,
            Height = 500,
            Images = images,
            TotalImageCount = images.Count,
            ThumbnailSize = size
        };
        var window = new Window
        {
            Width = viewportWidth,
            Height = 500,
            Content = grid
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        try
        {
            var scroll = grid.FindControl<ScrollViewer>("ThumbnailScrollViewer")!;
            var repeater = grid.FindControl<ItemsRepeater>("ThumbnailGrid")!;
            var tiles = repeater.GetVisualDescendants()
                .OfType<Border>()
                .Where(border => border.Classes.Contains("thumbnail"))
                .Select(tile => new RealizedTile(
                    tile,
                    tile.TranslatePoint(default, repeater)!.Value))
                .OrderBy(tile => tile.Origin.Y)
                .ThenBy(tile => tile.Origin.X)
                .ToArray();
            Assert.NotEmpty(tiles);

            var rowOrigins = tiles.Select(tile => tile.Origin.Y).Distinct().ToArray();
            Assert.True(rowOrigins.Length > 1);
            var firstRowY = tiles[0].Origin.Y;
            var firstRow = tiles
                .Where(tile => Math.Abs(tile.Origin.Y - firstRowY) < 0.001)
                .ToArray();
            Assert.NotEmpty(firstRow);
            var gaps = firstRow.Zip(firstRow.Skip(1), (left, right) =>
                right.Origin.X - (left.Origin.X + left.Tile.Bounds.Width)).ToArray();
            var imagePanel = Assert.IsType<Panel>(
                Assert.IsType<StackPanel>(firstRow[0].Tile.Child).Children[0]);
            var last = firstRow[^1];
            var lastRow = tiles
                .Where(tile => Math.Abs(tile.Origin.Y - rowOrigins[^1]) < 0.001)
                .ToArray();
            Assert.NotEmpty(lastRow);
            var trailingSlack = repeater.Bounds.Width -
                (last.Origin.X + last.Tile.Bounds.Width);
            var fraction = firstRow.Length * imagePanel.Bounds.Width /
                repeater.Bounds.Width;

            return new DensityMeasurement(
                scroll.Bounds.Width,
                firstRow.Length,
                firstRow[0].Tile.Bounds.Width,
                firstRow[0].Tile.Bounds.Height,
                tiles.Min(tile => tile.Tile.Bounds.Width),
                tiles.Max(tile => tile.Tile.Bounds.Width),
                lastRow[0].Tile.Bounds.Width,
                imagePanel.Bounds.Width,
                imagePanel.Bounds.Height,
                rowOrigins[1] - rowOrigins[0],
                gaps.Length == 0 ? 0 : gaps.Min(),
                gaps.Length == 0 ? 0 : gaps.Max(),
                trailingSlack,
                fraction);
        }
        finally
        {
            window.Close();
        }
    }

    private static async Task<ResizeChurnMeasurement> MeasureResizeChurnAsync()
    {
        var images = NewImages(60);
        var requests = new ConcurrentDictionary<ImageFile, int>(
            ReferenceEqualityComparer.Instance);
        var request = ThumbnailSizeRequest.For(BrowseThumbnailSize.Medium);
        using var cancellation = new CancellationTokenSource();
        using var scheduler = new ThumbnailLoadScheduler(
            1,
            (image, _, _) =>
            {
                requests.AddOrUpdate(image, 1, (_, count) => count + 1);
                image.Thumbnail = NewSatisfiedThumbnail();
                return Task.CompletedTask;
            },
            cancellation.Token);
        var grid = new BrowseGridView
        {
            Width = Widths[0],
            Height = 500,
            Images = images,
            TotalImageCount = images.Count,
            ThumbnailSize = BrowseThumbnailSize.Medium
        };
        grid.ViewportRangeChanged += (_, range) => scheduler.Enqueue(
            images.Skip(range.StartIndex).Take(range.Count)
                .Select(image => new ThumbnailLoadRequest(image, request, 0)));
        var window = new Window
        {
            Width = Widths[0],
            Height = 500,
            Content = grid
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        try
        {
            for (var cycle = 0; cycle < 4; cycle++)
            {
                foreach (var width in Widths)
                {
                    window.Width = width;
                    grid.Width = width;
                    Dispatcher.UIThread.RunJobs();
                    await Task.Yield();
                    Dispatcher.UIThread.RunJobs();
                }
            }

            await TestWaits.UntilAsync(() => scheduler.DesiredCount == 0);
            var counts = requests.Values.ToArray();
            return new ResizeChurnMeasurement(
                requests.Count,
                counts.Sum(),
                counts.Sum(count => Math.Max(0, count - 1)),
                counts.Length == 0 ? 0 : counts.Max());
        }
        finally
        {
            window.Close();
            cancellation.Cancel();
            await scheduler.Completion;
            foreach (var image in images)
            {
                image.Thumbnail?.Dispose();
                image.Thumbnail = null;
            }
        }
    }

    [AvaloniaFact]
    public async Task StretchedUnselectedTiles_RenderRoundedThumbnailCorners()
    {
        using var catalog = new CatalogService(Path.Combine(
            Path.GetTempPath(),
            $"happy-photon-griddensity-{Guid.NewGuid():N}"));
        await catalog.InitializeAsync();
        await using var viewModel = new MainWindowViewModel(catalog);
        viewModel.ShowWorkspaceReady(
            MainWindowViewModel.CurrentFirstRunExperienceVersion);
        var images = NewImages(7);
        foreach (var image in images)
        {
            image.SwapThumbnail(NewWhiteThumbnail());
        }
        // A portrait thumbnail letterboxes: its own corners sit inboard of the
        // viewport and must round there, not at the viewport corners.
        images[1].SwapThumbnail(NewWhiteThumbnail(128, 192));
        var grid = new BrowseGridView
        {
            Width = 777,
            Height = 500,
            Images = images,
            TotalImageCount = images.Count,
            ThumbnailSize = BrowseThumbnailSize.Medium,
            DataContext = viewModel
        };
        var window = new Window { Width = 777, Height = 500, Content = grid };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        try
        {
            var repeater = grid.FindControl<ItemsRepeater>("ThumbnailGrid")!;
            var clips = repeater.GetVisualDescendants()
                .OfType<Border>()
                .Where(border => border.Name == "ThumbnailContentClip")
                .Select(border => (Border: border,
                    Origin: border.TranslatePoint(default, window)!.Value))
                .OrderBy(entry => entry.Origin.Y)
                .ThenBy(entry => entry.Origin.X)
                .ToArray();
            Assert.True(clips.Length >= 2);

            using var frame = window.CaptureRenderedFrame() ??
                throw new InvalidOperationException("Grid frame was empty.");
            // Every realized tile, none selected: the white thumbnail must be
            // clipped off the 8px-radius corner but present at the center.
            var frames = repeater.GetVisualDescendants()
                .OfType<Grid>()
                .Where(grid => grid.Name == "ThumbnailImageFrame")
                .Select(grid => (Frame: grid,
                    Origin: grid.TranslatePoint(default, window)!.Value))
                .ToArray();
            Assert.Equal(clips.Length, frames.Length);
            Assert.Contains(frames, entry =>
                entry.Frame.Bounds.Width < entry.Frame.Bounds.Height);
            foreach (var (frame2, origin2) in frames)
            {
                var fw = frame2.Bounds.Width;
                var fh = frame2.Bounds.Height;
                Assert.Equal(0xFFFFFFFFu,
                    SamplePixel(frame, origin2.X + fw / 2, origin2.Y + fh / 2));
                foreach (var (cx, cy, name) in new[]
                {
                    (origin2.X + 1.5, origin2.Y + 1.5, "top-left"),
                    (origin2.X + fw - 1.5, origin2.Y + 1.5, "top-right"),
                    (origin2.X + 1.5, origin2.Y + fh - 1.5, "bottom-left"),
                    (origin2.X + fw - 1.5, origin2.Y + fh - 1.5, "bottom-right")
                })
                {
                    var corner = SamplePixel(frame, cx, cy);
                    Assert.True(corner != 0xFFFFFFFFu,
                        $"image {name} corner rendered unclipped white ({corner:X8}).");
                }
            }

            foreach (var (border, origin) in clips)
            {
                var w = border.Bounds.Width;
                var h = border.Bounds.Height;
                var center = SamplePixel(frame, origin.X + w / 2, origin.Y + h / 2);
                Assert.Equal(0xFFFFFFFFu, center);
                foreach (var (cx, cy, name) in new[]
                {
                    (origin.X + 1.5, origin.Y + 1.5, "top-left"),
                    (origin.X + w - 1.5, origin.Y + 1.5, "top-right"),
                    (origin.X + 1.5, origin.Y + h - 1.5, "bottom-left"),
                    (origin.X + w - 1.5, origin.Y + h - 1.5, "bottom-right")
                })
                {
                    var corner = SamplePixel(frame, cx, cy);
                    Assert.True(corner != 0xFFFFFFFFu,
                        $"{name} corner rendered unclipped white ({corner:X8}).");
                }
            }
        }
        finally
        {
            window.Close();
        }
    }

    private static uint SamplePixel(WriteableBitmap frame, double x, double y)
    {
        using var buffer = frame.Lock();
        var px = (int)Math.Round(x * buffer.Dpi.X / 96);
        var py = (int)Math.Round(y * buffer.Dpi.Y / 96);
        return (uint)System.Runtime.InteropServices.Marshal.ReadInt32(
            buffer.Address + py * buffer.RowBytes + px * 4);
    }

    private static WriteableBitmap NewWhiteThumbnail(int width = 192, int height = 128)
    {
        var bitmap = new WriteableBitmap(
            new PixelSize(width, height),
            new Vector(96, 96),
            PixelFormat.Bgra8888,
            AlphaFormat.Premul);
        using var buffer = bitmap.Lock();
        for (var i = 0; i < width * height; i++)
        {
            System.Runtime.InteropServices.Marshal.WriteInt32(
                buffer.Address + i * 4, unchecked((int)0xFFFFFFFFu));
        }
        return bitmap;
    }

    [AvaloniaFact]
    public void FilteredRecycledTile_KeepsChipRowInsideTheCard()
    {
        var images = NewImages(11);
        foreach (var image in images) image.SwapThumbnail(NewWhiteThumbnail());
        images[2].ColorLabel = HappyPhoton.Models.ColorLabel.Green;
        var grid = new BrowseGridView
        {
            Width = 900,
            Height = 600,
            Images = images,
            TotalImageCount = images.Count,
            ThumbnailSize = BrowseThumbnailSize.Medium
        };
        var window = new Window { Width = 900, Height = 600, Content = grid };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        // Simulate the Picked filter: swap the collection down to one image.
        var filtered = new ObservableCollection<ImageFile> { images[2] };
        grid.Images = filtered;
        grid.TotalImageCount = 1;
        Dispatcher.UIThread.RunJobs();
        try
        {
            var repeater = grid.FindControl<ItemsRepeater>("ThumbnailGrid")!;
            var tile = repeater.GetVisualDescendants().OfType<Border>()
                .First(border => border.Classes.Contains("thumbnail"));
            var stack = (StackPanel)tile.Child!;
            var panel = (Panel)stack.Children[0];
            var chipGrid = (Grid)stack.Children[1];
            var dot = tile.GetVisualDescendants().OfType<Border>()
                .First(border => border.Classes.Contains("color-label-marker"));
            var tileOrigin = tile.TranslatePoint(default, repeater)!.Value;
            var panelOrigin = panel.TranslatePoint(default, repeater)!.Value;
            var chipOrigin = chipGrid.TranslatePoint(default, repeater)!.Value;
            var dotOrigin = dot.TranslatePoint(default, repeater)!.Value;
            Assert.True(dot.IsVisible);
            Assert.Equal(panelOrigin.X, chipOrigin.X, 3);
            Assert.Equal(panel.Bounds.Width, chipGrid.Bounds.Width, 3);
            // The dot's right edge sits flush with the image edge, inside the card.
            Assert.Equal(
                chipOrigin.X + chipGrid.Bounds.Width,
                dotOrigin.X + dot.Bounds.Width, 3);
            Assert.True(dotOrigin.X + dot.Bounds.Width <
                tileOrigin.X + tile.Bounds.Width);
        }
        finally
        {
            window.Close();
        }
    }

    private static ObservableCollection<ImageFile> NewImages(int count) => new(
        Enumerable.Range(0, count)
            .Select(index => new ImageFile($"density-{index:D3}.jpg")));

    private static WriteableBitmap NewSatisfiedThumbnail() => new(
        new PixelSize(192, 128),
        new Vector(96, 96),
        PixelFormat.Bgra8888,
        AlphaFormat.Premul);

    private static DensityMeasurement Median(DensityMeasurement[] samples) =>
        samples.OrderBy(sample => sample.TrailingSlack).ElementAt(samples.Length / 2);

    private static ResizeChurnMeasurement Median(ResizeChurnMeasurement[] samples) =>
        samples.OrderBy(sample => sample.RepeatRequests).ElementAt(samples.Length / 2);

    private sealed record RealizedTile(Border Tile, Point Origin);

    private sealed record DensityMeasurement(
        double GridViewportWidth,
        int ColumnCount,
        double CellWidth,
        double CellHeight,
        double MinimumCellWidth,
        double MaximumCellWidth,
        double LastRowCellWidth,
        double ImageViewportWidth,
        double ImageViewportHeight,
        double RowPitch,
        double MinimumGap,
        double MaximumGap,
        double TrailingSlack,
        double ImageRowFraction);

    private sealed record ResizeChurnMeasurement(
        int UniqueLoadedImages,
        int TotalLoadRequests,
        int RepeatRequests,
        int MaximumRequestsPerImage);
}
