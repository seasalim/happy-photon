using HappyPhoton.Models;

namespace HappyPhoton.Services;

// Cell-centred area averages with a fixed scan order, independent of worker assignment.
internal sealed record PresenceGrid(int Width, int Height, double[] Values)
{
    private double BaseLongEdge { get; init; } = Math.Max(Width, Height);

    internal int GuidedRadius => Math.Max(1, (int)Math.Ceiling(.010 * BaseLongEdge * Math.Sqrt(3)));

    private double SampleWidth { get; init; } = Width;

    private double SampleHeight { get; init; } = Height;

    private double OffsetX { get; init; }

    private double OffsetY { get; init; }

    internal static PresenceGrid Reduce(int width, int height, Func<int, double> value,
        RenderExecutionOptions? execution, LocalsFrame? frame = null)
    {
        var crop = frame ?? new LocalsFrame(width, height, 0, 0, 1, 1);
        // Normalized crop coordinates survive resting's geometry preparation and resize.
        var baseWidth = width / crop.CropWidth;
        var baseHeight = height / crop.CropHeight;
        // The warp crops at source pixel scale; its corrected extent only anchors the grid.
        var baseLongEdge = Math.Max(baseWidth, baseHeight) * (crop.BaseLongEdge / crop.LongEdge);
        var scale = Math.Min(1, 256d / baseLongEdge);
        var baseW = Math.Max(1, (int)Math.Round(baseWidth * scale));
        var baseH = Math.Max(1, (int)Math.Round(baseHeight * scale));
        var startX = Math.Clamp((int)Math.Floor(crop.CropX * baseW), 0, baseW - 1);
        var startY = Math.Clamp((int)Math.Floor(crop.CropY * baseH), 0, baseH - 1);
        var w = Math.Clamp((int)Math.Ceiling((crop.CropX + crop.CropWidth) * baseW), startX + 1, baseW) - startX;
        var h = Math.Clamp((int)Math.Ceiling((crop.CropY + crop.CropHeight) * baseH), startY + 1, baseH) - startY;
        var result = new double[w * h];
        RenderPresence.Rows(h, 32, execution, cy =>
        {
            var top = (cy + startY) * baseHeight / baseH - crop.CropY * baseHeight;
            var bottom = (cy + startY + 1) * baseHeight / baseH - crop.CropY * baseHeight;

            for (var cx = 0; cx < w; cx++)
            {
                var left = (cx + startX) * baseWidth / baseW - crop.CropX * baseWidth;
                var right = (cx + startX + 1) * baseWidth / baseW - crop.CropX * baseWidth;
                double sum = 0;

                for (var y = (int)Math.Floor(top); y < Math.Ceiling(bottom); y++)
                for (var x = (int)Math.Floor(left); x < Math.Ceiling(right); x++)
                {
                    // Extend crop-edge pixels across partial boundary cells.
                    sum += value(Math.Clamp(y, 0, height - 1) * width + Math.Clamp(x, 0, width - 1)) *
                        (Math.Min(y + 1, bottom) - Math.Max(y, top)) *
                        (Math.Min(x + 1, right) - Math.Max(x, left));
                }

                result[cy * w + cx] = sum / ((right - left) * (bottom - top));
            }
        });

        return new(w, h, result)
        {
            BaseLongEdge = Math.Min(256, baseLongEdge),
            SampleWidth = crop.CropWidth * baseW,
            SampleHeight = crop.CropHeight * baseH,
            OffsetX = crop.CropX * baseW - startX,
            OffsetY = crop.CropY * baseH - startY
        };
    }

    internal (PresenceGrid A, PresenceGrid B) GuidedBase(RenderExecutionOptions? execution)
    {
        var radius = GuidedRadius;
        var mean = Blur(radius, execution);
        var square = (this with { Values = Values.Select(v => v * v).ToArray() }).Blur(radius, execution);
        var a = new double[Values.Length];
        var b = new double[a.Length];

        for (var i = 0; i < a.Length; i++)
        {
            var variance = Math.Max(0, square.Values[i] - mean.Values[i] * mean.Values[i]);
            a[i] = variance / (variance + .0025);
            b[i] = mean.Values[i] * (1 - a[i]);
        }

        return ((this with { Values = a }).Blur(radius, execution),
            (this with { Values = b }).Blur(radius, execution));
    }

    internal double Sample(double u, double v)
    {
        var x = Math.Clamp(u * SampleWidth + OffsetX - .5, 0, Width - 1);
        var y = Math.Clamp(v * SampleHeight + OffsetY - .5, 0, Height - 1);
        var ix = (int)x;
        var iy = (int)y;
        var nx = Math.Min(ix + 1, Width - 1);
        var ny = Math.Min(iy + 1, Height - 1);
        var a = Values[iy * Width + ix] * (1 - (x - ix)) + Values[iy * Width + nx] * (x - ix);
        var b = Values[ny * Width + ix] * (1 - (x - ix)) + Values[ny * Width + nx] * (x - ix);

        return a * (1 - (y - iy)) + b * (y - iy);
    }

    private PresenceGrid Blur(int radius, RenderExecutionOptions? execution)
    {
        var weight = 1d / (2 * radius + 1);
        var temp = new double[Values.Length];
        var result = new double[Values.Length];
        RenderPresence.Rows(Height, 32, execution, y =>
        {
            for (var x = 0; x < Width; x++)
            for (var k = -radius; k <= radius; k++)
            {
                temp[y * Width + x] += weight * Values[y * Width + Math.Clamp(x + k, 0, Width - 1)];
            }
        });
        RenderPresence.Rows(Height, 32, execution, y =>
        {
            for (var x = 0; x < Width; x++)
            for (var k = -radius; k <= radius; k++)
            {
                result[y * Width + x] += weight * temp[Math.Clamp(y + k, 0, Height - 1) * Width + x];
            }
        });

        return this with { Values = result };
    }
}
