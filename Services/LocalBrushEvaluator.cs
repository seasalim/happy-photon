using HappyPhoton.Models;

namespace HappyPhoton.Services;

// CSR grid fitted to expanded segment bounds, starting at r/2 and coarsened to fit budgets.
// Each cell contains stroke groups, so saturation can skip the rest of that stroke in O(1).
internal sealed class LocalBrushEvaluator
{
    private readonly record struct Group(int Stroke, int Start, int End);
    private readonly SegmentData data;
    private readonly int[] offsets, entries;
    private readonly Group[] groups;
    private readonly double left, top, right, bottom, cellSize;
    internal int Columns { get; }
    internal int Rows { get; }
    internal int SegmentCount => data.Segments.Length;
    internal int EntryCount => entries.Length;
    internal int WorstSegmentsPerCell { get; }
    internal long PayloadBytes => data.PayloadBytes + (offsets.LongLength + entries.LongLength) * 4 + groups.LongLength * 12;

    internal const int DocumentBudget = 1024 * 1024;
    internal static int CountSegments(ValueArray<LocalBrushStroke>? strokes) =>
        strokes?.Sum(s => Math.Max(1, s.Points.Length - 1)) ?? 0;
    // One cell: segment data + owner/entry indices, stroke data + groups, three two-int
    // arrays, and the same conservative allowance for object headers and delegates as below.
    internal static long MinimumBudget(ValueArray<LocalBrushStroke>? strokes) =>
        CountSegments(strokes) * 48L + (strokes?.Length ?? 0) * 84L + 24 + 4096;

    internal LocalBrushEvaluator(ValueArray<LocalBrushStroke>? strokes, double width, double height,
        long budget = DocumentBudget)
    {
        data = new(strokes ?? [], width, height);
        if (data.Strokes.Length == 0)
        { offsets = [0, 0]; entries = []; groups = []; Columns = Rows = 1; cellSize = 1; return; }
        left = Math.Max(0, data.Strokes.Min(s => s.Left)); top = Math.Max(0, data.Strokes.Min(s => s.Top));
        right = Math.Min(data.FrameWidth, data.Strokes.Max(s => s.Right));
        bottom = Math.Min(data.FrameHeight, data.Strokes.Max(s => s.Bottom));
        cellSize = data.Strokes.Min(s => s.Radius) / 2;
        (Columns, Rows) = Dimensions();
        var minimumBudget = MinimumBudget(strokes);
        budget = Math.Max(budget, minimumBudget);
        // Bound scratch before allocating it so even the one-cell fallback fits this share.
        var cellBudget = Math.Min(Math.Max(4096, 4 * SegmentCount), 1 + (budget - minimumBudget) / 12);
        while ((long)Columns * Rows > cellBudget)
        {
            cellSize *= 2;
            (Columns, Rows) = Dimensions();
        }
        // Scratch is allocated once at the bounded initial size and reused on every retry.
        var counts = new int[Columns * Rows + 1];
        var cursor = new int[counts.Length];
        var owner = new int[SegmentCount];
        for (var s = 0; s < data.Strokes.Length; s++)
            Array.Fill(owner, s, data.Strokes[s].Start, data.Strokes[s].Count);
        int entryCount = 0, groupCount = 0;
        long fixedBytes = 0;
        bool Count(int cell, int segment)
        {
            counts[cell + 1]++; entryCount++;
            if (cursor[cell] != owner[segment]) { cursor[cell] = owner[segment]; groupCount++; }
            // Count exact groups before allocating entries; abort oversized candidates early.
            return Columns == 1 && Rows == 1 || fixedBytes + entryCount * 4L + groupCount * 12L <= budget;
        }
        Func<int, int, bool> count = Count;
        while (true)
        {
            Array.Clear(counts); Array.Fill(cursor, -1);
            entryCount = groupCount = 0;
            // Includes all scratch, final offsets, segment data, and 4 KiB for headers/delegates.
            fixedBytes = data.PayloadBytes +
                (counts.LongLength + cursor.LongLength + owner.LongLength + Columns * (long)Rows + 1) * 4 + 4096;
            if (VisitAll(count)) break;
            cellSize *= 2;
            (Columns, Rows) = Dimensions();
        }
        var cellCount = Columns * Rows;
        WorstSegmentsPerCell = counts.Max();
        for (var i = 1; i <= cellCount; i++) counts[i] += counts[i - 1];
        entries = new int[entryCount];
        Array.Copy(counts, cursor, cellCount + 1);
        VisitAll((cell, segment) => { entries[cursor[cell]++] = segment; return true; });
        offsets = new int[cellCount + 1];
        for (var cell = 0; cell < cellCount; cell++)
        {
            var previous = -1;
            for (var p = counts[cell]; p < counts[cell + 1]; p++)
                if (owner[entries[p]] != previous) { offsets[cell + 1]++; previous = owner[entries[p]]; }
        }
        for (var i = 1; i < offsets.Length; i++) offsets[i] += offsets[i - 1];
        groups = new Group[offsets[^1]];
        for (var cell = 0; cell < cellCount; cell++)
        {
            var group = offsets[cell]; var p = counts[cell];
            while (p < counts[cell + 1])
            {
                var start = p; var stroke = owner[entries[p++]];
                while (p < counts[cell + 1] && owner[entries[p]] == stroke) p++;
                groups[group++] = new(stroke, start, p);
            }
        }
    }

    private (int Columns, int Rows) Dimensions() => (
        Math.Max(1, (int)Math.Ceiling((right - left) / cellSize)),
        Math.Max(1, (int)Math.Ceiling((bottom - top) / cellSize)));

    private bool VisitAll(Func<int, int, bool> visit)
    {
        if (right < left || bottom < top) return true;
        foreach (var stroke in data.Strokes)
        for (var p = stroke.Start; p < stroke.Start + stroke.Count; p++)
        {
            var s = data.Segments[p]; var radius = stroke.Radius;
            var x0 = Math.Min(s.X, s.X + s.Dx) - radius; var x1 = Math.Max(s.X, s.X + s.Dx) + radius;
            var y0 = Math.Min(s.Y, s.Y + s.Dy) - radius; var y1 = Math.Max(s.Y, s.Y + s.Dy) + radius;
            if (x1 < left || y1 < top || x0 > right || y0 > bottom) continue;
            for (var y = CellY(y0); y <= CellY(y1); y++)
            for (var x = CellX(x0); x <= CellX(x1); x++)
                if (!visit(y * Columns + x, p)) return false;
        }
        return true;
    }
    private int CellX(double x) => Math.Clamp((int)Math.Floor((x - left) / cellSize), 0, Columns - 1);
    private int CellY(double y) => Math.Clamp((int)Math.Floor((y - top) / cellSize), 0, Rows - 1);

    internal IEnumerable<(double U, double V)> Boundaries()
    {
        for (var y = 0; y <= Rows; y++) for (var x = 0; x <= Columns; x++)
        {
            var u = (left + x * cellSize) / data.FrameWidth;
            var v = (top + y * cellSize) / data.FrameHeight;
            if (u >= 0 && u <= 1 && v >= 0 && v <= 1) yield return (u, v);
        }
    }

    internal double Weight(double u, double v)
    {
        var x = u * data.FrameWidth; var y = v * data.FrameHeight;
        if (x < left || x > right || y < top || y > bottom || groups.Length == 0) return 0;
        var cell = CellY(y) * Columns + CellX(x);
        double coverage = 0;
        for (var g = offsets[cell]; g < offsets[cell + 1]; g++)
        {
            var group = groups[g]; var stroke = data.Strokes[group.Stroke];
            if (!stroke.Contains(x, y)) continue;
            var minimum = stroke.Radius * stroke.Radius; var core = stroke.CoreSquared;
            for (var p = group.Start; p < group.End; p++)
            {
                minimum = Math.Min(minimum, data.Segments[entries[p]].DistanceSquared(x, y));
                if (minimum < stroke.Radius * stroke.Radius && minimum <= core) break;
            }
            var maximum = stroke.Weight(minimum);
            coverage = stroke.Erase ? coverage * (1 - maximum) : coverage + maximum * (1 - coverage);
        }
        return coverage;
    }

    private readonly record struct BrushSegment(double X, double Y, double Dx, double Dy, double InverseLength2)
    {
        internal double DistanceSquared(double x, double y)
        {
            var dx = x - X; var dy = y - Y;
            var t = Math.Clamp((dx * Dx + dy * Dy) * InverseLength2, 0, 1);
            dx -= t * Dx; dy -= t * Dy;
            return dx * dx + dy * dy;
        }
    }

    private sealed class SegmentData
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

        internal SegmentData(ValueArray<LocalBrushStroke> strokes, double width, double height)
        {
            var edge = (double)Math.Max(width, height);
            FrameWidth = width / edge; FrameHeight = height / edge;
            Segments = new BrushSegment[strokes.Sum(s => Math.Max(1, s.Points.Length - 1))];
            Strokes = new Stroke[strokes.Length];
            var next = 0;
            for (var s = 0; s < Strokes.Length; s++)
            {
                var stroke = strokes[s]; var start = next;
                double left = double.PositiveInfinity, top = left, right = double.NegativeInfinity, bottom = right;
                for (var p = 0; p < Math.Max(1, stroke.Points.Length - 1); p++)
                {
                    var a = stroke.Points[p]; var b = stroke.Points[Math.Min(p + 1, stroke.Points.Length - 1)];
                    var x = a.U / (double)LocalBrushPoint.Scale * FrameWidth; var y = a.V / (double)LocalBrushPoint.Scale * FrameHeight;
                    var dx = (b.U - (double)a.U) / LocalBrushPoint.Scale * FrameWidth; var dy = (b.V - (double)a.V) / LocalBrushPoint.Scale * FrameHeight;
                    var length2 = dx * dx + dy * dy;
                    Segments[next++] = new(x, y, dx, dy, length2 == 0 ? 0 : 1 / length2);
                    left = Math.Min(left, Math.Min(x, x + dx)); right = Math.Max(right, Math.Max(x, x + dx));
                    top = Math.Min(top, Math.Min(y, y + dy)); bottom = Math.Max(bottom, Math.Max(y, y + dy));
                }
                Strokes[s] = new(start, next - start, left - stroke.Radius, top - stroke.Radius,
                    right + stroke.Radius, bottom + stroke.Radius, stroke.Radius, stroke.Feather, stroke.Flow, stroke.Mode == "erase");
            }
        }
    }
}
