using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HappyPhoton.Models;
using HappyPhoton.ViewModels;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class BrowseTileColorLabelTests
{
    [AvaloniaTheory]
    [InlineData(BrowseThumbnailSize.Small)]
    [InlineData(BrowseThumbnailSize.Medium)]
    [InlineData(BrowseThumbnailSize.Large)]
    public void MarkerStaysInCaptionBadgeRowAtEveryThumbnailSize(
        BrowseThumbnailSize size)
    {
        var image = new ImageFile(Path.Combine(
            Path.GetTempPath(), "worst-case-color-label-tile.jpg"))
        {
            HasEdits = true,
            Flag = ImageFlag.Picked,
            Rating = 5,
            ColorLabel = ColorLabel.Yellow,
            BurstGroupOrdinal = 1,
            BurstIndex = 10,
            BurstSize = 10
        };
        var control = new BrowseGridView
        {
            Images = new ObservableCollection<ImageFile> { image },
            TotalImageCount = 1,
            ThumbnailSize = size
        };
        var window = new Window
        {
            Width = 800,
            Height = 500,
            Content = control
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        try
        {
            var repeater = control.FindControl<ItemsRepeater>("ThumbnailGrid")!;
            var tile = Assert.Single(
                repeater.GetVisualDescendants().OfType<Border>(),
                border => border.Classes.Contains("thumbnail"));
            var content = Assert.IsType<StackPanel>(tile.Child);
            var imagePanel = Assert.IsType<Panel>(content.Children[0]);
            var caption = Assert.IsType<Grid>(content.Children[1]);
            var marker = Assert.Single(
                tile.GetVisualDescendants().OfType<Border>(),
                border => border.Classes.Contains("color-label-marker"));
            var editBadge = Assert.Single(
                caption.GetVisualDescendants().OfType<Border>(),
                border => Equals(ToolTip.GetTip(border), "Has edits"));
            var badgeRow = Assert.IsType<Grid>(marker.Parent);
            var chips = Assert.IsType<StackPanel>(badgeRow.Children[0]);

            Assert.True(marker.IsVisible);
            Assert.False(marker.IsHitTestVisible);
            Assert.Equal("Color label", ToolTip.GetTip(marker));
            Assert.Contains(marker, caption.GetVisualDescendants());
            Assert.DoesNotContain(marker, imagePanel.GetVisualDescendants());

            var markerRight = marker.TranslatePoint(
                new Point(marker.Bounds.Width, 0), caption)!.Value.X;
            var editRight = editBadge.TranslatePoint(
                new Point(editBadge.Bounds.Width, 0), caption)!.Value.X;
            Assert.Equal(editRight, markerRight, precision: 3);

            tile.Measure(new Size(
                control.ThumbnailItemWidth,
                double.PositiveInfinity));
            Assert.True(
                marker.Bounds.Height <= 12,
                $"Marker height {marker.Bounds.Height} exceeds its 12px row footprint.");
            Assert.True(
                tile.DesiredSize.Height <= control.ThumbnailItemHeight,
                $"Tile desired height {tile.DesiredSize.Height} exceeds " +
                $"item height {control.ThumbnailItemHeight} at {size}.");

            AssertFitsInside(caption, badgeRow);
            foreach (var chip in chips.Children.Where(child => child.IsVisible))
            {
                AssertFitsInside(caption, chip);
            }
            AssertFitsInside(caption, marker);
        }
        finally
        {
            window.Close();
        }
    }

    private static void AssertFitsInside(Control caption, Control child)
    {
        var origin = child.TranslatePoint(default, caption)!.Value;

        Assert.True(origin.X >= 0);
        Assert.True(origin.Y >= 0);
        Assert.True(origin.X + child.Bounds.Width <= caption.Bounds.Width);
        Assert.True(origin.Y + child.Bounds.Height <= caption.Bounds.Height);
    }
}
