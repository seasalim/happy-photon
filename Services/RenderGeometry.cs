using HappyPhoton.Models;
using ImageMagick;

namespace HappyPhoton.Services;

internal readonly record struct RenderGeometryTrace(
    int QuarterTurnWidth,
    int QuarterTurnHeight,
    int HorizonCanvasWidth,
    int HorizonCanvasHeight,
    int CropX,
    int CropY,
    int Width,
    int Height);

internal static class RenderGeometry
{
    public static RenderGeometryTrace Apply(
        MagickImage image,
        EditSettings settings)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(settings);

        if (settings.Rotation != 0)
        {
            image.Rotate(settings.Rotation);
        }

        var quarterTurnWidth = checked((int)image.Width);
        var quarterTurnHeight = checked((int)image.Height);

        CropRegion? safeCrop = null;
        if (settings.HorizonRotation != 0.0)
        {
            var sourceWidth = image.Width;
            var sourceHeight = image.Height;
            image.Rotate(settings.HorizonRotation);
            image.ResetPage();
            safeCrop = CropGeometry.SafeBoundsAfterRotation(
                sourceWidth,
                sourceHeight,
                settings.HorizonRotation,
                image.Width,
                image.Height);
        }

        var horizonCanvasWidth = checked((int)image.Width);
        var horizonCanvasHeight = checked((int)image.Height);

        var effectiveCrop = GetEffectiveCrop(settings.Crop, safeCrop);
        if (effectiveCrop == null || effectiveCrop.IsFullImage)
        {
            return new RenderGeometryTrace(
                quarterTurnWidth,
                quarterTurnHeight,
                horizonCanvasWidth,
                horizonCanvasHeight,
                0,
                0,
                horizonCanvasWidth,
                horizonCanvasHeight);
        }

        var (x, y, width, height) =
            effectiveCrop.ToPixels((int)image.Width, (int)image.Height);
        image.Crop(new MagickGeometry(x, y, (uint)width, (uint)height));
        image.ResetPage();
        return new RenderGeometryTrace(
            quarterTurnWidth,
            quarterTurnHeight,
            horizonCanvasWidth,
            horizonCanvasHeight,
            x,
            y,
            width,
            height);
    }

    private static CropRegion? GetEffectiveCrop(
        CropRegion? crop,
        CropRegion? safeCrop)
    {
        if (crop == null)
        {
            return safeCrop;
        }

        // An explicit full-image crop requests the whole rotated canvas; the
        // crop tool previews this way so its overlay coordinates match the
        // displayed bitmap. Only a missing crop falls back to the automatic
        // horizon safe bounds.
        if (crop.IsFullImage)
        {
            return null;
        }

        return safeCrop == null ? crop : CropGeometry.Intersect(crop, safeCrop);
    }
}
