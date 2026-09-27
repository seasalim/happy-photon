using HappyPhoton.Models;
using ImageMagick;

namespace HappyPhoton.Services;

// Sequential, linear Rec.2020 repairs on the render-owned working copy.
internal sealed class RenderRepairs
{
    internal const int RingSamples = 32;

    // Smallest contract radius at 1600 is 3.2px: r/3 exceeds one pixel.
    // The square stays within sqrt(2)/3 of its ring point in radius units.
    internal const double BoundaryHalfWidthInRadii = 1d / 3;

    // Smallest offset keeping every axis-aligned square outside the disc (worst case at 45 degrees),
    // so a blemish filling its spot never enters the destination's boundary estimate.
    internal static readonly double BoundaryOffsetInRadii = Math.Sqrt(2) / 3;

    private readonly RenderScratchSlot<ushort> sourceScratch = new();

    private readonly RenderScratchSlot<ushort> destinationScratch = new();

    private readonly RenderScratchSlot<double> sourcePrefixScratch = new();

    private readonly RenderScratchSlot<double> destinationPrefixScratch = new();

    private static readonly (double X, double Y)[] Ring = Enumerable.Range(0, RingSamples)
        .Select(i => (Math.Cos(2 * Math.PI * i / RingSamples), Math.Sin(2 * Math.PI * i / RingSamples))).ToArray();

    private int sourceCapacity;

    private int destinationCapacity;

    private int prefixCapacity;

    internal long RetainedScratchBytes =>
        2L * (Volatile.Read(ref sourceCapacity) + Volatile.Read(ref destinationCapacity)) +
        8L * Volatile.Read(ref prefixCapacity);

    internal bool Apply(MagickImage image, IReadOnlyList<Repair>? spots,
        RenderExecutionOptions? execution = null)
    {
        if (spots is not { Count: > 0 }) return false;

        execution?.ThrowIfCancellationRequested();
        execution?.ReportStage("repairs");
        var probe = RenderStageProbe.Begin();
        using var pixels = image.GetPixelsUnsafe();
        var layout = RenderKernelSupport.GetLayout(pixels);
        var channels = new[] { layout.Red, layout.Green, layout.Blue };
        var width = (int)image.Width;
        var height = (int)image.Height;

        foreach (var spot in spots)
        {
            execution?.ThrowIfCancellationRequested();

            var isClone = spot.Type == "clone";
            var radius = RepairGeometry.EffectiveRadius(spot.Radius, width, height);
            var (su, sv) = RepairGeometry.ClampSource(spot, width, height);
            var cx = BaseFrameMapping.ToPixel(spot.U, width);
            var cy = BaseFrameMapping.ToPixel(spot.V, height);
            var sx = BaseFrameMapping.ToPixel(su, width);
            var sy = BaseFrameMapping.ToPixel(sv, height);
            var halfWidth = isClone ? 0 : radius * BoundaryHalfWidthInRadii;
            var ringRadius = radius * (1 + (isClone ? 0 : BoundaryOffsetInRadii));
            var reach = Math.Max(radius, ringRadius) + halfWidth + 1;
            var dest = Box.Around(cx, cy, reach, width, height, layout.Channels);
            var src = Box.Around(sx, sy, reach, width, height, layout.Channels);

            var source = sourceScratch.Take(src.Count);
            var target = destinationScratch.Take(dest.Count);
            var sourcePrefix = isClone ? [] : sourcePrefixScratch.Take(src.Count);
            var targetPrefix = isClone ? [] : destinationPrefixScratch.Take(dest.Count);
            RecordCapacity(ref sourceCapacity, source.Length);
            RecordCapacity(ref destinationCapacity, target.Length);
            RecordCapacity(ref prefixCapacity, sourcePrefix.Length + targetPrefix.Length);

            try
            {
                pixels.GetReadOnlyArea(src.X, src.Y, (uint)src.W, (uint)src.H).CopyTo(source);
                pixels.GetReadOnlyArea(dest.X, dest.Y, (uint)dest.W, (uint)dest.H).CopyTo(target);

                var boundary = new double[RingSamples * 3];
                if (!isClone)
                {
                    var targetRows = new RepairBoundaryRows(target, dest.W, dest.H, layout.Channels, targetPrefix, execution);
                    var sourceRows = new RepairBoundaryRows(source, src.W, src.H, layout.Channels, sourcePrefix, execution);
                    for (var k = 0; k < RingSamples; k++)
                    for (var c = 0; c < 3; c++)
                    {
                        var d = targetRows.Mean(cx + ringRadius * Ring[k].X - dest.X, cy + ringRadius * Ring[k].Y - dest.Y, halfWidth, channels[c]);
                        var s = sourceRows.Mean(sx + ringRadius * Ring[k].X - src.X, sy + ringRadius * Ring[k].Y - src.Y, halfWidth, channels[c]);
                        boundary[k * 3 + c] = d - s;
                    }
                }

                var disc = Box.Around(cx, cy, radius, width, height, layout.Channels);
                var discPixels = Math.Min((double)disc.W * disc.H, Math.PI * radius * radius);
                var work = (long)Math.Ceiling(discPixels * (isClone ? 1 : RingSamples));
                ForRows(disc.H, work, execution, discRow =>
                {
                    var row = disc.Y + discRow - dest.Y;
                    for (var discCol = 0; discCol < disc.W; discCol++)
                    {
                        var col = disc.X + discCol - dest.X;
                        var dx = (dest.X + col - cx) / radius;
                        var dy = (dest.Y + row - cy) / radius;
                        var rho = Math.Sqrt(dx * dx + dy * dy);
                        var ring = ringRadius / radius;
                        if (rho >= 1) continue;

                        var weight = Weight(rho, spot.Feather) * spot.Opacity;
                        double a = 0;
                        double b = 0;
                        double c = 0;
                        double sum = 0;
                        if (!isClone)
                            for (var k = 0; k < RingSamples; k++)
                            {
                                // Interpolate from the circle the samples were actually taken on.
                                var distance = (dx - ring * Ring[k].X) * (dx - ring * Ring[k].X) + (dy - ring * Ring[k].Y) * (dy - ring * Ring[k].Y);
                                // The common Poisson numerator cancels on normalization.
                                var w = 1 / Math.Max(1e-24, distance);
                                sum += w;
                                a += w * boundary[k * 3];
                                b += w * boundary[k * 3 + 1];
                                c += w * boundary[k * 3 + 2];
                            }

                        for (var channel = 0; channel < 3; channel++)
                        {
                            var s = Sample(source, src, sx + dx * radius, sy + dy * radius, channels[channel]);
                            var correction = isClone ? 0 : (channel == 0 ? a : channel == 1 ? b : c) / sum;
                            var repaired = s + Attenuate(s, correction);
                            var offset = (row * dest.W + col) * layout.Channels + channels[channel];
                            var original = target[offset] / 65535d;
                            target[offset] = checked((ushort)Math.Round((original + weight * (repaired - original)) * 65535));
                        }
                    }
                });

                execution?.ThrowIfCancellationRequested();

                // Import detaches Magick's lazy clone without losing earlier spot writes.
                image.ImportPixels(target.AsSpan(0, dest.Count), new PixelImportSettings(
                    dest.X, dest.Y, (uint)dest.W, (uint)dest.H, StorageType.Quantum,
                    layout.Channels == 4 ? PixelMapping.RGBA : PixelMapping.RGB));
            }
            finally
            {
                sourceScratch.Return(source);
                destinationScratch.Return(target);
                if (!isClone)
                {
                    sourcePrefixScratch.Return(sourcePrefix);
                    destinationPrefixScratch.Return(targetPrefix);
                }
            }
        }

        execution?.ThrowIfCancellationRequested();
        RenderStageProbe.End(probe, "repairs", image);
        return true;
    }

    internal static void ForRows(int height, long work, RenderExecutionOptions? execution, Action<int> rowAction)
    {
        var workers = (int)Math.Min(Math.Min(Environment.ProcessorCount, height), Math.Max(1, (work + 32767) / 32768));
        workers = execution?.CapWorkers(workers) ?? workers;
        if (workers == 1)
        {
            for (var row = 0; row < height; row++)
            {
                execution?.ThrowIfCancellationRequested();
                rowAction(row);
            }
            return;
        }

        var options = execution?.ParallelOptions ?? new ParallelOptions { MaxDegreeOfParallelism = workers };
        Parallel.For(0, workers, options, worker =>
        {
            var end = height * (worker + 1) / workers;
            for (var row = height * worker / workers; row < end; row++)
            {
                execution?.ThrowIfCancellationRequested();
                rowAction(row);
            }
        });
    }

    private static void RecordCapacity(ref int capacity, int length)
    {
        var current = Volatile.Read(ref capacity);
        while (current < length)
        {
            var previous = Interlocked.CompareExchange(ref capacity, length, current);
            if (previous == current) return;
            current = previous;
        }
    }

    internal const double QuantumReserve = .51 / 65535;

    internal static double Attenuate(double source, double correction)
    {
        // Headroom less just over half a code, so a result can never round onto the range limit
        // (or onto 0) unless the source already sits there.
        var room = Math.Max(0, (correction >= 0 ? 1 - source : source) - QuantumReserve);
        var magnitude = Math.Abs(correction);
        var knee = 7d / 8 * room;
        if (magnitude <= knee) return correction;

        // C1 shoulder: reserve >= 3/32 of source headroom, preserving Q16 detail.
        // At fixed correction the output's source derivative is >= 3/32.
        // The 1/32-wide transition joins identity at 7/8 and approaches 29/32.
        var shoulder = room / 32;
        return Math.CopySign(knee + shoulder * (1 - shoulder / (magnitude - knee + shoulder)), correction);
    }

    internal static double Weight(double rho, double feather)
    {
        if (rho >= 1) return 0;
        if (rho <= 1 - feather) return 1;

        var t = (rho - 1 + feather) / feather;
        return 1 - t * t * (3 - 2 * t);
    }

    private static double Sample(ushort[] pixels, Box box, double x, double y, int c)
    {
        x = Math.Clamp(x - box.X, 0, box.W - 1);
        y = Math.Clamp(y - box.Y, 0, box.H - 1);

        var ix = (int)x;
        var iy = (int)y;
        var fx = x - ix;
        var fy = y - iy;
        var right = Math.Min(ix + 1, box.W - 1);
        var bottom = Math.Min(iy + 1, box.H - 1);
        var top = pixels[(iy * box.W + ix) * box.Channels + c] * (1 - fx) + pixels[(iy * box.W + right) * box.Channels + c] * fx;
        var low = pixels[(bottom * box.W + ix) * box.Channels + c] * (1 - fx) + pixels[(bottom * box.W + right) * box.Channels + c] * fx;
        return (top * (1 - fy) + low * fy) / 65535;
    }

    private readonly record struct Box(int X, int Y, int W, int H, int Channels)
    {
        internal int Count => checked(W * H * Channels);

        internal static Box Around(double x, double y, double r, int width, int height, int channels)
        {
            var left = Math.Max(0, (int)Math.Floor(x - r));
            var top = Math.Max(0, (int)Math.Floor(y - r));
            return new(left, top, Math.Min(width - 1, (int)Math.Ceiling(x + r)) - left + 1,
                Math.Min(height - 1, (int)Math.Ceiling(y + r)) - top + 1, channels);
        }
    }
}

