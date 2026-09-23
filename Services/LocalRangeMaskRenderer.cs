using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using HappyPhoton.Models;
using ImageMagick;

namespace HappyPhoton.Services;

internal static class LocalRangeMaskRenderer
{
    internal static unsafe WriteableBitmap Render(BaseImage basis, EditSettings settings,
        LocalAdjustment local, PixelSize size, uint tint, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using var geometry = RenderGeometry.Apply(basis.Pixels, settings, out var trace);
        if (geometry.Width != size.Width || geometry.Height != size.Height)
            geometry.Resize(new MagickGeometry((uint)size.Width, (uint)size.Height) { IgnoreAspectRatio = true });
        var maskSettings = settings.Clone();
        maskSettings.Locals = [local with { Enabled = true, Exposure = 1, Temperature = 0,
            Tint = 0, Saturation = 0, Luminance = null }];
        var mask = RenderLocals.Create(maskSettings, trace, size.Width, size.Height)!;
        var wb = new AgxCrossing.Matrix3x3(RenderChromaticStage.CreateWhiteBalanceMatrix(basis.Info, settings));
        var map = basis.Info.IsRawSource && !basis.Info.IsMonochrome ? basis.Info.DcpProfile?.HueSatMap : null;
        using var pixels = geometry.GetPixelsUnsafe();
        var layout = RenderKernelSupport.GetLayout(pixels);
        // The native pixels remain read-only for the lifetime of this collection. Never write
        // through this pointer: an identity geometry clone can share the immutable base cache.
        var source = (ushort*)pixels.GetAreaPointer(0, 0, geometry.Width, geometry.Height);
        if (source == null) throw new InvalidOperationException("Unable to read local mask basis.");
        if (map != null) map = DcpHueSatRenderer.Prepare(map);
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
                        var offset = pixel * layout.Channels;
                        double r = source[offset + layout.Red], g = source[offset + layout.Green], b = source[offset + layout.Blue];
                        if (map != null)
                        {
                            sample![0] = (ushort)r; sample[1] = (ushort)g; sample[2] = (ushort)b;
                            DcpHueSatRenderer.ApplyLut(sample, 0, 0, 1, 2, map.RgbLut!);
                            r = sample[0]; g = sample[1]; b = sample[2];
                        }
                        r /= 65535; g /= 65535; b /= 65535;
                        weight *= LuminanceWindow.Weight(local.Luminance!, OklabColor.ClassifyLightness(
                            wb.Row0(r, g, b), wb.Row1(r, g, b), wb.Row2(r, g, b)));
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
