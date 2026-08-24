using System.Runtime.CompilerServices;
using HappyPhoton.Models;

namespace HappyPhoton.Services;

internal sealed record SourceSaturationProjection(
    SourceSaturationMask Mask,
    ChannelClip High,
    double HighAny);

internal static class SourceSaturationMaskProjector
{
    private static readonly ConditionalWeakTable<SourceSaturationMask, ProjectionSlot>
        Cache = new();

    internal static SourceSaturationProjection? Project(
        SourceSaturationMask? source,
        EditSettings settings,
        RenderGeometryTrace geometry,
        int targetWidth,
        int targetHeight)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (source == null) return null;

        var key = ProjectionKey.Create(settings, geometry, targetWidth, targetHeight);
        var slot = Cache.GetOrCreateValue(source);
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
        if (settings.Rotation == 0 && geometry.Map.IsIdentity &&
            geometry.CropX == 0 && geometry.CropY == 0 &&
            geometry.Width == source.Width && geometry.Height == source.Height &&
            targetWidth == source.Width && targetHeight == source.Height)
        {
            return CreateProjection(source);
        }

        var result = new SourceSaturationMask(targetWidth, targetHeight);
        for (var y = 0; y < source.Height; y++)
        {
            for (var x = 0; x < source.Width; x++)
            {
                var flags = source.GetFlags(x, y);
                if (flags == 0) continue;
                var (quarterTurnX, quarterTurnY) = QuarterTurn(
                    x,
                    y,
                    source.Width,
                    source.Height,
                    settings.Rotation);
                var projected = geometry.Map.MapForward(
                    quarterTurnX,
                    quarterTurnY);
                if (!double.IsFinite(projected.X) ||
                    !double.IsFinite(projected.Y))
                {
                    continue;
                }
                var projectedX = checked((int)Math.Round(
                    projected.X,
                    MidpointRounding.AwayFromZero));
                var projectedY = checked((int)Math.Round(
                    projected.Y,
                    MidpointRounding.AwayFromZero));
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
