using System.Buffers;
using System.Diagnostics;
using HappyPhoton.Models;
using ImageMagick;
using static HappyPhoton.Services.RenderKernelSupport;

namespace HappyPhoton.Services;

internal static partial class RenderNoiseReduction
{
    // The combined path carries seven float values per core pixel. Quarter-size
    // bands keep it below the 150 MiB sibling ceiling.
    private const int NoiseReductionBandPixelLimit =
        DefaultBandPixelLimit / 4;

    internal static void Apply(
        MagickImage image,
        BaseImageInfo info,
        DetailSettings settings) =>
        Apply(image, info, settings, NoiseReductionBandPixelLimit);

    internal static void Apply(
        MagickImage image,
        BaseImageInfo info,
        DetailSettings settings,
        int bandPixelLimit)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(info);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bandPixelLimit);

        var lumaAmount = SliderAmount(settings.LuminanceNr);
        var chromaAmount = info.IsMonochrome
            ? 0
            : SliderAmount(settings.ChromaNr);
        if (lumaAmount <= 0 && chromaAmount <= 0)
        {
            return;
        }

        var lumaScales = ResolveScales(image, info, lumaAmount);
        var chromaScales = ResolveChromaScales(image, info, chromaAmount);
        if (lumaScales.Length == 0 && chromaScales.Length == 0)
        {
            return;
        }

        ApplyBanded(
            image,
            lumaScales,
            chromaScales,
            bandPixelLimit);
    }

    internal static void ApplyResting(
        MagickImage image,
        BaseImageInfo info,
        DetailSettings settings,
        RenderExecutionOptions execution)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(info);
        ArgumentNullException.ThrowIfNull(settings);
        execution.ThrowIfCancellationRequested();

        var lumaAmount = SliderAmount(settings.LuminanceNr);
        var chromaAmount = info.IsMonochrome
            ? 0
            : SliderAmount(settings.ChromaNr);
        if (lumaAmount <= 0 && chromaAmount <= 0)
        {
            return;
        }

        var lumaScales = ResolveScales(image, info, lumaAmount);
        var chromaScales = ResolveChromaScales(image, info, chromaAmount);
        if (lumaScales.Length == 0 && chromaScales.Length == 0)
        {
            return;
        }

        ApplyBanded(
            image,
            lumaScales,
            chromaScales,
            NoiseReductionBandPixelLimit,
            execution);
    }

    private static float SliderAmount(int value) =>
        Math.Clamp(value, 0, 100) / 100f;

    private static void ApplyBanded(
        MagickImage image,
        WaveletScale[] lumaScales,
        WaveletScale[] chromaScales,
        int bandPixelLimit,
        RenderExecutionOptions? execution = null)
    {
        var stopwatch = Stopwatch.StartNew();
        using var pixels = image.GetPixels();
        var layout = GetLayout(pixels);
        var width = checked((int)image.Width);
        var height = checked((int)image.Height);
        var lumaHalo = lumaScales.Sum(scale => scale.SupportRadius);
        var chromaAlignment = chromaScales.Length > 1
            ? 1 << (chromaScales.Length - 1)
            : 1;
        var chromaSupport = chromaScales.Sum(scale => scale.SupportRadius);
        var chromaHalo = checked(
            (chromaSupport + chromaAlignment - 1) /
            chromaAlignment * chromaAlignment);
        var halo = Math.Max(lumaHalo, chromaHalo);
        var bandRows = Math.Min(
            height,
            Math.Max(halo, bandPixelLimit / width));
        if (bandRows < height && chromaAlignment > 1)
        {
            bandRows = Math.Max(
                halo,
                bandRows / chromaAlignment * chromaAlignment);
        }
        var upperCarry = new ushort[checked(
            halo * width * layout.Channels)];
        var maximumSourceRows = Math.Min(height, bandRows + halo * 2);
        var source = ArrayPool<ushort>.Shared.Rent(checked(
            maximumSourceRows * width * layout.Channels));
        var hasUpperCarry = false;
        var bandCount = 0;
        try
        {
            for (var bandStart = 0; bandStart < height;)
            {
                execution?.ThrowIfCancellationRequested();
                var outputRows = Math.Min(bandRows, height - bandStart);
                var bandEnd = bandStart + outputRows;
                var sourceStart = Math.Max(0, bandStart - halo);
                var sourceEnd = Math.Min(height, bandEnd + halo);
                var sourceRows = sourceEnd - sourceStart;
                var sourceSampleCount = checked(
                    sourceRows * width * layout.Channels);
                pixels.GetReadOnlyArea(
                        0,
                        sourceStart,
                        image.Width,
                        checked((uint)sourceRows))
                    .CopyTo(source.AsSpan(0, sourceSampleCount));
                if (hasUpperCarry)
                {
                    upperCarry.CopyTo(source, 0);
                }
                if (bandEnd < height)
                {
                    var carryOffset = checked(
                        (bandEnd - halo - sourceStart) *
                        width * layout.Channels);
                    source.AsSpan(carryOffset, upperCarry.Length)
                        .CopyTo(upperCarry);
                    hasUpperCarry = true;
                }

                ProcessBand(
                    source,
                    width,
                    height,
                    sourceStart,
                    sourceEnd,
                    bandStart,
                    outputRows,
                    layout,
                    lumaScales,
                    chromaScales,
                    execution);

                var coreSamples = checked(
                    outputRows * width * layout.Channels);
                var coreOffset = checked(
                    (bandStart - sourceStart) * width * layout.Channels);
                source.AsSpan(coreOffset, coreSamples).CopyTo(source);
                execution?.ThrowIfCancellationRequested();
                pixels.SetArea(
                    0,
                    bandStart,
                    image.Width,
                    checked((uint)outputRows),
                    source.AsSpan(0, coreSamples));
                bandStart = bandEnd;
                bandCount++;
            }
        }
        finally
        {
            ArrayPool<ushort>.Shared.Return(source);
        }

        ImageServiceHelpers.LogPerformance(
            nameof(RenderNoiseReduction),
            nameof(ApplyBanded),
            stopwatch.ElapsedMilliseconds,
            $"size={image.Width}x{image.Height}",
            $"planes={(lumaScales.Length > 0 ? 1 : 0) + (chromaScales.Length > 0 ? 2 : 0)};" +
            $"scales={Math.Max(lumaScales.Length, chromaScales.Length)};" +
            $"halo={halo};bands={bandCount};bandRows={bandRows}");
    }

    private static void ProcessBand(
        ushort[] source,
        int width,
        int height,
        int sourceStart,
        int sourceEnd,
        int bandStart,
        int outputRows,
        PixelLayout pixelLayout,
        WaveletScale[] lumaScales,
        WaveletScale[] chromaScales,
        RenderExecutionOptions? execution)
    {
        var sourceRows = sourceEnd - sourceStart;
        var planeSamples = checked(sourceRows * width);
        var outputSamples = checked(outputRows * width);
        var luma = lumaScales.Length > 0
            ? ArrayPool<float>.Shared.Rent(planeSamples)
            : null;
        var chroma = chromaScales.Length > 0
            ? ArrayPool<float>.Shared.Rent(checked(planeSamples * 2))
            : null;
        var workspacePlanes = chroma is null ? 1 : 2;
        var horizontal = ArrayPool<float>.Shared.Rent(checked(
            planeSamples * workspacePlanes));
        var adjustment = ArrayPool<float>.Shared.Rent(checked(
            outputSamples * workspacePlanes));
        try
        {
            LoadActivePlanes(
                source,
                luma,
                chroma,
                width,
                sourceRows,
                pixelLayout,
                execution);
            if (luma is not null)
            {
                ProcessLumaPass(
                    source,
                    luma,
                    horizontal,
                    adjustment,
                    width,
                    height,
                    sourceStart,
                    sourceEnd,
                    bandStart,
                    outputRows,
                    pixelLayout,
                    lumaScales,
                    execution);
            }
            if (chroma is not null)
            {
                ProcessChromaPass(
                    source,
                    chroma,
                    horizontal,
                    adjustment,
                    width,
                    height,
                    sourceStart,
                    sourceEnd,
                    bandStart,
                    outputRows,
                    pixelLayout,
                    chromaScales,
                    execution);
            }
        }
        finally
        {
            if (luma is not null)
                ArrayPool<float>.Shared.Return(luma);
            if (chroma is not null)
                ArrayPool<float>.Shared.Return(chroma);
            ArrayPool<float>.Shared.Return(horizontal);
            ArrayPool<float>.Shared.Return(adjustment);
        }
    }
}
