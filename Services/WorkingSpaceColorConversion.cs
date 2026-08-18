using ImageMagick;

namespace HappyPhoton.Services;

internal static class WorkingSpaceColorConversion
{
    private static readonly MagickColorMatrix SrgbToRec2020Matrix = new(3,
    [
        RgbColorSpaceMatrices.LinearSrgbToLinearRec2020[0, 0],
        RgbColorSpaceMatrices.LinearSrgbToLinearRec2020[0, 1],
        RgbColorSpaceMatrices.LinearSrgbToLinearRec2020[0, 2],
        RgbColorSpaceMatrices.LinearSrgbToLinearRec2020[1, 0],
        RgbColorSpaceMatrices.LinearSrgbToLinearRec2020[1, 1],
        RgbColorSpaceMatrices.LinearSrgbToLinearRec2020[1, 2],
        RgbColorSpaceMatrices.LinearSrgbToLinearRec2020[2, 0],
        RgbColorSpaceMatrices.LinearSrgbToLinearRec2020[2, 1],
        RgbColorSpaceMatrices.LinearSrgbToLinearRec2020[2, 2]
    ]);

    internal static void ConvertSrgbToLinearRec2020(MagickImage image)
    {
        ArgumentNullException.ThrowIfNull(image);

        image.SetAttribute("colorspace", "sRGB");
        image.ColorSpace = ColorSpace.RGB;
        image.ColorMatrix(SrgbToRec2020Matrix);
        image.SetAttribute("colorspace", "RGB");
        if (image.ColorSpace != ColorSpace.RGB)
        {
            throw new InvalidOperationException(
                "Working-space conversion did not produce linear RGB samples.");
        }
    }
}
