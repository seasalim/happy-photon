using HappyPhoton.Models;
using ImageMagick;

namespace HappyPhoton.Services;

internal readonly record struct RenderGeometryTrace(
    int QuarterTurnWidth,
    int QuarterTurnHeight,
    int CorrectedFrameWidth,
    int CorrectedFrameHeight,
    int CropX,
    int CropY,
    int Width,
    int Height,
    RenderGeometryMap Map);

internal static class RenderGeometry
{
    public static MagickImage Apply(
        MagickImage source,
        EditSettings settings,
        out RenderGeometryTrace trace)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(settings);

        MagickImage? owned = null;
        try
        {
            if (settings.Rotation != 0)
            {
                owned = new MagickImage(source);
                owned.Rotate(settings.Rotation);
                owned.ResetPage();
            }

            var geometrySource = owned ?? source;
            var quarterTurnWidth = checked((int)geometrySource.Width);
            var quarterTurnHeight = checked((int)geometrySource.Height);
            var map = new RenderGeometryMap(
                quarterTurnWidth,
                quarterTurnHeight,
                settings.HorizonRotation,
                settings.Geometry);
            if (!map.IsIdentity)
            {
                var warped = GeometryWarpProcessor.Apply(geometrySource, map);
                owned?.Dispose();
                owned = warped;
            }
            else if (owned == null)
            {
                owned = new MagickImage(source);
            }

            var correctedFrameWidth = checked((int)owned.Width);
            var correctedFrameHeight = checked((int)owned.Height);
            var cropX = 0;
            var cropY = 0;
            var width = correctedFrameWidth;
            var height = correctedFrameHeight;
            if (settings.Crop is { IsFullImage: false } crop)
            {
                (cropX, cropY, width, height) = crop.ToPixels(
                    correctedFrameWidth,
                    correctedFrameHeight);
                owned.Crop(new MagickGeometry(
                    cropX,
                    cropY,
                    (uint)width,
                    (uint)height));
                owned.ResetPage();
            }

            trace = new RenderGeometryTrace(
                quarterTurnWidth,
                quarterTurnHeight,
                correctedFrameWidth,
                correctedFrameHeight,
                cropX,
                cropY,
                width,
                height,
                map);
            var result = owned;
            owned = null;
            return result;
        }
        finally
        {
            owned?.Dispose();
        }
    }
}
