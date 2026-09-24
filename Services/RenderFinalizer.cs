using HappyPhoton.Models;
using ImageMagick;
using static HappyPhoton.Services.RenderKernelSupport;

namespace HappyPhoton.Services;

internal static class RenderFinalizer
{
    internal static MagickImage FinalizeProof(
        MagickImage displayRec2020,
        int? maxDimension,
        OutputColorSpace outputColorSpace,
        OutputSharpeningMode outputSharpening,
        EffectsSettings? effects = null,
        WatermarkSpec? watermark = null)
    {
        ArgumentNullException.ThrowIfNull(displayRec2020);
        if (maxDimension is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxDimension));
        }

        return FinalizeOwnedProof(
            new MagickImage(displayRec2020),
            maxDimension,
            outputColorSpace,
            outputSharpening,
            effects, watermark);
    }

    internal static MagickImage FinalizeOwnedProof(
        MagickImage displayRec2020,
        int? maxDimension,
        OutputColorSpace outputColorSpace,
        OutputSharpeningMode outputSharpening,
        EffectsSettings? effects = null,
        WatermarkSpec? watermark = null)
    {
        ArgumentNullException.ThrowIfNull(displayRec2020);
        try
        {
            if (maxDimension is <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxDimension));
            }

            var before = Math.Max(displayRec2020.Width, displayRec2020.Height);
            if (maxDimension is { } limit)
            {
                RenderColorEncoding.ResizeInLinearLight(displayRec2020, limit);
            }
            var wasResized = Math.Max(
                displayRec2020.Width,
                displayRec2020.Height) < before;
            RenderSharpening.ApplyOutput(
                displayRec2020,
                outputSharpening,
                wasResized,
                DefaultBandPixelLimit);
            RenderEffects.Apply(displayRec2020, effects);
            RenderColorEncoding.ConvertEncodedRec2020ToTarget(
                displayRec2020,
                outputColorSpace);
            if (watermark != null)
            {
                var watermarkProbe = RenderStageProbe.Begin();
                WatermarkRenderer.Apply(displayRec2020, watermark);
                RenderStageProbe.End(watermarkProbe, "watermark", displayRec2020);
            }
            return displayRec2020;
        }
        catch
        {
            displayRec2020.Dispose();
            throw;
        }
    }

    internal static MagickImage Finalize(
        MagickImage displayRec2020,
        int? maxDimension,
        OutputColorSpace outputColorSpace,
        OutputSharpeningMode outputSharpening,
        bool wasResized,
        int detailBandPixelLimit = DefaultBandPixelLimit,
        EffectsSettings? effects = null,
        WatermarkSpec? watermark = null)
    {
        ArgumentNullException.ThrowIfNull(displayRec2020);
        if (maxDimension is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxDimension));
        }

        var output = new MagickImage(displayRec2020);
        return FinalizeOwned(
            output,
            maxDimension,
            outputColorSpace,
            outputSharpening,
            wasResized,
            detailBandPixelLimit,
            effects, watermark);
    }

    internal static MagickImage FinalizeOwned(
        MagickImage displayRec2020,
        int? maxDimension,
        OutputColorSpace outputColorSpace,
        OutputSharpeningMode outputSharpening,
        bool wasResized,
        int detailBandPixelLimit = DefaultBandPixelLimit,
        EffectsSettings? effects = null,
        WatermarkSpec? watermark = null)
        => FinalizeOwned(displayRec2020, maxDimension, outputColorSpace,
            outputSharpening, wasResized, false, out _, detailBandPixelLimit, effects, watermark);

    internal static MagickImage FinalizeOwned(
        MagickImage displayRec2020,
        int? maxDimension,
        OutputColorSpace outputColorSpace,
        OutputSharpeningMode outputSharpening,
        bool wasResized,
        bool retainEncodedFrame,
        out RenderColorEncoding.EncodedFrame? encodedFrame,
        int detailBandPixelLimit = DefaultBandPixelLimit,
        EffectsSettings? effects = null,
        WatermarkSpec? watermark = null)
    {
        encodedFrame = null;
        ArgumentNullException.ThrowIfNull(displayRec2020);
        try
        {
            if (maxDimension is <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxDimension));
            }
            // The watermark is drawn after encode-target, so a retained frame would not match the image.
            if (retainEncodedFrame && watermark != null)
            {
                throw new ArgumentException(
                    "A watermarked render cannot retain its encoded frame.",
                    nameof(watermark));
            }

            var probe = RenderStageProbe.Begin();
            if (maxDimension is { } limit)
            {
                var before = Math.Max(
                    displayRec2020.Width,
                    displayRec2020.Height);
                RenderColorEncoding.ResizeInLinearLight(
                    displayRec2020,
                    limit);
                wasResized |= Math.Max(
                    displayRec2020.Width,
                    displayRec2020.Height) < before;
            }
            RenderStageProbe.End(probe, "resize", displayRec2020);

            probe = RenderStageProbe.Begin();
            RenderSharpening.ApplyOutput(
                displayRec2020,
                outputSharpening,
                wasResized,
                detailBandPixelLimit);
            RenderStageProbe.End(probe, "output-sharpen", displayRec2020);
            probe = RenderStageProbe.Begin();
            RenderEffects.Apply(displayRec2020, effects);
            RenderStageProbe.End(probe, "effects", displayRec2020);
            probe = RenderStageProbe.Begin();
            var frame = RenderColorEncoding.ConvertEncodedRec2020ToTarget(
                displayRec2020,
                outputColorSpace);
            if (retainEncodedFrame) encodedFrame = frame;
            RenderStageProbe.End(probe, "encode-target", displayRec2020);
            if (watermark != null)
            {
                var watermarkProbe = RenderStageProbe.Begin();
                WatermarkRenderer.Apply(displayRec2020, watermark);
                RenderStageProbe.End(watermarkProbe, "watermark", displayRec2020);
            }
            return displayRec2020;
        }
        catch
        {
            displayRec2020.Dispose();
            throw;
        }
    }

    internal static MagickImage FinalizeOwnedResting(
        MagickImage displayRec2020,
        int? maxDimension,
        OutputColorSpace outputColorSpace,
        EffectsSettings? effects,
        RenderExecutionOptions execution)
    {
        ArgumentNullException.ThrowIfNull(displayRec2020);
        try
        {
            if (maxDimension is <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxDimension));
            }
            var probe = RenderStageProbe.Begin();
            if (maxDimension is { } limit)
            {
                RenderColorEncoding.ResizeInLinearLightResting(
                    displayRec2020,
                    limit,
                    execution);
            }
            RenderStageProbe.End(probe, "resize", displayRec2020);
            execution.ThrowIfCancellationRequested();
            probe = RenderStageProbe.Begin();
            RenderEffects.ApplyResting(
                displayRec2020,
                effects,
                execution);
            RenderStageProbe.End(probe, "effects", displayRec2020);
            probe = RenderStageProbe.Begin();
            RenderColorEncoding.ConvertEncodedRec2020ToTargetResting(
                displayRec2020,
                outputColorSpace,
                execution);
            RenderStageProbe.End(probe, "encode-target", displayRec2020);
            return displayRec2020;
        }
        catch
        {
            displayRec2020.Dispose();
            throw;
        }
    }
}
