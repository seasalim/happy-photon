using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using HappyPhoton.Models;
using HappyPhoton.Services;

namespace HappyPhoton.Tests;

// LocalRangeMaskRenderer's requested-mask path with only the geometric term replaced.
// The source pointer stays read-only.
internal static class LocalsBrushMaskRenderer
{
    internal static string BuildReport { get; private set; } = "";

    internal static unsafe WriteableBitmap Render(BaseImage basis, EditSettings settings,
        LocalAdjustment local, BrushDocument document, PixelSize size, uint tint)
    {
        using var geometry = LocalRangeSampling.Prepare(basis, settings, size, out _, out var wb, out var map);
        var evaluation = new LocalsBrushEvaluation([document], size.Width, size.Height);
        BuildReport = evaluation.Report;
        using var pixels = geometry.GetPixelsUnsafe();
        var layout = RenderKernelSupport.GetLayout(pixels);
        var source = (ushort*)pixels.GetAreaPointer(0, 0, geometry.Width, geometry.Height);
        if (source == null) throw new InvalidOperationException("Unable to read brush mask basis");
        var bitmap = new WriteableBitmap(size, new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        try
        {
            using var frame = bitmap.Lock();
            var destination = (byte*)frame.Address; var stride = frame.RowBytes;
            var count = size.Width * size.Height;
            var workers = Math.Min(Environment.ProcessorCount, Math.Max(1, (count + 32767) / 32768));
            Parallel.For(0, workers, worker =>
            {
                var sample = map == null ? null : new ushort[3];
                for (var pixel = count * worker / workers; pixel < count * (worker + 1) / workers; pixel++)
                {
                    var weight = evaluation.Weight(0, pixel);
                    if (weight > 0)
                    {
                        var lab = LocalRangeSampling.Read(source, pixel * layout.Channels, layout, wb, map, sample);
                        weight *= LocalsBrushPlan.RangeWeight(local, lab, basis.Info.IsMonochrome);
                    }
                    var alpha = (byte)Math.Round(weight * .35 * 255);
                    var output = destination + pixel / size.Width * stride + pixel % size.Width * 4;
                    output[0] = (byte)((tint & 255) * alpha / 255);
                    output[1] = (byte)(((tint >> 8) & 255) * alpha / 255);
                    output[2] = (byte)(((tint >> 16) & 255) * alpha / 255);
                    output[3] = alpha;
                }
            });
            return bitmap;
        }
        catch { bitmap.Dispose(); throw; }
    }
}
