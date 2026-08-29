namespace HappyPhoton.Services;

public sealed record WindowPlacement(
    int Version,
    double X,
    double Y,
    double Width,
    double Height,
    double Scaling,
    bool Maximized)
{
    public const int CurrentVersion = 1;
    public static WindowPlacement? Resolve(
        WindowPlacement? saved,
        IReadOnlyList<WindowPlacementScreen> screens,
        (double Width, double Height) minimumSize)
    {
        if (saved is not { Version: CurrentVersion } ||
            !double.IsFinite(saved.X) || !double.IsFinite(saved.Y) ||
            !Positive(saved.Width) || !Positive(saved.Height) ||
            !Positive(saved.Scaling) ||
            saved.Width < minimumSize.Width ||
            saved.Height < minimumSize.Height ||
            saved.X < int.MinValue || saved.X > int.MaxValue ||
            saved.Y < int.MinValue || saved.Y > int.MaxValue)
        {
            return null;
        }
        var savedPosition = new Avalonia.PixelPoint(
            (int)Math.Floor(saved.X), (int)Math.Floor(saved.Y));
        var topLeftScreen = screens.FirstOrDefault(screen =>
            screen.Bounds.Contains(savedPosition) && Positive(screen.Scaling));
        var scaling = topLeftScreen == default ? saved.Scaling : topLeftScreen.Scaling;
        var physicalWidth = saved.Width * scaling;
        var physicalHeight = saved.Height * scaling;
        if (!Positive(physicalWidth) || !Positive(physicalHeight) ||
            !screens.Any(screen =>
                screen.WorkingArea.Width >= physicalWidth &&
                screen.WorkingArea.Height >= physicalHeight))
        {
            return null;
        }
        var right = saved.X + physicalWidth;
        var bottom = saved.Y + physicalHeight;
        if (!double.IsFinite(right) || !double.IsFinite(bottom)) return null;
        var area = physicalWidth * physicalHeight;
        return screens.Any(screen =>
        {
            var working = screen.WorkingArea;
            var overlapWidth = Math.Max(
                0, Math.Min(right, working.Right) - Math.Max(saved.X, working.X));
            var overlapHeight = Math.Max(
                0, Math.Min(bottom, working.Bottom) - Math.Max(saved.Y, working.Y));
            return saved.Y >= working.Y && saved.Y < working.Bottom &&
                   overlapWidth * overlapHeight >= area / 2;
        }) ? saved : null;
    }

    private static bool Positive(double value) => double.IsFinite(value) && value > 0;
}

public readonly record struct WindowPlacementScreen(
    Avalonia.PixelRect Bounds, Avalonia.PixelRect WorkingArea, double Scaling);
