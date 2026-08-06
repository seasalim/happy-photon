using HappyPhoton.Models;
using HappyPhoton.Services;
using ImageMagick;

namespace HappyPhoton.Tests;

internal static class GeometryReferenceRenderer
{
    public static void Apply(MagickImage image, EditSettings settings)
    {
        if (settings.Rotation != 0)
        {
            image.Rotate(settings.Rotation);
        }

        CropRegion? safeCrop = null;
        if (settings.HorizonRotation != 0)
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

        var effectiveCrop = settings.Crop == null
            ? safeCrop
            : safeCrop == null
                ? settings.Crop
                : CropGeometry.Intersect(settings.Crop, safeCrop);
        if (effectiveCrop == null || effectiveCrop.IsFullImage)
        {
            return;
        }

        var (x, y, width, height) = effectiveCrop.ToPixels(
            (int)image.Width,
            (int)image.Height);
        image.Crop(new MagickGeometry(x, y, (uint)width, (uint)height));
        image.ResetPage();
    }
}
