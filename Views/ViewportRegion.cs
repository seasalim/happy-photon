using Avalonia;

namespace HappyPhoton.Views;

internal static class ViewportRegion
{
    private const double HiddenVisibleAreaFraction = 0.995;

    public static Rect? Calculate(Rect imageBounds, Rect viewportBounds)
    {
        if (!IsValid(imageBounds) || !IsValid(viewportBounds))
        {
            return null;
        }

        var left = Math.Max(imageBounds.Left, viewportBounds.Left);
        var top = Math.Max(imageBounds.Top, viewportBounds.Top);
        var right = Math.Min(imageBounds.Right, viewportBounds.Right);
        var bottom = Math.Min(imageBounds.Bottom, viewportBounds.Bottom);
        if (right <= left || bottom <= top)
        {
            return null;
        }

        var visibleAreaFraction =
            (right - left) * (bottom - top) /
            (imageBounds.Width * imageBounds.Height);
        if (visibleAreaFraction >= HiddenVisibleAreaFraction)
        {
            return null;
        }

        var normalizedLeft = Math.Clamp(
            (left - imageBounds.Left) / imageBounds.Width,
            0,
            1);
        var normalizedTop = Math.Clamp(
            (top - imageBounds.Top) / imageBounds.Height,
            0,
            1);
        var normalizedRight = Math.Clamp(
            (right - imageBounds.Left) / imageBounds.Width,
            0,
            1);
        var normalizedBottom = Math.Clamp(
            (bottom - imageBounds.Top) / imageBounds.Height,
            0,
            1);

        return new Rect(
            normalizedLeft,
            normalizedTop,
            normalizedRight - normalizedLeft,
            normalizedBottom - normalizedTop);
    }

    public static Rect UniformImageBounds(Size availableSize, Size imageSize)
    {
        if (!IsValid(availableSize) || !IsValid(imageSize))
        {
            return default;
        }

        var scale = Math.Min(
            availableSize.Width / imageSize.Width,
            availableSize.Height / imageSize.Height);
        var width = imageSize.Width * scale;
        var height = imageSize.Height * scale;
        return new Rect(
            (availableSize.Width - width) / 2,
            (availableSize.Height - height) / 2,
            width,
            height);
    }

    public static Rect MapToImage(Rect normalizedRegion, Rect imageBounds)
    {
        var left = Math.Clamp(normalizedRegion.Left, 0, 1);
        var top = Math.Clamp(normalizedRegion.Top, 0, 1);
        var right = Math.Clamp(normalizedRegion.Right, left, 1);
        var bottom = Math.Clamp(normalizedRegion.Bottom, top, 1);
        return new Rect(
            imageBounds.X + left * imageBounds.Width,
            imageBounds.Y + top * imageBounds.Height,
            (right - left) * imageBounds.Width,
            (bottom - top) * imageBounds.Height);
    }

    private static bool IsValid(Rect bounds) =>
        IsValid(bounds.Size) &&
        double.IsFinite(bounds.X) &&
        double.IsFinite(bounds.Y);

    private static bool IsValid(Size size) =>
        double.IsFinite(size.Width) &&
        double.IsFinite(size.Height) &&
        size.Width > 0 &&
        size.Height > 0;
}
