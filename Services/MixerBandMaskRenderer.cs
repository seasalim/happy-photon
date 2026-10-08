using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using HappyPhoton.Models;

namespace HappyPhoton.Services;

internal static class MixerBandMaskRenderer
{
    internal static unsafe WriteableBitmap Render(BaseImage basis, EditSettings settings,
        ColorMixerBand band, PixelSize size, uint tint, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using var geometry = LocalRangeSampling.Prepare(basis, settings, size, out _, out var wb, out var map);
        using var pixels = geometry.GetPixelsUnsafe();
        var layout = RenderKernelSupport.GetLayout(pixels);
        var source = (ushort*)pixels.GetAreaPointer(0, 0, geometry.Width, geometry.Height);
        if (source == null) throw new InvalidOperationException("Unable to read mixer mask basis.");

        var bitmap = new WriteableBitmap(size, new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);

        try
        {
            using var frame = bitmap.Lock();
            var destination = (byte*)frame.Address;
            var stride = frame.RowBytes;
            var count = size.Width * size.Height;
            var workers = Math.Min(Environment.ProcessorCount, Math.Max(1, (count + 32767) / 32768));
            Parallel.For(0, workers, new ParallelOptions { CancellationToken = token }, RenderWorker);

            [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveOptimization)]
            void RenderWorker(int worker)
            {
                var sample = map == null ? null : new ushort[3];

                for (var pixel = count * worker / workers; pixel < count * (worker + 1) / workers; pixel++)
                {
                    if ((pixel & 8191) == 0) token.ThrowIfCancellationRequested();

                    var lab = LocalRangeSampling.Read(source, pixel * layout.Channels, layout, wb, map, sample);
                    var weight = lab.Chroma <= OklabColor.MixerHueFloor ? 0
                        : OklabColor.MixerBandInfluence((int)band, Math.Atan2(lab.B, lab.A), lab.Chroma);
                    var alpha = (byte)Math.Round((1 - weight) * .9 * 255);
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
        catch
        {
            bitmap.Dispose();
            throw;
        }
    }
}
