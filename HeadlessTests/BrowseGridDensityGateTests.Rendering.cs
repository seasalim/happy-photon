using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
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

public sealed partial class BrowseGridDensityGateTests
{
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
}
