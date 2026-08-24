namespace HappyPhoton.Models;

internal static class CropGeometry
{
    /// <summary>
    /// Returns the orientation-independent long-edge/short-edge aspect ratio.
    /// Invalid dimensions return <see langword="null"/> so callers can choose
    /// whether unavailable geometry should be accepted or rejected.
    /// </summary>
    public static double? AspectRatio(long width, long height)
    {
        if (width <= 0 || height <= 0)
        {
            return null;
        }

        return Math.Max(width, height) / (double)Math.Min(width, height);
    }

    /// <summary>
    /// Compares two frame aspect ratios relative to the reference frame.
    /// </summary>
    public static double? RelativeAspectRatioDifference(
        long referenceWidth,
        long referenceHeight,
        long candidateWidth,
        long candidateHeight)
    {
        var referenceRatio = AspectRatio(referenceWidth, referenceHeight);
        var candidateRatio = AspectRatio(candidateWidth, candidateHeight);
        return RelativeAspectRatioDifference(referenceRatio, candidateRatio);
    }

    /// <summary>
    /// Center-crops one frame toward another frame's aspect ratio without
    /// mutating either frame. Browse normalization crops the embedded preview
    /// toward the visible RAW frame; exposure estimation does the opposite and
    /// crops the RAW base toward the preview frame.
    /// </summary>
    public static CenterCropRectangle? CenterCropToAspect(
        long cropWidth,
        long cropHeight,
        long referenceWidth,
        long referenceHeight)
    {
        var cropRatio = AspectRatio(cropWidth, cropHeight);
        var referenceRatio = AspectRatio(referenceWidth, referenceHeight);
        if (cropRatio == null || referenceRatio == null)
        {
            return null;
        }

        var sourceWidth = checked((uint)cropWidth);
        var sourceHeight = checked((uint)cropHeight);
        uint width;
        uint height;
        if (sourceWidth >= sourceHeight)
        {
            width = cropRatio > referenceRatio
                ? checked((uint)Math.Round(sourceHeight * referenceRatio.Value))
                : sourceWidth;
            height = cropRatio > referenceRatio
                ? sourceHeight
                : checked((uint)Math.Round(sourceWidth / referenceRatio.Value));
        }
        else
        {
            height = cropRatio > referenceRatio
                ? checked((uint)Math.Round(sourceWidth * referenceRatio.Value))
                : sourceHeight;
            width = cropRatio > referenceRatio
                ? sourceWidth
                : checked((uint)Math.Round(sourceHeight / referenceRatio.Value));
        }

        width = Math.Clamp(width, 1u, sourceWidth);
        height = Math.Clamp(height, 1u, sourceHeight);
        return new CenterCropRectangle(
            checked((int)((sourceWidth - width) / 2)),
            checked((int)((sourceHeight - height) / 2)),
            width,
            height);
    }

    private static double? RelativeAspectRatioDifference(
        double? referenceRatio,
        double? candidateRatio)
    {
        if (referenceRatio == null || candidateRatio == null)
        {
            return null;
        }

        return Math.Abs(referenceRatio.Value - candidateRatio.Value) /
            referenceRatio.Value;
    }

    public static CropRegion Intersect(CropRegion crop, CropRegion bounds)
    {
        var left = Math.Max(crop.Left, bounds.Left);
        var top = Math.Max(crop.Top, bounds.Top);
        var right = Math.Min(crop.Right, bounds.Right);
        var bottom = Math.Min(crop.Bottom, bounds.Bottom);

        EnsureRange(ref left, ref right, bounds.Left, bounds.Right);
        EnsureRange(ref top, ref bottom, bounds.Top, bounds.Bottom);

        return new CropRegion
        {
            Left = left,
            Top = top,
            Right = right,
            Bottom = bottom
        };
    }

    private static void EnsureRange(ref double start, ref double end, double min, double max)
    {
        if (end > start)
        {
            return;
        }

        var center = Math.Clamp((start + end) / 2, min, max);
        var halfSize = Math.Min(0.005, (max - min) / 2);
        start = Math.Clamp(center - halfSize, min, max);
        end = Math.Clamp(center + halfSize, min, max);
    }

}

internal readonly record struct CenterCropRectangle(
    int X,
    int Y,
    uint Width,
    uint Height);
