using ImageMagick;

namespace HappyPhoton.Tests;

internal static class PipelineTestImages
{
    public static MagickImage CreateUnitGradient(int sampleCount = 256)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(sampleCount, 2);
        var gradient = new MagickImage(
            "gradient:black-white", 1, (uint)sampleCount)
        {
            Depth = 16,
            ColorSpace = ColorSpace.sRGB
        };
        return gradient;
    }
}
