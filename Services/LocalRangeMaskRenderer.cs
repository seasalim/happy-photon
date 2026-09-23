using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using HappyPhoton.Models;

namespace HappyPhoton.Services;

internal static class LocalRangeMaskRenderer
{
    internal static unsafe WriteableBitmap Render(BaseImage basis, EditSettings settings,
        LocalAdjustment local, PixelSize size, uint tint, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using var geometry = LocalRangeSampling.Prepare(basis, settings, size, out var trace, out var wb, out var map);
        var maskSettings = settings.Clone();
        maskSettings.Locals = [local with { Enabled = true, Exposure = 1, Temperature = 0,
            Tint = 0, Saturation = 0, Luminance = null, Hue = null }];
        var mask = RenderLocals.Create(maskSettings, trace, size.Width, size.Height)!;
        var luminance = local.Luminance;
        var hue = !basis.Info.IsMonochrome && local.Hue is { Enabled: true } enabledHue ? enabledHue : null;
        using var pixels = geometry.GetPixelsUnsafe();
        var layout = RenderKernelSupport.GetLayout(pixels);
        // The native pixels remain read-only for the lifetime of this collection. Never write
        // through this pointer: an identity geometry clone can share the immutable base cache.
        var source = (ushort*)pixels.GetAreaPointer(0, 0, geometry.Width, geometry.Height);
        if (source == null) throw new InvalidOperationException("Unable to read local mask basis.");
        var bitmap = new WriteableBitmap(size, new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        try
        {
            using var frame = bitmap.Lock();
            var destination = (byte*)frame.Address;
            var stride = frame.RowBytes;
            var count = size.Width * size.Height;
            var workers = Math.Min(Environment.ProcessorCount, Math.Max(1, (count + 32767) / 32768));
            Parallel.For(0, workers, new ParallelOptions { CancellationToken = token }, RenderWorker);
            // Requested overlays must meet their latency bound on the first use, before tier promotion.
            [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveOptimization)]
            void RenderWorker(int worker)
            {
                var sample = map == null ? null : new ushort[3];
                for (var pixel = count * worker / workers; pixel < count * (worker + 1) / workers; pixel++)
                {
                    if ((pixel & 8191) == 0) token.ThrowIfCancellationRequested();
                    var weight = mask.Gain(pixel) - 1;
                    if (weight != 0)
                    {
                        var lab = LocalRangeSampling.Read(source, pixel * layout.Channels, layout, wb, map, sample, lightnessOnly: hue == null);
                        if (luminance != null) weight *= LuminanceWindow.Weight(luminance, lab.L);
                        if (weight != 0 && hue != null)
                            weight *= HueWindow.Weight(hue, lab.Hue, lab.Chroma);
                    }
                    var alpha = (byte)Math.Round(weight * .35 * 255);
                    var output = destination + pixel / size.Width * stride + pixel % size.Width * 4;
                    output[0] = (byte)((tint & 255) * alpha / 255);
                    output[1] = (byte)(((tint >> 8) & 255) * alpha / 255);
                    output[2] = (byte)(((tint >> 16) & 255) * alpha / 255);
                    output[3] = alpha;
                }
            }
            token.ThrowIfCancellationRequested();
            return bitmap;
        }
        catch { bitmap.Dispose(); throw; }
    }
}
