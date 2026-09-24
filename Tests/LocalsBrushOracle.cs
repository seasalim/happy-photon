namespace HappyPhoton.Tests;

// Independent double definition: no production geometry, brush, or persistence calls.
internal static class LocalsBrushOracle
{
    internal const int Quantization = 16384, StrokeCap = 96, PointCap = 4000;

    internal static double Weight(BrushDocument document, double u, double v, int width, int height)
    {
        var edge = (double)Math.Max(width, height);
        double coverage = 0;
        foreach (var stroke in document.Strokes)
        {
            double maximum = 0;
            for (var i = 0; i < stroke.Points.Length; i++)
            {
                var a = stroke.Points[i];
                var b = stroke.Points[Math.Min(i + 1, stroke.Points.Length - 1)];
                var ax = a.U * width / edge; var ay = a.V * height / edge;
                var bx = b.U * width / edge; var by = b.V * height / edge;
                var x = u * width / edge; var y = v * height / edge;
                var dx = bx - ax; var dy = by - ay;
                var length2 = dx * dx + dy * dy;
                var t = length2 == 0 ? 0 : Math.Clamp(((x - ax) * dx + (y - ay) * dy) / length2, 0, 1);
                var distance = Math.Sqrt(Math.Pow(x - ax - t * dx, 2) + Math.Pow(y - ay - t * dy, 2));
                var weight = distance >= stroke.Radius ? 0 : stroke.Feather == 0 ? 1 :
                    Smooth((stroke.Radius - distance) / (stroke.Radius * stroke.Feather));
                maximum = Math.Max(maximum, weight * stroke.Flow);
            }
            coverage = stroke.Erase ? coverage * (1 - maximum) : coverage + (1 - coverage) * maximum;
        }
        return coverage;
    }

    internal static double RangedWeight(BrushDocument document, double u, double v, int width, int height,
        double r, double g, double b, LocalsRangeOracle.LightWindow light, LocalsRangeOracle.HueWindow hue)
    {
        var lab = LocalsRangeOracle.Classify(r, g, b);
        return Weight(document, u, v, width, height) * light.Weight(lab.L) *
            hue.Weight(lab.Hue) * LocalsRangeOracle.Reliability(lab.C);
    }

    private static double Smooth(double value)
    {
        var t = Math.Clamp(value, 0, 1);
        return t * t * (3 - 2 * t);
    }

    internal sealed record StoredStroke(int[] Points, double Radius, double Feather, double Flow, bool Erase);

    // First pair absolute, following pairs delta from the previous quantized point.
    // Quantize each axis in image-normalized coordinates, before converting to the long-edge metric.
    internal static StoredStroke[] Encode(BrushDocument document)
    {
        ValidateDocuments([document]);
        return document.Strokes.Select(stroke =>
        {
            if (stroke.Points.Length is < 1 or > PointCap) throw new ArgumentOutOfRangeException(nameof(document));
            var encoded = new int[stroke.Points.Length * 2];
            int previousU = 0, previousV = 0;
            for (var i = 0; i < stroke.Points.Length; i++)
            {
                var u = Quantize(stroke.Points[i].U); var v = Quantize(stroke.Points[i].V);
                encoded[2 * i] = u - previousU; encoded[2 * i + 1] = v - previousV;
                previousU = u; previousV = v;
            }
            return new StoredStroke(encoded, Math.Clamp(Finite(stroke.Radius), .001, .25), Math.Clamp(Finite(stroke.Feather), 0, 1),
                Math.Clamp(Finite(stroke.Flow), .05, 1), stroke.Erase);
        }).ToArray();
    }

    internal static BrushDocument Decode(StoredStroke[] strokes)
    {
        if (strokes.Length > StrokeCap || strokes.Sum(s => (long)s.Points.Length) > PointCap * 2)
            throw new ArgumentOutOfRangeException(nameof(strokes));
        return new(strokes.Select(stroke =>
        {
            if (stroke.Points.Length < 2 || stroke.Points.Length > PointCap * 2 || stroke.Points.Length % 2 != 0)
                throw new ArgumentOutOfRangeException(nameof(strokes));
            var points = new BrushPoint[stroke.Points.Length / 2];
            long u = 0, v = 0;
            for (var i = 0; i < points.Length; i++)
            {
                u += stroke.Points[2 * i]; v += stroke.Points[2 * i + 1];
                points[i] = new(Math.Clamp(u / (double)Quantization, -1, 2),
                    Math.Clamp(v / (double)Quantization, -1, 2));
            }
            return new BrushStroke(points, Math.Clamp(Finite(stroke.Radius), .001, .25), Math.Clamp(Finite(stroke.Feather), 0, 1),
                Math.Clamp(Finite(stroke.Flow), .05, 1), stroke.Erase);
        }).ToArray());
    }

    // Limits apply across all brush locals, not independently to each local.
    internal static void ValidateDocuments(BrushDocument[] documents)
    {
        if (documents.Sum(d => (long)d.Strokes.Length) > StrokeCap ||
            documents.Sum(d => d.Strokes.Sum(s => (long)s.Points.Length)) > PointCap)
            throw new ArgumentOutOfRangeException(nameof(documents));
    }

    private static int Quantize(double value) => (int)Math.Round(Math.Clamp(Finite(value), -1, 2) * Quantization,
        MidpointRounding.AwayFromZero);
    private static double Finite(double value) => double.IsFinite(value) ? value : throw new ArgumentOutOfRangeException(nameof(value));
}
