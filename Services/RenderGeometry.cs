using HappyPhoton.Models;
using ImageMagick;

namespace HappyPhoton.Services;

internal static class RenderGeometry
{
    public static void Apply(MagickImage image, EditSettings settings)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(settings);

        if (settings.Rotation != 0)
        {
            image.Rotate(settings.Rotation);
        }

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

        var effectiveCrop = GetEffectiveCrop(settings.Crop, safeCrop);
        if (effectiveCrop == null || effectiveCrop.IsFullImage)
        {
            return;
        }

        var (x, y, width, height) =
            effectiveCrop.ToPixels((int)image.Width, (int)image.Height);
        image.Crop(new MagickGeometry(x, y, (uint)width, (uint)height));
        image.ResetPage();
    }

    private static CropRegion? GetEffectiveCrop(
        CropRegion? crop,
        CropRegion? safeCrop)
    {
        if (crop == null)
        {
            return safeCrop;
        }

        return safeCrop == null ? crop : CropGeometry.Intersect(crop, safeCrop);
    }
}
