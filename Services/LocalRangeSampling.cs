using Avalonia;
using HappyPhoton.Models;
using ImageMagick;
using System.Runtime.CompilerServices;

namespace HappyPhoton.Services;

internal static class LocalRangeSampling
{
    internal static MagickImage Prepare(BaseImage basis, EditSettings settings, PixelSize size, out RenderGeometryTrace trace,
        out AgxCrossing.Matrix3x3 wb, out DcpHueSatMap? map)
    {
        wb = new(RenderChromaticStage.CreateWhiteBalanceMatrix(basis.Info, settings));
        map = basis.Info.IsRawSource && !basis.Info.IsMonochrome ? basis.Info.DcpProfile?.HueSatMap : null;
        if (map != null) map = DcpHueSatRenderer.Prepare(map);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(size.Width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(size.Height);
        var geometry = RenderGeometry.Apply(basis.Pixels, settings, out trace);
        try
        {
            if (geometry.Width != size.Width || geometry.Height != size.Height)
                geometry.Resize(new MagickGeometry((uint)size.Width, (uint)size.Height) { IgnoreAspectRatio = true });
            return geometry;
        }
        catch { geometry.Dispose(); throw; }
    }

    // Read-only even when identity geometry shares storage with the immutable base.
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    internal static unsafe OklabColor.Classification Read(ushort* source, int offset,
        RenderKernelSupport.PixelLayout layout, AgxCrossing.Matrix3x3 wb, DcpHueSatMap? map, ushort[]? sample,
        bool lightnessOnly = false)
    {
        double r = source[offset + layout.Red], g = source[offset + layout.Green], b = source[offset + layout.Blue];
        if (map != null)
        {
            sample![0] = (ushort)r; sample[1] = (ushort)g; sample[2] = (ushort)b;
            DcpHueSatRenderer.ApplyLut(sample, 0, 0, 1, 2, map.RgbLut!);
            r = sample[0]; g = sample[1]; b = sample[2];
        }
        r /= 65535; g /= 65535; b /= 65535;
        var red = wb.Row0(r, g, b); var green = wb.Row1(r, g, b); var blue = wb.Row2(r, g, b);
        return lightnessOnly ? new(OklabColor.ClassifyLightness(red, green, blue), 0, 0)
            : OklabColor.Classify(red, green, blue);
    }

    internal static (double? Hue, string? Rejection, int Count) Pick(BaseImage? basis,
        EditSettings settings, Point correctedPoint, PixelSize size) => Pick(basis, settings, correctedPoint, size, out _, out _);

    internal static unsafe (double? Hue, string? Rejection, int Count) Pick(BaseImage? basis,
        EditSettings settings, Point correctedPoint, PixelSize size, out double reliability, out double coherence)
    {
        reliability = coherence = 0;
        if (basis == null) return (null, "no matching base", 0);
        if (basis.Info.IsMonochrome) return (null, "Monochrome RAW — color controls unavailable", 0);
        using var geometry = Prepare(basis, settings, size, out var trace, out var wb, out var map);
        var x = correctedPoint.X * trace.CorrectedFrameWidth - trace.CropX;
        var y = correctedPoint.Y * trace.CorrectedFrameHeight - trace.CropY;
        if (!double.IsFinite(x) || !double.IsFinite(y) || x < 0 || y < 0 || x >= trace.Width || y >= trace.Height)
            return (null, "off-image", 0);
        var radius = .004 * Math.Max(trace.CorrectedFrameWidth, trace.CorrectedFrameHeight);
        var width = size.Width; var height = size.Height;
        var scaleX = width / (double)trace.Width; var scaleY = height / (double)trace.Height;
        x *= scaleX; y *= scaleY;
        // Keep the corrected-frame disk circular even when rounded surface dimensions scale differently.
        var radiusX = radius * scaleX; var radiusY = radius * scaleY;
        using var pixels = geometry.GetPixelsUnsafe();
        var layout = RenderKernelSupport.GetLayout(pixels);
        var source = (ushort*)pixels.GetAreaPointer(0, 0, geometry.Width, geometry.Height);
        if (source == null) throw new InvalidOperationException("Unable to read hue sample basis.");
        var sample = map == null ? null : new ushort[3];
        double a = 0, b = 0, chroma = 0;
        var count = 0;
        for (var row = Math.Max(0, (int)Math.Floor(y - radiusY)); row <= Math.Min(height - 1, (int)(y + radiusY)); row++)
        for (var col = Math.Max(0, (int)Math.Floor(x - radiusX)); col <= Math.Min(width - 1, (int)(x + radiusX)); col++)
        {
            if (Math.Pow((col + .5 - x) / scaleX, 2) + Math.Pow((row + .5 - y) / scaleY, 2) > radius * radius &&
                (col != (int)x || row != (int)y)) continue;
            var lab = Read(source, (row * width + col) * layout.Channels, layout, wb, map, sample);
            a += lab.A; b += lab.B; chroma += lab.Chroma; count++;
        }
        var mean = new OklabColor.Classification(0, a / count, b / count);
        reliability = HueWindow.Reliability(mean.Chroma);
        coherence = chroma == 0 ? 0 : mean.Chroma / (chroma / count);
        if (reliability < .5) return (null, "too neutral", count);
        if (coherence < .75) return (null, "mixed colors", count);
        return (mean.Hue, null, count);
    }
}
