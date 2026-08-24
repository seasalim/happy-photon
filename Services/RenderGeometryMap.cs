using System.Runtime.CompilerServices;
using HappyPhoton.Models;

namespace HappyPhoton.Services;

internal sealed class RenderGeometryMap
{
    private readonly double _halfWidth;
    private readonly double _halfHeight;
    private readonly double _halfDiagonal;
    private readonly double _vertical;
    private readonly double _horizontal;
    private readonly double _aspectX;
    private readonly double _aspectY;
    private readonly double _radial;
    private readonly double _cosine;
    private readonly double _sine;

    internal int SourceWidth { get; }
    internal int SourceHeight { get; }
    internal int OutputWidth { get; }
    internal int OutputHeight { get; }
    internal bool IsIdentity { get; }

    internal RenderGeometryMap(
        int sourceWidth,
        int sourceHeight,
        double horizonRotation,
        GeometrySettings? geometry)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sourceWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sourceHeight);
        SourceWidth = sourceWidth;
        SourceHeight = sourceHeight;
        _halfWidth = Math.Max(0.5, (sourceWidth - 1) / 2d);
        _halfHeight = Math.Max(0.5, (sourceHeight - 1) / 2d);
        _halfDiagonal = Math.Sqrt(
            _halfWidth * _halfWidth + _halfHeight * _halfHeight);
        _vertical = -(geometry?.Vertical ?? 0) / 200d;
        _horizontal = -(geometry?.Horizontal ?? 0) / 200d;
        var aspect = (geometry?.Aspect ?? 0) / 400d;
        _aspectX = Math.Exp(aspect);
        _aspectY = Math.Exp(-aspect);
        _radial = -(geometry?.Distortion ?? 0) / 400d;
        var radians = horizonRotation * Math.PI / 180d;
        _cosine = Math.Cos(radians);
        _sine = Math.Sin(radians);
        IsIdentity = horizonRotation == 0 && geometry?.IsIdentity != false;

        var scale = IsIdentity ? 1d : FindCoverScale();
        OutputWidth = ScaledDimension(sourceWidth, scale);
        OutputHeight = ScaledDimension(sourceHeight, scale);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal GeometryPoint MapInverse(double x, double y)
    {
        var centeredX = x - (OutputWidth - 1) / 2d;
        var centeredY = y - (OutputHeight - 1) / 2d;
        var normalizedX = centeredX / _halfWidth;
        var normalizedY = centeredY / _halfHeight;
        var denominator = 1 +
            _vertical * normalizedY + _horizontal * normalizedX;
        var radialX = normalizedX / denominator * _halfWidth * _aspectX;
        var radialY = normalizedY / denominator * _halfHeight * _aspectY;
        ApplyRadial(ref radialX, ref radialY);
        return new GeometryPoint(
            _cosine * radialX + _sine * radialY + (SourceWidth - 1) / 2d,
            -_sine * radialX + _cosine * radialY + (SourceHeight - 1) / 2d);
    }

    internal GeometryPoint MapForward(double sourceX, double sourceY)
    {
        var sourceCenteredX = sourceX - (SourceWidth - 1) / 2d;
        var sourceCenteredY = sourceY - (SourceHeight - 1) / 2d;
        var radialX = _cosine * sourceCenteredX - _sine * sourceCenteredY;
        var radialY = _sine * sourceCenteredX + _cosine * sourceCenteredY;
        RemoveRadial(ref radialX, ref radialY);
        var projectedX = radialX / _aspectX / _halfWidth;
        var projectedY = radialY / _aspectY / _halfHeight;
        var denominator = 1 -
            _horizontal * projectedX - _vertical * projectedY;
        return new GeometryPoint(
            projectedX / denominator * _halfWidth + (OutputWidth - 1) / 2d,
            projectedY / denominator * _halfHeight + (OutputHeight - 1) / 2d);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ApplyRadial(ref double x, ref double y)
    {
        if (_radial == 0) return;
        var radius = Math.Sqrt(x * x + y * y) / _halfDiagonal;
        if (radius == 0) return;
        var mapped = radius <= 1
            ? radius * (1 + _radial * radius * radius)
            : 1 + _radial + (1 + 3 * _radial) * (radius - 1);
        var scale = mapped / radius;
        x *= scale;
        y *= scale;
    }

    private void RemoveRadial(ref double x, ref double y)
    {
        if (_radial == 0) return;
        var mapped = Math.Sqrt(x * x + y * y) / _halfDiagonal;
        if (mapped == 0) return;
        var knee = 1 + _radial;
        var radius = mapped > knee
            ? 1 + (mapped - knee) / (1 + 3 * _radial)
            : SolveCubic(mapped);
        var scale = radius / mapped;
        x *= scale;
        y *= scale;
    }

    private double SolveCubic(double mapped)
    {
        if (_radial > 0)
        {
            var p = 1 / _radial;
            var q = -mapped / _radial;
            var root = Math.Sqrt(q * q / 4 + p * p * p / 27);
            return Math.Cbrt(-q / 2 + root) + Math.Cbrt(-q / 2 - root);
        }

        var negativeP = -1 / _radial;
        var amplitude = 2 * Math.Sqrt(negativeP / 3);
        var argument = Math.Clamp(
            -3 * mapped / 2 * Math.Sqrt(3 / negativeP), -1, 1);
        return amplitude * Math.Cos((Math.Acos(argument) - 2 * Math.PI) / 3);
    }

    private double FindCoverScale()
    {
        if (Fits(SourceWidth, SourceHeight)) return 1;
        var low = 0d;
        var high = 1d;
        for (var iteration = 0; iteration < 30; iteration++)
        {
            var middle = (low + high) / 2;
            var width = ScaledDimension(SourceWidth, middle);
            var height = ScaledDimension(SourceHeight, middle);
            if (Fits(width, height)) low = middle; else high = middle;
        }
        return low;
    }

    private bool Fits(int width, int height)
    {
        var maxX = width - 1d;
        var maxY = height - 1d;
        for (var x = 0; x < width; x++)
        {
            if (!Fits(MapInverseForSize(x, 0, width, height)) ||
                !Fits(MapInverseForSize(x, maxY, width, height))) return false;
        }
        for (var y = 1; y < height - 1; y++)
        {
            if (!Fits(MapInverseForSize(0, y, width, height)) ||
                !Fits(MapInverseForSize(maxX, y, width, height))) return false;
        }
        return true;
    }

    private GeometryPoint MapInverseForSize(
        double x,
        double y,
        int width,
        int height)
    {
        var centeredX = x - (width - 1) / 2d;
        var centeredY = y - (height - 1) / 2d;
        var normalizedX = centeredX / _halfWidth;
        var normalizedY = centeredY / _halfHeight;
        var denominator = 1 +
            _vertical * normalizedY + _horizontal * normalizedX;
        if (denominator <= 0.000001) return new GeometryPoint(double.NaN, double.NaN);
        var radialX = normalizedX / denominator * _halfWidth * _aspectX;
        var radialY = normalizedY / denominator * _halfHeight * _aspectY;
        ApplyRadial(ref radialX, ref radialY);
        return new GeometryPoint(
            _cosine * radialX + _sine * radialY + (SourceWidth - 1) / 2d,
            -_sine * radialX + _cosine * radialY + (SourceHeight - 1) / 2d);
    }

    private bool Fits(GeometryPoint point) =>
        double.IsFinite(point.X) && double.IsFinite(point.Y) &&
        point.X >= 0 && point.X <= SourceWidth - 1 &&
        point.Y >= 0 && point.Y <= SourceHeight - 1;

    private static int ScaledDimension(int dimension, double scale) =>
        Math.Max(1, checked((int)Math.Floor((dimension - 1) * scale) + 1));
}

internal readonly record struct GeometryPoint(double X, double Y);
