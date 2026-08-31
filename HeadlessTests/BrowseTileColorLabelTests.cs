using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
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
    public void MarkerAndCachedTooltipStayInTileAtEveryThumbnailSize(
        BrowseThumbnailSize size)
    {
        var image = new ImageFile(Path.Combine(
            Path.GetTempPath(), "worst-case-color-label-tile.jpg"))
        {
            HasEdits = true,
            Flag = ImageFlag.Rejected,
            Rating = 5,
            ColorLabel = ColorLabel.Yellow,
            BurstGroupOrdinal = 1,
            BurstIndex = 10,
            BurstSize = 10
        };
        image.ApplyMetadata(new ImageMetadata
        {
            PixelWidth = 6000,
            PixelHeight = 4000,
            DateTaken = new DateTime(2026, 8, 30, 14, 15, 0)
        });
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
            var clippedContent = Assert.Single(
                imagePanel.Children.OfType<Border>(),
                border => border.Name == "ThumbnailContentClip");
            var status = Assert.IsType<Grid>(content.Children[1]);
            var marker = Assert.Single(
                tile.GetVisualDescendants().OfType<Border>(),
                border => border.Classes.Contains("color-label-marker"));
            var editBadge = Assert.Single(
                status.GetVisualDescendants().OfType<Border>(),
                border => Equals(ToolTip.GetTip(border), "Has edits"));
            var badges = Assert.IsType<StackPanel>(marker.Parent);
            var chips = Assert.IsType<StackPanel>(status.Children[0]);
            var rejectBadge = Assert.Single(
                chips.Children.OfType<Border>(),
                border => Equals(ToolTip.GetTip(border), "Rejected"));

            Assert.True(marker.IsVisible);
            Assert.False(marker.IsHitTestVisible);
            Assert.Equal("Color label", ToolTip.GetTip(marker));
            Assert.Contains(marker, status.GetVisualDescendants());
            Assert.DoesNotContain(marker, imagePanel.GetVisualDescendants());
            Assert.DoesNotContain(tile.GetVisualDescendants().OfType<TextBlock>(),
                text => text.Text == image.FileName);
            var tooltip = Assert.IsType<string>(ToolTip.GetTip(tile));
            Assert.Contains(image.FileName, tooltip);
            Assert.Contains("6000×4000 pixels", tooltip);
            Assert.Contains("Aug 30, 2026", tooltip);
            var roundedClip = Assert.IsType<RectangleGeometry>(clippedContent.Clip);
            Assert.Equal(8, roundedClip.RadiusX);
            Assert.Equal(8, roundedClip.RadiusY);
            Assert.Equal(clippedContent.Bounds, roundedClip.Rect);
            Assert.Equal(
                ThemeResourceTests.Brush("RejectSurface", ThemeVariant.Dark).Color,
                Assert.IsType<SolidColorBrush>(rejectBadge.Background).Color);
            Assert.Equal(
                ThemeResourceTests.Brush("RejectOutline", ThemeVariant.Dark).Color,
                Assert.IsType<SolidColorBrush>(rejectBadge.BorderBrush).Color);
            Assert.Equal(new Thickness(1), rejectBadge.BorderThickness);
            var rejectGlyph = Assert.IsType<TextBlock>(rejectBadge.Child);
            Assert.Equal(
                ThemeResourceTests.Brush("RejectGlyph", ThemeVariant.Dark).Color,
                Assert.IsType<SolidColorBrush>(rejectGlyph.Foreground).Color);

            var markerRight = marker.TranslatePoint(
                new Point(marker.Bounds.Width, 0), status)!.Value.X;
            var editRight = editBadge.TranslatePoint(
                new Point(editBadge.Bounds.Width, 0), status)!.Value.X;
            var markerLeft = marker.TranslatePoint(default, status)!.Value.X;
            Assert.True(editRight <= markerLeft);
            Assert.True(markerRight <= status.Bounds.Width);

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

            AssertFitsInside(status, badges);
            foreach (var chip in chips.Children.Where(child => child.IsVisible))
            {
                AssertFitsInside(status, chip);
            }
            AssertFitsInside(status, marker);
        }
        finally
        {
            window.Close();
        }
    }

    private static void AssertFitsInside(Control status, Control child)
    {
        var origin = child.TranslatePoint(default, status)!.Value;

        Assert.True(origin.X >= 0);
        Assert.True(origin.Y >= 0);
        Assert.True(origin.X + child.Bounds.Width <= status.Bounds.Width);
        Assert.True(origin.Y + child.Bounds.Height <= status.Bounds.Height);
    }
}
