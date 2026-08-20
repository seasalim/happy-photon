using System.Runtime.CompilerServices;
using HappyPhoton.Models;
using ImageMagick;

namespace HappyPhoton.Services;

internal static class RenderEffects
{
    private const double GrainAmplitude = 0.12;
    private const double HorizontalRadius = 0.90;
    private const double VerticalRadius = 0.75;
    private static readonly double CornerRadius = Math.Sqrt(
        1 / (HorizontalRadius * HorizontalRadius) +
        1 / (VerticalRadius * VerticalRadius));

    internal static void Apply(
        MagickImage image,
        EffectsSettings? settings)
    {
        ArgumentNullException.ThrowIfNull(image);
        if (settings?.HasActivePixels != true)
        {
            return;
        }

        ApplyCore(image, settings, execution: null);
    }

    internal static void ApplyResting(
        MagickImage image,
        EffectsSettings? settings,
        RenderExecutionOptions execution)
    {
        ArgumentNullException.ThrowIfNull(image);
        if (settings?.HasActivePixels != true)
        {
            return;
        }

        execution.ThrowIfCancellationRequested();
        execution.ReportStage("effects");
        ApplyCore(image, settings, execution);
    }

    private static void ApplyCore(
        MagickImage image,
        EffectsSettings settings,
        RenderExecutionOptions? execution)
    {
        using var pixels = image.GetPixels();
        var values = pixels.GetArea(0, 0, image.Width, image.Height) ??
            throw new InvalidOperationException("Unable to access Q16 pixels.");
        execution?.ThrowIfCancellationRequested();

        var layout = RenderKernelSupport.GetLayout(pixels);
        var width = checked((int)image.Width);
        var height = checked((int)image.Height);
        var pixelCount = checked(width * height);
        var workers = Math.Min(
            Environment.ProcessorCount,
            Math.Max(1, (pixelCount + 32_767) / 32_768));
        if (execution is { } bounded)
        {
            workers = bounded.CapWorkers(workers);
            Parallel.For(0, workers, bounded.ParallelOptions, ProcessWorker);
        }
        else
        {
            Parallel.For(0, workers, ProcessWorker);
        }

        execution?.ThrowIfCancellationRequested();
        pixels.SetArea(0, 0, image.Width, image.Height, values);
        execution?.ThrowIfCancellationRequested();

        void ProcessWorker(int worker)
        {
            var start = pixelCount * worker / workers;
            var end = pixelCount * (worker + 1) / workers;
            for (var pixel = start; pixel < end; pixel++)
            {
                var x = pixel % width;
                var y = pixel / width;
                var offset = pixel * layout.Channels;
                if (settings.Vignette != 0)
                {
                    ApplyVignette(
                        values,
                        offset,
                        layout,
                        VignetteStrength(
                            x,
                            y,
                            width,
                            height,
                            settings.Vignette,
                            settings.Midpoint));
                }
                if (settings.Grain != 0)
                {
                    ApplyGrain(
                        values,
                        offset,
                        layout,
                        GrainSample(x, y, settings.GrainSize) *
                            Math.Clamp(settings.Grain, 0, 100) /
                            100.0 * GrainAmplitude);
                }
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static double VignetteStrength(
        int x,
        int y,
        int width,
        int height,
        int amount,
        int midpoint)
    {
        var normalizedX = (2 * (x + 0.5) / width - 1) / HorizontalRadius;
        var normalizedY = (2 * (y + 0.5) / height - 1) / VerticalRadius;
        var radius = Math.Min(
            1,
            Math.Sqrt(normalizedX * normalizedX +
                normalizedY * normalizedY) / CornerRadius);
        var onset = Math.Clamp(midpoint, 0, 100) * 0.0095;
        var transition = Math.Clamp((radius - onset) / (1 - onset), 0, 1);
        var falloff = transition * transition * (3 - 2 * transition);
        return Math.Clamp(amount, -100, 100) / 100.0 * falloff;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ApplyVignette(
        Span<ushort> values,
        int offset,
        RenderKernelSupport.PixelLayout layout,
        double strength)
    {
        if (strength < 0)
        {
            var scale = 1 + strength;
            values[offset + layout.Red] = ScaleQuantum(
                values[offset + layout.Red], scale);
            values[offset + layout.Green] = ScaleQuantum(
                values[offset + layout.Green], scale);
            values[offset + layout.Blue] = ScaleQuantum(
                values[offset + layout.Blue], scale);
            return;
        }

        values[offset + layout.Red] = LiftQuantum(
            values[offset + layout.Red], strength);
        values[offset + layout.Green] = LiftQuantum(
            values[offset + layout.Green], strength);
        values[offset + layout.Blue] = LiftQuantum(
            values[offset + layout.Blue], strength);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ApplyGrain(
        Span<ushort> values,
        int offset,
        RenderKernelSupport.PixelLayout layout,
        double sample)
    {
        var red = values[offset + layout.Red];
        var green = values[offset + layout.Green];
        var blue = values[offset + layout.Blue];
        var minimum = Math.Min(red, Math.Min(green, blue));
        var maximum = Math.Max(red, Math.Max(green, blue));
        var requested = sample * ushort.MaxValue;
        var safe = Math.Clamp(
            requested,
            -(double)minimum,
            ushort.MaxValue - (double)maximum);
        var delta = (int)Math.Round(safe, MidpointRounding.AwayFromZero);
        values[offset + layout.Red] = (ushort)(red + delta);
        values[offset + layout.Green] = (ushort)(green + delta);
        values[offset + layout.Blue] = (ushort)(blue + delta);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static double GrainSample(int x, int y, GrainSize size)
    {
        var scale = size switch
        {
            GrainSize.Fine => 1,
            GrainSize.Medium => 2,
            GrainSize.Coarse => 3,
            _ => throw new ArgumentOutOfRangeException(nameof(size), size, null)
        };
        if (scale == 1)
        {
            return HashSample(x, y, size);
        }

        var cellX = x / scale;
        var cellY = y / scale;
        var fractionX = (double)(x % scale) / scale;
        var fractionY = (double)(y % scale) / scale;
        var top = Lerp(
            HashSample(cellX, cellY, size),
            HashSample(cellX + 1, cellY, size),
            fractionX);
        var bottom = Lerp(
            HashSample(cellX, cellY + 1, size),
            HashSample(cellX + 1, cellY + 1, size),
            fractionX);
        return Lerp(top, bottom, fractionY);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double HashSample(int x, int y, GrainSize size)
    {
        var value = unchecked(
            (uint)x * 0x9E3779B1u ^
            (uint)y * 0x85EBCA77u ^
            ((uint)size + 1u) * 0xC2B2AE3Du);
        value ^= value >> 16;
        value *= 0x7FEB352Du;
        value ^= value >> 15;
        value *= 0x846CA68Bu;
        value ^= value >> 16;
        return value / (double)uint.MaxValue * 2 - 1;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double Lerp(double left, double right, double amount) =>
        left + (right - left) * amount;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ushort ScaleQuantum(ushort value, double scale) =>
        (ushort)Math.Round(value * scale, MidpointRounding.AwayFromZero);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ushort LiftQuantum(ushort value, double strength) =>
        (ushort)Math.Round(
            value + (ushort.MaxValue - value) * strength,
            MidpointRounding.AwayFromZero);
}
