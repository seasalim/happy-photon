using Avalonia;
using Avalonia.Controls;
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
    public double RowHeight => ItemHeight + RowSpacing;

    public static BrowseThumbnailGeometry For(BrowseThumbnailSize size) => size switch
    {
        BrowseThumbnailSize.Small => new(120, 80, 130, 135, 5, 5, 5),
        BrowseThumbnailSize.Medium => new(180, 120, 190, 175, 5, 5, 5),
        BrowseThumbnailSize.Large => new(280, 187, 290, 242, 5, 5, 5),
        _ => new(180, 120, 190, 175, 5, 5, 5)
    };
}

public partial class BrowseGridView
{
    private BrowseThumbnailGeometry Geometry =>
        BrowseThumbnailGeometry.For(ThumbnailSize);

    public double ImageViewportWidth => Geometry.ImageWidth;
    public double ImageViewportHeight => Geometry.ImageHeight;
    public double ThumbnailItemWidth => Geometry.ItemWidth;
    public double ThumbnailItemHeight => Geometry.ItemHeight;

    private void ApplyThumbnailGeometry()
    {
        var anchorIndex = SelectedImage == null || Images == null
            ? -1
            : Images.IndexOf(SelectedImage);
        var geometry = Geometry;
        RaisePropertyChanged(ImageViewportWidthProperty, 0d, geometry.ImageWidth);
        RaisePropertyChanged(ImageViewportHeightProperty, 0d, geometry.ImageHeight);
        RaisePropertyChanged(ThumbnailItemWidthProperty, 0d, geometry.ItemWidth);
        RaisePropertyChanged(ThumbnailItemHeightProperty, 0d, geometry.ItemHeight);
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

    public int GetItemsPerRow(double? availableWidth = null)
    {
        var geometry = Geometry;
        var width = availableWidth ??
            ThumbnailScrollViewer.Bounds.Width - geometry.GridMargin * 2;
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
