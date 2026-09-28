using Avalonia;
using HappyPhoton.Models;

namespace HappyPhoton.Services;

/// <summary>Base-normalized coordinates mapped through the geometry of one displayed render.</summary>
public sealed class RepairDisplayMap
{
    private readonly RenderGeometryMap _map;

    private readonly int _rotation;

    private readonly int _cropX;

    private readonly int _cropY;

    private readonly int _width;

    private readonly int _height;

    public int BaseWidth { get; }

    public int BaseHeight { get; }

    internal RepairDisplayMap(int width, int height, EditSettings settings)
    {
        BaseWidth = width;
        BaseHeight = height;
        _rotation = settings.Rotation;
        if (_rotation is 90 or 270) (width, height) = (height, width);
        _map = new RenderGeometryMap(width, height, settings.HorizonRotation, settings.Geometry);
        (_cropX, _cropY, _width, _height) = settings.Crop is { IsFullImage: false } crop
            ? crop.ToPixels(_map.OutputWidth, _map.OutputHeight)
            : (0, 0, _map.OutputWidth, _map.OutputHeight);
    }

    public Point ToDisplay(Point point)
    {
        var turned = QuarterTurn(point.X * BaseWidth - .5, point.Y * BaseHeight - .5,
            BaseWidth, BaseHeight, _rotation);
        var mapped = _map.MapForward(turned.X, turned.Y);
        return new((mapped.X - _cropX + .5) / _width, (mapped.Y - _cropY + .5) / _height);
    }

    public Point ToBase(Point point)
    {
        var mapped = _map.MapInverse(point.X * _width + _cropX - .5, point.Y * _height + _cropY - .5);
        var unturned = QuarterTurn(mapped.X, mapped.Y, _map.SourceWidth, _map.SourceHeight,
            (360 - _rotation) % 360);
        return new((unturned.X + .5) / BaseWidth, (unturned.Y + .5) / BaseHeight);
    }

    internal static (double X, double Y) QuarterTurn(double x, double y, int width, int height, int rotation) =>
        rotation switch
        {
            0 => (x, y),
            90 => (height - 1 - y, x),
            180 => (width - 1 - x, height - 1 - y),
            270 => (y, width - 1 - x),
            _ => throw new ArgumentOutOfRangeException(nameof(rotation))
        };
}
