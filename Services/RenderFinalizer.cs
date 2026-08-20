using HappyPhoton.Models;
using ImageMagick;

namespace HappyPhoton.Services;

internal static class RenderFinalizer
{
    internal static MagickImage Finalize(
        MagickImage displayRec2020,
        int? maxDimension,
        OutputColorSpace outputColorSpace,
        bool outputSharpening,
        bool wasResized,
        int detailBandPixelLimit = RenderDetail.DefaultBandPixelLimit,
        EffectsSettings? effects = null)
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
            effects);
    }

    internal static MagickImage FinalizeOwned(
        MagickImage displayRec2020,
        int? maxDimension,
        OutputColorSpace outputColorSpace,
        bool outputSharpening,
        bool wasResized,
        int detailBandPixelLimit = RenderDetail.DefaultBandPixelLimit,
        EffectsSettings? effects = null)
    {
        ArgumentNullException.ThrowIfNull(displayRec2020);
        try
        {
            if (maxDimension is <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxDimension));
            }

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

            RenderSharpening.ApplyOutput(
                displayRec2020,
                outputSharpening,
                wasResized,
                detailBandPixelLimit);
            RenderEffects.Apply(displayRec2020, effects);
            RenderColorEncoding.ConvertEncodedRec2020ToTarget(
                displayRec2020,
                outputColorSpace);
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
            if (maxDimension is { } limit)
            {
                RenderColorEncoding.ResizeInLinearLightResting(
                    displayRec2020,
                    limit,
                    execution);
            }
            execution.ThrowIfCancellationRequested();
            RenderEffects.ApplyResting(
                displayRec2020,
                effects,
                execution);
            RenderColorEncoding.ConvertEncodedRec2020ToTargetResting(
                displayRec2020,
                outputColorSpace,
                execution);
            return displayRec2020;
        }
        catch
        {
            displayRec2020.Dispose();
            throw;
        }
    }
}
