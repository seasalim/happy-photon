using HappyPhoton.Services;
using ImageMagick;

namespace HappyPhoton.Tests;

// Boundary footprint in radius units: square half-width, and its center's radial
// offset from the ring (0 = on the ring). Non-default values are diagnostics only.
internal readonly record struct HealBoundary(double HalfWidthInRadii, double OffsetInRadii)
{
    internal static HealBoundary Default => new(HealPrototype.BoundaryHalfWidthInRadii, HealPrototype.BoundaryOffsetInRadii);
}

// Test-only Q16 working-copy stage. All per-spot reads precede its first write.
internal sealed class HealPrototype
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
    internal long RetainedScratchBytes { get; private set; }
    private int sourceCapacity, destinationCapacity, prefixCapacity;

    internal BaseImage Repair(BaseImage basis, IReadOnlyList<HealSpot> spots, HealCandidate candidate, int workers = 2)
    {
        var copy = new MagickImage(basis.Pixels);
        try { Apply(copy, spots, candidate, workers); return new(copy, basis.Info); }
        catch { copy.Dispose(); throw; }
    }

    internal void Apply(MagickImage image, IReadOnlyList<HealSpot> spots, HealCandidate candidate, int workers,
        HealBoundary? boundaryFootprint = null)
    {
        var footprint = boundaryFootprint ?? HealBoundary.Default;
        if (spots.Count == 0) return;
        using var pixels = image.GetPixelsUnsafe();
        if (pixels.Channels != 3) throw new InvalidOperationException("HEAL requires canonical RGB Q16");
        var width = (int)image.Width; var height = (int)image.Height;
        foreach (var spot in spots)
        {
            var radius = Math.Min(spot.Radius * Math.Max(width, height), Math.Min(width, height) / 2d);
            var cx = spot.U * width - .5; var cy = spot.V * height - .5;
            var sx = Math.Clamp(spot.Su * width, radius, width - radius) - .5;
            var sy = Math.Clamp(spot.Sv * height, radius, height - radius) - .5;
            var halfWidth = spot.IsClone ? 0 : radius * footprint.HalfWidthInRadii;
            var ringRadius = radius * (1 + (spot.IsClone ? 0 : footprint.OffsetInRadii));
            var reach = Math.Max(radius, ringRadius) + halfWidth + 1;
            var dest = Box.Around(cx, cy, reach, width, height);
            var src = Box.Around(sx, sy, reach, width, height);
            var source = sourceScratch.Take(src.Count);
            var target = destinationScratch.Take(dest.Count);
            var sourcePrefix = spot.IsClone ? [] : sourcePrefixScratch.Take(src.Count);
            var targetPrefix = spot.IsClone ? [] : destinationPrefixScratch.Take(dest.Count);
            sourceCapacity = Math.Max(sourceCapacity, source.Length);
            destinationCapacity = Math.Max(destinationCapacity, target.Length);
            prefixCapacity = Math.Max(prefixCapacity, sourcePrefix.Length + targetPrefix.Length);
            RetainedScratchBytes = 2L * (sourceCapacity + destinationCapacity) + 8L * prefixCapacity;
            try
            {
                pixels.GetReadOnlyArea(src.X, src.Y, (uint)src.W, (uint)src.H).CopyTo(source);
                pixels.GetReadOnlyArea(dest.X, dest.Y, (uint)dest.W, (uint)dest.H).CopyTo(target);
                var boundary = new double[RingSamples * 3];
                if (!spot.IsClone)
                {
                    var targetRows = new HealBoundaryRows(target, dest.W, dest.H, targetPrefix, workers);
                    var sourceRows = new HealBoundaryRows(source, src.W, src.H, sourcePrefix, workers);
                    for (var k = 0; k < RingSamples; k++)
                    for (var c = 0; c < 3; c++)
                    {
                        var d = targetRows.Mean(cx + ringRadius * Ring[k].X - dest.X, cy + ringRadius * Ring[k].Y - dest.Y, halfWidth, c);
                        var s = sourceRows.Mean(sx + ringRadius * Ring[k].X - src.X, sy + ringRadius * Ring[k].Y - src.Y, halfWidth, c);
                        boundary[k * 3 + c] = candidate.Domain == HealDomain.Additive ? d - s :
                            Math.Log((d + 1d / 65535) / (s + 1d / 65535));
                    }
                }
                Parallel.For(0, dest.H, new ParallelOptions { MaxDegreeOfParallelism = workers }, row =>
                {
                    for (var col = 0; col < dest.W; col++)
                    {
                        var dx = (dest.X + col - cx) / radius; var dy = (dest.Y + row - cy) / radius;
                        var rho = Math.Sqrt(dx * dx + dy * dy);
                        var ring = ringRadius / radius;
                        if (rho >= 1) continue;
                        var weight = Weight(rho, spot.Feather) * spot.Opacity;
                        double a = 0, b = 0, c = 0, sum = 0;
                        if (!spot.IsClone)
                            for (var k = 0; k < RingSamples; k++)
                            {
                                // Interpolate from the circle the samples were actually taken on.
                                var distance = (dx - ring * Ring[k].X) * (dx - ring * Ring[k].X) + (dy - ring * Ring[k].Y) * (dy - ring * Ring[k].Y);
                                // Common Poisson numerator cancels on normalization. Gaussian sigma = r/2.
                                var w = candidate.Formulation == HealFormulation.Membrane ? 1 / Math.Max(1e-24, distance) : Math.Exp(-2 * distance);
                                sum += w; a += w * boundary[k * 3]; b += w * boundary[k * 3 + 1]; c += w * boundary[k * 3 + 2];
                            }
                        for (var channel = 0; channel < 3; channel++)
                        {
                            var s = Sample(source, src, sx + dx * radius, sy + dy * radius, channel);
                            var correction = spot.IsClone ? 0 : (channel == 0 ? a : channel == 1 ? b : c) / sum;
                            if (!spot.IsClone && candidate.Domain == HealDomain.Ratio)
                                correction = (s + 1d / 65535) * (Math.Exp(correction) - 1);
                            var repaired = s + Attenuate(s, correction);
                            var offset = (row * dest.W + col) * 3 + channel;
                            var original = target[offset] / 65535d;
                            target[offset] = checked((ushort)Math.Round((original + weight * (repaired - original)) * 65535));
                        }
                    }
                });
                image.ImportPixels(target.AsSpan(0, dest.Count), new PixelImportSettings(
                    dest.X, dest.Y, (uint)dest.W, (uint)dest.H, StorageType.Quantum, PixelMapping.RGB));
            }
            finally
            {
                sourceScratch.Return(source); destinationScratch.Return(target);
                if (!spot.IsClone) { sourcePrefixScratch.Return(sourcePrefix); destinationPrefixScratch.Return(targetPrefix); }
            }
        }
    }

    // Convenience for tests: exact footprint mean of a whole buffer (see HealBoundaryRows).
    internal static double BoundaryMean(ushort[] pixels, int width, int height,
        double x, double y, double halfWidth, int channel) =>
        new HealBoundaryRows(pixels, width, height, new double[width * height * 3], 1).Mean(x, y, halfWidth, channel);

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
        x = Math.Clamp(x - box.X, 0, box.W - 1); y = Math.Clamp(y - box.Y, 0, box.H - 1);
        var ix = (int)x; var iy = (int)y; var fx = x - ix; var fy = y - iy;
        var right = Math.Min(ix + 1, box.W - 1); var bottom = Math.Min(iy + 1, box.H - 1);
        var top = pixels[(iy * box.W + ix) * 3 + c] * (1 - fx) + pixels[(iy * box.W + right) * 3 + c] * fx;
        var low = pixels[(bottom * box.W + ix) * 3 + c] * (1 - fx) + pixels[(bottom * box.W + right) * 3 + c] * fx;
        return (top * (1 - fy) + low * fy) / 65535;
    }

    private readonly record struct Box(int X, int Y, int W, int H)
    {
        internal int Count => W * H * 3;
        internal static Box Around(double x, double y, double r, int width, int height)
        {
            var left = Math.Max(0, (int)Math.Floor(x - r)); var top = Math.Max(0, (int)Math.Floor(y - r));
            return new(left, top, Math.Min(width - 1, (int)Math.Ceiling(x + r)) - left + 1,
                Math.Min(height - 1, (int)Math.Ceiling(y + r)) - top + 1);
        }
    }
}

