namespace HappyPhoton.Tests;

// Compact, blittable 40-byte records; stroke identity belongs to the contiguous stroke range.
// Double coordinates retain the fixed oracle tolerance. No production helpers are used.
internal readonly record struct BrushSegment(double X, double Y, double Dx, double Dy, double InverseLength2)
{
    internal double DistanceSquared(double x, double y)
    {
        var dx = x - X; var dy = y - Y;
        var t = Math.Clamp((dx * Dx + dy * Dy) * InverseLength2, 0, 1);
        dx -= t * Dx; dy -= t * Dy;
        return dx * dx + dy * dy;
    }
}

internal sealed class LocalsBrushSegments
{
    internal readonly record struct Stroke(int Start, int Count, double Left, double Top, double Right,
        double Bottom, double Radius, double Feather, double Flow, bool Erase)
    {
        internal bool Contains(double x, double y) => x >= Left && x <= Right && y >= Top && y <= Bottom;
        internal double CoreSquared => Radius * Radius * (1 - Feather) * (1 - Feather);
        internal double Weight(double distance2)
        {
            if (distance2 >= Radius * Radius) return 0;
            if (Feather == 0) return Flow;
            var ramp = Math.Clamp((1 - Math.Sqrt(distance2) / Radius) / Feather, 0, 1);
            return Flow * ramp * ramp * (3 - 2 * ramp);
        }
    }

    internal BrushSegment[] Segments { get; }
    internal Stroke[] Strokes { get; }
    internal double FrameWidth { get; }
    internal double FrameHeight { get; }
    internal long PayloadBytes => Segments.LongLength * 40 + Strokes.LongLength * 72;

    internal LocalsBrushSegments(BrushDocument document, int width, int height)
    {
        var edge = (double)Math.Max(width, height);
        FrameWidth = width / edge; FrameHeight = height / edge;
        Segments = new BrushSegment[document.Strokes.Sum(s => Math.Max(1, s.Points.Length - 1))];
        Strokes = new Stroke[document.Strokes.Length];
        var next = 0;
        for (var s = 0; s < Strokes.Length; s++)
        {
            var stroke = document.Strokes[s]; var start = next;
            double left = double.PositiveInfinity, top = left, right = double.NegativeInfinity, bottom = right;
            for (var p = 0; p < Math.Max(1, stroke.Points.Length - 1); p++)
            {
                var a = stroke.Points[p]; var b = stroke.Points[Math.Min(p + 1, stroke.Points.Length - 1)];
                var x = a.U * FrameWidth; var y = a.V * FrameHeight;
                var dx = (b.U - a.U) * FrameWidth; var dy = (b.V - a.V) * FrameHeight;
                var length2 = dx * dx + dy * dy;
                Segments[next++] = new(x, y, dx, dy, length2 == 0 ? 0 : 1 / length2);
                left = Math.Min(left, Math.Min(x, x + dx)); right = Math.Max(right, Math.Max(x, x + dx));
                top = Math.Min(top, Math.Min(y, y + dy)); bottom = Math.Max(bottom, Math.Max(y, y + dy));
            }
            Strokes[s] = new(start, next - start, left - stroke.Radius, top - stroke.Radius,
                right + stroke.Radius, bottom + stroke.Radius, stroke.Radius, stroke.Feather, stroke.Flow, stroke.Erase);
        }
    }
}
