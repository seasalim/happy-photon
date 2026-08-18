using HappyPhoton.Models;
using ImageMagick;

namespace HappyPhoton.Services;

internal static class RenderChromaStage
{
    public static bool Apply(MagickImage image, EditSettings settings)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(settings);

        var factor =
            (100 + settings.Saturation) / 100.0 *
            (100 + settings.Vibrance * 0.5) / 100.0;
        if (factor == 1.0)
        {
            return false;
        }

        image.Modulate(
            new Percentage(100),
            new Percentage(factor * 100),
            new Percentage(100));
        return true;
    }
}
