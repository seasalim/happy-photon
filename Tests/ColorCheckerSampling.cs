using ImageMagick;

namespace HappyPhoton.Tests;

internal readonly record struct ColorCheckerPatchSample(
    double[] Xyz,
    int PixelCount,
    bool ContainsClippedSample);

internal static class ColorCheckerSampling
{
    public static ColorCheckerPatchSample[] SampleXyz(
        MagickImage image,
        ColorCheckerGeometry geometry,
        double[,] rgbToXyz,
        bool decodeSrgb)
    {
        using var pixels = image.GetPixels();
        var values = pixels.ToShortArray(PixelMapping.RGB)
            ?? throw new InvalidOperationException("Could not read ColorChecker pixels.");
        var result = new ColorCheckerPatchSample[24];
        for (var row = 0; row < geometry.Rows; row++)
        for (var column = 0; column < geometry.Columns; column++)
        {
            var polygon = CreatePatchPolygon(geometry, row, column);
            var sums = new double[3];
            var count = 0;
            var clipped = false;
            var minY = Math.Max(0, (int)Math.Floor(polygon.Min(point => point.Y)));
            var maxY = Math.Min((int)image.Height - 1,
                (int)Math.Ceiling(polygon.Max(point => point.Y)));
            var minX = Math.Max(0, (int)Math.Floor(polygon.Min(point => point.X)));
            var maxX = Math.Min((int)image.Width - 1,
                (int)Math.Ceiling(polygon.Max(point => point.X)));
            for (var y = minY; y <= maxY; y++)
            for (var x = minX; x <= maxX; x++)
            {
                if (!Contains(polygon, x + 0.5, y + 0.5)) continue;
                var offset = ((int)image.Width * y + x) * 3;
                var rgb = new double[3];
                for (var channel = 0; channel < 3; channel++)
                {
                    var sample = values[offset + channel];
                    clipped |= sample is 0 or ushort.MaxValue;
                    var value = sample / (double)ushort.MaxValue;
                    rgb[channel] = decodeSrgb ? DecodeSrgb(value) : value;
                }
                var xyz = PrecisionColorCases.Transform(rgbToXyz, rgb);
                for (var channel = 0; channel < 3; channel++)
                {
                    sums[channel] += xyz[channel];
                }
                count++;
            }

            if (count == 0)
            {
                throw new InvalidOperationException(
                    $"ColorChecker ROI row {row}, column {column} contained no pixels.");
            }
            var patch = geometry.PatchIndexByImageCell[row][column];
            result[patch] = new ColorCheckerPatchSample(
                sums.Select(value => value / count).ToArray(),
                count,
                clipped);
        }
        return result;
    }

    internal static MagickGeometry GetPatchBounds(
        ColorCheckerGeometry geometry,
        int patchIndex,
        double scale)
    {
        if (scale <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(scale));
        }
        for (var row = 0; row < geometry.Rows; row++)
        for (var column = 0; column < geometry.Columns; column++)
        {
            if (geometry.PatchIndexByImageCell[row][column] != patchIndex)
            {
                continue;
            }
            var polygon = CreatePatchPolygon(geometry, row, column, 0.08);
            var x = (int)Math.Floor(polygon.Min(point => point.X) * scale);
            var y = (int)Math.Floor(polygon.Min(point => point.Y) * scale);
            var right = (int)Math.Ceiling(polygon.Max(point => point.X) * scale);
            var bottom = (int)Math.Ceiling(polygon.Max(point => point.Y) * scale);
            return new MagickGeometry(x, y, (uint)(right - x), (uint)(bottom - y));
        }
        throw new ArgumentOutOfRangeException(nameof(patchIndex));
    }

    private static ColorCheckerPoint[] CreatePatchPolygon(
        ColorCheckerGeometry geometry,
        int row,
        int column) =>
        CreatePatchPolygon(
            geometry,
            row,
            column,
            geometry.CentralInsetFraction);

    private static ColorCheckerPoint[] CreatePatchPolygon(
        ColorCheckerGeometry geometry,
        int row,
        int column,
        double inset)
    {
        return
        [
            Project(geometry, (column + inset) / geometry.Columns,
                (row + inset) / geometry.Rows),
            Project(geometry, (column + 1 - inset) / geometry.Columns,
                (row + inset) / geometry.Rows),
            Project(geometry, (column + 1 - inset) / geometry.Columns,
                (row + 1 - inset) / geometry.Rows),
            Project(geometry, (column + inset) / geometry.Columns,
                (row + 1 - inset) / geometry.Rows)
        ];
    }

    private static ColorCheckerPoint Project(
        ColorCheckerGeometry geometry,
        double u,
        double v)
    {
        var p00 = geometry.CornersClockwiseFromTopLeft[0];
        var p10 = geometry.CornersClockwiseFromTopLeft[1];
        var p11 = geometry.CornersClockwiseFromTopLeft[2];
        var p01 = geometry.CornersClockwiseFromTopLeft[3];
        var dx1 = p10.X - p11.X;
        var dx2 = p01.X - p11.X;
        var dx3 = p00.X - p10.X + p11.X - p01.X;
        var dy1 = p10.Y - p11.Y;
        var dy2 = p01.Y - p11.Y;
        var dy3 = p00.Y - p10.Y + p11.Y - p01.Y;
        var denominator = dx1 * dy2 - dx2 * dy1;
        var projectiveX = (dx3 * dy2 - dx2 * dy3) / denominator;
        var projectiveY = (dx1 * dy3 - dx3 * dy1) / denominator;
        var a = p10.X - p00.X + projectiveX * p10.X;
        var b = p01.X - p00.X + projectiveY * p01.X;
        var d = p10.Y - p00.Y + projectiveX * p10.Y;
        var e = p01.Y - p00.Y + projectiveY * p01.Y;
        var divisor = projectiveX * u + projectiveY * v + 1;
        return new ColorCheckerPoint(
            (a * u + b * v + p00.X) / divisor,
            (d * u + e * v + p00.Y) / divisor);
    }

    private static bool Contains(ColorCheckerPoint[] polygon, double x, double y)
    {
        var positive = false;
        var negative = false;
        for (var index = 0; index < polygon.Length; index++)
        {
            var start = polygon[index];
            var end = polygon[(index + 1) % polygon.Length];
            var cross = (end.X - start.X) * (y - start.Y) -
                (end.Y - start.Y) * (x - start.X);
            positive |= cross > 0;
            negative |= cross < 0;
        }
        return !(positive && negative);
    }

    private static double DecodeSrgb(double value) => value <= 0.04045
        ? value / 12.92
        : Math.Pow((value + 0.055) / 1.055, 2.4);
}
