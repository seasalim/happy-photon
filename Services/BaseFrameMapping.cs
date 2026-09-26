namespace HappyPhoton.Services;

/// <summary>Pixel centres in every oriented, lens-corrected base, with no decode phase correction.</summary>
public static class BaseFrameMapping
{
    public static double ToPixel(double coordinate, int extent) => coordinate * extent - .5;
    public static double ToNormalized(double pixel, int extent) => (pixel + .5) / extent;
}
