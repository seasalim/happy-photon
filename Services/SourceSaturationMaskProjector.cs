using System.Runtime.CompilerServices;
using HappyPhoton.Models;

namespace HappyPhoton.Services;

internal sealed record SourceSaturationProjection(
    SourceSaturationMask Mask,
    ChannelClip High,
    double HighAny);

internal static class SourceSaturationMaskProjector
{
    private static readonly ConditionalWeakTable<BaseImage, ProjectionSlot> Cache = new();

    internal static SourceSaturationProjection? Project(
        BaseImage image,
        EditSettings settings,
        RenderGeometryTrace geometry,
        int targetWidth,
        int targetHeight)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(settings);
        var source = image.SourceSaturation;
        if (source == null) return null;

        var key = ProjectionKey.Create(settings, geometry, targetWidth, targetHeight);
        var slot = Cache.GetOrCreateValue(image);
        lock (slot)
        {
            if (slot.Key == key && slot.Value != null)
            {
                return slot.Value;
            }

            var projected = ProjectCore(
                source,
                settings,
                geometry,
                targetWidth,
                targetHeight);
            slot.Key = key;
            slot.Value = projected;
            return projected;
        }
    }

    private static SourceSaturationProjection ProjectCore(
        SourceSaturationMask source,
        EditSettings settings,
        RenderGeometryTrace geometry,
        int targetWidth,
        int targetHeight)
    {
        if (settings.Rotation == 0 && settings.HorizonRotation == 0 &&
            geometry.CropX == 0 && geometry.CropY == 0 &&
            geometry.Width == source.Width && geometry.Height == source.Height &&
            targetWidth == source.Width && targetHeight == source.Height)
        {
            return CreateProjection(source);
        }

        var result = new SourceSaturationMask(targetWidth, targetHeight);
        var radians = settings.HorizonRotation * Math.PI / 180;
        var cosine = Math.Cos(radians);
        var sine = Math.Sin(radians);
        var sourceCenterX = (geometry.QuarterTurnWidth - 1) / 2d;
        var sourceCenterY = (geometry.QuarterTurnHeight - 1) / 2d;
        var canvasCenterX = (geometry.HorizonCanvasWidth - 1) / 2d;
        var canvasCenterY = (geometry.HorizonCanvasHeight - 1) / 2d;

        for (var y = 0; y < source.Height; y++)
        {
            for (var x = 0; x < source.Width; x++)
            {
                var flags = source.GetFlags(x, y);
                if (flags == 0) continue;
                var (projectedX, projectedY) = QuarterTurn(
                    x,
                    y,
                    source.Width,
                    source.Height,
                    settings.Rotation);
                if (settings.HorizonRotation != 0)
                {
                    var centeredX = projectedX - sourceCenterX;
                    var centeredY = projectedY - sourceCenterY;
                    projectedX = checked((int)Math.Round(
                        cosine * centeredX - sine * centeredY + canvasCenterX,
                        MidpointRounding.AwayFromZero));
                    projectedY = checked((int)Math.Round(
                        sine * centeredX + cosine * centeredY + canvasCenterY,
                        MidpointRounding.AwayFromZero));
                }
                projectedX -= geometry.CropX;
                projectedY -= geometry.CropY;
                if (projectedX < 0 || projectedY < 0 ||
                    projectedX >= geometry.Width || projectedY >= geometry.Height)
                {
                    continue;
                }
                result.SetMappedPixel(
                    projectedX,
                    projectedY,
                    geometry.Width,
                    geometry.Height,
                    flags);
            }
        }
        return CreateProjection(result);
    }

    private static SourceSaturationProjection CreateProjection(
        SourceSaturationMask mask)
    {
        long red = 0, green = 0, blue = 0, any = 0;
        for (var y = 0; y < mask.Height; y++)
        {
            for (var x = 0; x < mask.Width; x++)
            {
                var flags = mask.GetFlags(x, y);
                if ((flags & 1) != 0) red++;
                if ((flags & 2) != 0) green++;
                if ((flags & 4) != 0) blue++;
                if (flags != 0) any++;
            }
        }
        var divisor = (double)checked(mask.Width * mask.Height);
        return new SourceSaturationProjection(
            mask,
            new ChannelClip(red / divisor, green / divisor, blue / divisor),
            any / divisor);
    }

    private static (int X, int Y) QuarterTurn(
        int x,
        int y,
        int width,
        int height,
        int rotation) =>
        rotation switch
        {
            0 => (x, y),
            90 => (height - 1 - y, x),
            180 => (width - 1 - x, height - 1 - y),
            270 => (y, width - 1 - x),
            _ => throw new ArgumentOutOfRangeException(nameof(rotation))
        };

    private sealed class ProjectionSlot
    {
        internal ProjectionKey? Key { get; set; }
        internal SourceSaturationProjection? Value { get; set; }
    }

    private sealed record ProjectionKey(
        int Rotation,
        double Horizon,
        double? CropLeft,
        double? CropTop,
        double? CropRight,
        double? CropBottom,
        RenderGeometryTrace Geometry,
        int Width,
        int Height)
    {
        internal static ProjectionKey Create(
            EditSettings settings,
            RenderGeometryTrace geometry,
            int width,
            int height) =>
            new(
                settings.Rotation,
                settings.HorizonRotation,
                settings.Crop?.Left,
                settings.Crop?.Top,
                settings.Crop?.Right,
                settings.Crop?.Bottom,
                geometry,
                width,
                height);
    }
}
