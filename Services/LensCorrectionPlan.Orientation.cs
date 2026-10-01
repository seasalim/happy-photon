namespace HappyPhoton.Services;

internal sealed partial class LensCorrectionPlan
{
    private readonly int _sensorOrientation;

    private readonly int _bufferWidth;

    private readonly int _bufferHeight;

    // The complete prescription is evaluated in sensor coordinates; only the
    // final sample coordinate returns to LibRaw's already-oriented buffer.
    private LensPoint ToBuffer(LensPoint point)
    {
        var x = point.X * _sourceScaleX + _sourceOffsetX;
        var y = point.Y * _sourceScaleY + _sourceOffsetY;

        return _sensorOrientation switch
        {
            2 => new(_bufferWidth - 1 - x, y),
            3 => new(_bufferWidth - 1 - x, _bufferHeight - 1 - y),
            4 => new(x, _bufferHeight - 1 - y),
            5 => new(y, x),
            6 => new(_bufferWidth - 1 - y, x),
            7 => new(_bufferWidth - 1 - y, _bufferHeight - 1 - x),
            8 => new(y, _bufferHeight - 1 - x),
            _ => new(x, y)
        };
    }

    private static PixelAffine OrientedPixelAffine(
        int width,
        int height,
        int orientation)
    {
        var halfX = 0.5 / width;
        var halfY = 0.5 / height;
        var stepX = 1.0 / width;
        var stepY = 1.0 / height;

        return orientation switch
        {
            1 => new(halfX, halfY, stepX, 0, 0, stepY),
            2 => new(1 - halfX, halfY, -stepX, 0, 0, stepY),
            3 => new(1 - halfX, 1 - halfY, -stepX, 0, 0, -stepY),
            4 => new(halfX, 1 - halfY, stepX, 0, 0, -stepY),
            5 => new(halfY, halfX, 0, stepX, stepY, 0),
            6 => new(halfY, 1 - halfX, 0, -stepX, stepY, 0),
            7 => new(1 - halfY, 1 - halfX, 0, -stepX, -stepY, 0),
            8 => new(1 - halfY, halfX, 0, stepX, -stepY, 0),
            _ => throw new ArgumentOutOfRangeException(nameof(orientation))
        };
    }

    private readonly record struct PixelAffine(
        double OriginX,
        double OriginY,
        double XStepX,
        double XStepY,
        double YStepX,
        double YStepY);

}
