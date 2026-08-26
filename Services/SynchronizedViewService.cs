namespace HappyPhoton.Services;

public readonly record struct NormalizedPoint(double X, double Y)
{
    public NormalizedPoint Clamp() => new(
        Math.Clamp(double.IsFinite(X) ? X : 0.5, 0, 1),
        Math.Clamp(double.IsFinite(Y) ? Y : 0.5, 0, 1));
}

public readonly record struct ImagePoint(double X, double Y);

public static class SynchronizedViewMath
{
    public static NormalizedPoint NormalizePoint(
        ImagePoint point,
        double imageWidth,
        double imageHeight)
    {
        ValidateDimensions(imageWidth, imageHeight);
        return new NormalizedPoint(
            point.X / imageWidth,
            point.Y / imageHeight).Clamp();
    }

    public static ImagePoint MapPoint(
        NormalizedPoint point,
        double imageWidth,
        double imageHeight)
    {
        ValidateDimensions(imageWidth, imageHeight);
        point = point.Clamp();
        return new ImagePoint(
            point.X * imageWidth,
            point.Y * imageHeight);
    }

    private static void ValidateDimensions(double width, double height)
    {
        if (!double.IsFinite(width) || !double.IsFinite(height) ||
            width <= 0 || height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width));
        }
    }
}

public readonly record struct NormalizedViewport(
    NormalizedPoint Center,
    double ZoomRelativeToFit)
{
    public static NormalizedViewport Fit { get; } =
        new(new NormalizedPoint(0.5, 0.5), 1);

    public NormalizedViewport Clamp() => new(
        Center.Clamp(),
        Math.Clamp(
            double.IsFinite(ZoomRelativeToFit) ? ZoomRelativeToFit : 1,
            1,
            100));
}

public readonly record struct NormalizedCenterBounds(
    double MinimumX,
    double MaximumX,
    double MinimumY,
    double MaximumY)
{
    public static NormalizedCenterBounds Unconstrained { get; } =
        new(0, 1, 0, 1);
}

public sealed class SynchronizedViewService
{
    private NormalizedViewport _viewport = NormalizedViewport.Fit;

    public NormalizedViewport Viewport => _viewport;

    public event EventHandler<NormalizedViewport>? ViewportChanged;

    public void Reset() => SetViewport(NormalizedViewport.Fit);

    public void SetViewport(NormalizedViewport viewport)
    {
        SetViewport(viewport, []);
    }

    public void SetViewport(
        NormalizedViewport viewport,
        IEnumerable<NormalizedCenterBounds> centerBounds)
    {
        viewport = viewport.Clamp();
        var bounds = centerBounds.ToArray();
        if (bounds.Length > 0)
        {
            var minimumX = bounds.Max(item => item.MinimumX);
            var maximumX = bounds.Min(item => item.MaximumX);
            var minimumY = bounds.Max(item => item.MinimumY);
            var maximumY = bounds.Min(item => item.MaximumY);
            viewport = viewport with
            {
                Center = new NormalizedPoint(
                    Math.Clamp(viewport.Center.X, minimumX, maximumX),
                    Math.Clamp(viewport.Center.Y, minimumY, maximumY))
            };
        }
        if (viewport == _viewport) return;

        _viewport = viewport;
        ViewportChanged?.Invoke(this, viewport);
    }

    public NormalizedPoint MapPoint(NormalizedPoint point) => point.Clamp();
}
