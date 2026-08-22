using System.Buffers;
using HappyPhoton.Models;
using ImageMagick;

namespace HappyPhoton.Services;

internal static class RenderChromaStage
{
    internal const int DefaultBandPixelLimit = 524_288;

    /// <summary>
    /// Applies chroma edits to an image whose pixel cache is already owned and
    /// materialized by a whole-frame write. Banded writes are not safe on
    /// ImageMagick copy-on-write clones; see the pixel-write rule documented by
    /// <see cref="DcpHueSatRenderer.Apply(MagickImage, DcpHueSatMap?)"/>.
    /// </summary>
    public static bool Apply(MagickImage image, EditSettings settings)
        => Apply(image, settings, DefaultBandPixelLimit, execution: null);

    internal static bool Apply(
        MagickImage image,
        EditSettings settings,
        int bandPixelLimit)
        => Apply(image, settings, bandPixelLimit, execution: null);

    internal static bool Apply(
        MagickImage image,
        EditSettings settings,
        RenderExecutionOptions execution)
        => Apply(image, settings, DefaultBandPixelLimit, execution);

    private static bool Apply(
        MagickImage image,
        EditSettings settings,
        int bandPixelLimit,
        RenderExecutionOptions? execution)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bandPixelLimit);
        var mixer = ColorMixerParameters.From(settings.Mixer);
        if (settings.Saturation == 0 &&
            settings.Vibrance == 0 &&
            !mixer.HasActive)
        {
            return false;
        }

        execution?.ThrowIfCancellationRequested();
        using var pixels = image.GetPixels();
        var layout = RenderKernelSupport.GetLayout(pixels);
        var width = checked((int)image.Width);
        var height = checked((int)image.Height);
        var bandHeight = Math.Max(
            1,
            Math.Min(height, bandPixelLimit / width));
        var bandSampleCount = checked(width * bandHeight * layout.Channels);
        var buffer = ArrayPool<ushort>.Shared.Rent(bandSampleCount);
        try
        {
            for (var y = 0; y < height; y += bandHeight)
            {
                execution?.ThrowIfCancellationRequested();
                var rows = Math.Min(bandHeight, height - y);
                var pixelCount = checked(width * rows);
                var sampleCount = checked(pixelCount * layout.Channels);
                pixels.GetReadOnlyArea(0, y, image.Width, (uint)rows)
                    .CopyTo(buffer);
                TransformBand(
                    buffer,
                    pixelCount,
                    layout,
                    settings.Saturation,
                    settings.Vibrance,
                    in mixer,
                    execution);
                execution?.ThrowIfCancellationRequested();
                pixels.SetArea(
                    0,
                    y,
                    image.Width,
                    (uint)rows,
                    buffer.AsSpan(0, sampleCount));
            }
        }
        finally
        {
            ArrayPool<ushort>.Shared.Return(buffer);
        }

        execution?.ThrowIfCancellationRequested();
        return true;
    }

    private static void TransformBand(
        ushort[] values,
        int pixelCount,
        RenderKernelSupport.PixelLayout layout,
        int saturation,
        int vibrance,
        in ColorMixerParameters mixer,
        RenderExecutionOptions? execution)
    {
        var mixerParameters = mixer;
        var workers = Math.Min(
            Environment.ProcessorCount,
            Math.Max(1, (pixelCount + 32_767) / 32_768));
        if (execution is { } bounded)
        {
            workers = bounded.CapWorkers(workers);
            Parallel.For(
                0,
                workers,
                bounded.ParallelOptions,
                TransformWorker);
        }
        else
        {
            Parallel.For(0, workers, TransformWorker);
        }

        void TransformWorker(int worker)
        {
            var start = pixelCount * worker / workers;
            var end = pixelCount * (worker + 1) / workers;
            for (var pixel = start; pixel < end; pixel++)
            {
                var offset = pixel * layout.Channels;
                var red = values[offset + layout.Red];
                var green = values[offset + layout.Green];
                var blue = values[offset + layout.Blue];
                if (red == green && green == blue)
                {
                    continue;
                }

                var transformed = OklabColor.TransformQ16(
                    red,
                    green,
                    blue,
                    saturation,
                    vibrance,
                    in mixerParameters);
                values[offset + layout.Red] = transformed.Red;
                values[offset + layout.Green] = transformed.Green;
                values[offset + layout.Blue] = transformed.Blue;
            }
        }
    }
}
