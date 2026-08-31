using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using HappyPhoton.Models;

namespace HappyPhoton.Views;

internal readonly record struct BrowseThumbnailGeometry(
    double ImageWidth,
    double ImageHeight,
    double ItemWidth,
    double ItemHeight,
    double RowSpacing,
    double ColumnSpacing,
    double GridMargin)
{
    private const double TilePadding = 3;
    private const double TileBorder = 2;
    private const double ChipRowTopMargin = 3;
    private const double ChipRowHeight = 22;

    public double RowHeight => ItemHeight + RowSpacing;

    public static BrowseThumbnailGeometry For(BrowseThumbnailSize size) =>
        FromItemWidth(size switch
        {
            BrowseThumbnailSize.Small => 126,
            BrowseThumbnailSize.Medium => 186,
            BrowseThumbnailSize.Large => 286,
            _ => 186
        });

    public static BrowseThumbnailGeometry FromItemWidth(double itemWidth)
    {
        var imageWidth = itemWidth - (TilePadding + TileBorder) * 2;
        // Whole-pixel height keeps rows pixel-aligned under layout rounding.
        var imageHeight = Math.Round(imageWidth * 2 / 3);
        return new(
            imageWidth,
            imageHeight,
            itemWidth,
            imageHeight + (TilePadding + TileBorder) * 2 +
                ChipRowTopMargin + ChipRowHeight,
            4,
            4,
            4);
    }
}

public partial class BrowseGridView
{
    private BrowseThumbnailGeometry _realizedGeometry =
        BrowseThumbnailGeometry.For(BrowseThumbnailSize.Medium);
    private BrowseThumbnailGeometry Geometry => _realizedGeometry;

    public double ImageViewportWidth => Geometry.ImageWidth;
    public double ImageViewportHeight => Geometry.ImageHeight;
    public double ThumbnailMinimumItemWidth =>
        BrowseThumbnailGeometry.For(ThumbnailSize).ItemWidth;
    public double ThumbnailItemHeight => Geometry.ItemHeight;

    private void ApplyThumbnailGeometry()
    {
        var anchorIndex = SelectedImage == null || Images == null
            ? -1
            : Images.IndexOf(SelectedImage);
        var minimumGeometry = BrowseThumbnailGeometry.For(ThumbnailSize);
        RaisePropertyChanged(
            ThumbnailMinimumItemWidthProperty,
            0d,
            minimumGeometry.ItemWidth);
        SetRealizedGeometry(minimumGeometry);
        ThumbnailGrid.InvalidateMeasure();
        _lastViewportStart = -1;
        _lastViewportCount = -1;
        Dispatcher.UIThread.Post(() =>
        {
            if (anchorIndex >= 0)
            {
                ScrollItemIntoView(anchorIndex);
            }

            ReportViewportRange();
        }, DispatcherPriority.Loaded);
    }

    private void OnThumbnailTileSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (e.NewSize.Width <= 0 ||
            Math.Abs(e.NewSize.Width - Geometry.ItemWidth) < 0.001)
        {
            return;
        }

        SetRealizedGeometry(BrowseThumbnailGeometry.FromItemWidth(e.NewSize.Width));
        ThumbnailGrid.InvalidateMeasure();
        _lastViewportStart = -1;
        _lastViewportCount = -1;
        QueueViewportReport();
    }

    private void SetRealizedGeometry(BrowseThumbnailGeometry geometry)
    {
        var previous = _realizedGeometry;
        _realizedGeometry = geometry;
        RaisePropertyChanged(
            ImageViewportWidthProperty,
            previous.ImageWidth,
            geometry.ImageWidth);
        RaisePropertyChanged(
            ImageViewportHeightProperty,
            previous.ImageHeight,
            geometry.ImageHeight);
        RaisePropertyChanged(
            ThumbnailItemHeightProperty,
            previous.ItemHeight,
            geometry.ItemHeight);
    }

    public int GetItemsPerRow(double? availableWidth = null)
    {
        var geometry = Geometry;
        var width = availableWidth ?? ThumbnailGrid.Bounds.Width;
        if (width <= 0) return 1;

        return Math.Max(1, (int)Math.Floor(
            (width + geometry.ColumnSpacing) /
            (geometry.ItemWidth + geometry.ColumnSpacing)));
    }

    public int GetRowsPerPage(double? viewportHeight = null)
    {
        var height = viewportHeight ?? ThumbnailScrollViewer.Viewport.Height;
        if (height <= 0) return 1;

        return Math.Max(1, (int)Math.Floor(height / Geometry.RowHeight));
    }

    public void ScrollItemIntoView(int index)
    {
        if (index < 0 || Images == null || index >= Images.Count) return;

        var geometry = Geometry;
        var row = index / GetItemsPerRow();
        var itemTop = geometry.GridMargin + row * geometry.RowHeight;
        var itemBottom = itemTop + geometry.ItemHeight;
        var viewportTop = ThumbnailScrollViewer.Offset.Y;
        var viewportBottom = viewportTop + ThumbnailScrollViewer.Viewport.Height;

        if (itemTop < viewportTop)
        {
            ThumbnailScrollViewer.Offset = new Vector(0, itemTop);
        }
        else if (itemBottom > viewportBottom)
        {
            ThumbnailScrollViewer.Offset = new Vector(
                0,
                itemBottom - ThumbnailScrollViewer.Viewport.Height);
        }
    }

    public string? CaptureViewportAnchorPath()
    {
        if (Images == null || Images.Count == 0) return null;
        var row = Math.Max(0, (int)Math.Floor(
            ThumbnailScrollViewer.Offset.Y / Geometry.RowHeight));
        var index = Math.Min(Images.Count - 1, row * GetItemsPerRow());
        return Images[index].FilePath;
    }

    public void RestoreViewportAnchorPath(string? filePath)
    {
        if (Images == null || string.IsNullOrWhiteSpace(filePath)) return;
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        var index = Images.IndexOf(Images.FirstOrDefault(image =>
            string.Equals(image.FilePath, filePath, comparison))!);
        if (index < 0) return;
        Dispatcher.UIThread.Post(
            () => ScrollItemIntoView(index),
            DispatcherPriority.Loaded);
    }
}
