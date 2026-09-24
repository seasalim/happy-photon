using HappyPhoton.Models;
using ImageMagick;
using SkiaSharp;
using static HappyPhoton.Services.RenderKernelSupport;

namespace HappyPhoton.Services;

internal static class WatermarkRenderer
{
    internal static void Apply(MagickImage image, WatermarkSpec spec)
    {
        if (string.IsNullOrWhiteSpace(spec.Text)) return;
        using var typeface = SKFontManager.Default.MatchFamily(spec.FontFamily,
            new SKFontStyle(spec.Bold ? SKFontStyleWeight.Bold : SKFontStyleWeight.Normal,
                SKFontStyleWidth.Normal, spec.Italic ? SKFontStyleSlant.Italic : SKFontStyleSlant.Upright));
        using var font = new SKFont(typeface ?? SKTypeface.Default,
            (float)(Math.Min(image.Width, image.Height) * spec.Size / 100))
        {
            Edging = SKFontEdging.Antialias,
            Subpixel = false,
            Embolden = spec.Bold && !(typeface?.IsBold ?? false),
            SkewX = spec.Italic && !(typeface?.IsItalic ?? false) ? -0.25f : 0
        };
        var rotated = spec.RotateAlongEdge && spec.Edge is WatermarkEdge.Left or WatermarkEdge.Right;
        var margin = (float)(Math.Min(image.Width, image.Height) * spec.Margin / 100);
        var advance = font.MeasureText(spec.Text, out var ink);
        var height = font.Metrics.Descent - font.Metrics.Ascent;
        ink.Offset(0, -font.Metrics.Ascent);
        var bounds = new SKRect(Math.Min(0, ink.Left), Math.Min(0, ink.Top),
            Math.Max(advance, ink.Right), Math.Max(height, ink.Bottom));
        var availableWidth = (rotated ? image.Height : image.Width) - 2 * margin;
        var availableHeight = (rotated ? image.Width : image.Height) - 2 * margin;
        var scale = Math.Min(1, Math.Min(availableWidth / Math.Max(bounds.Width, 1),
            availableHeight / Math.Max(bounds.Height, 1)));
        if (scale <= 0) return;
        advance *= scale;
        height *= scale;
        bounds = new SKRect(bounds.Left * scale, bounds.Top * scale,
            bounds.Right * scale, bounds.Bottom * scale);
        var width = rotated ? height : advance;
        var boxHeight = rotated ? advance : height;
        float Along(float length, float box) => spec.Alignment switch
        {
            WatermarkAlignment.Start => margin,
            WatermarkAlignment.Middle => (length - box) / 2,
            _ => length - margin - box
        };
        var x = spec.Edge switch
        {
            WatermarkEdge.Left => margin,
            WatermarkEdge.Right => image.Width - margin - width,
            WatermarkEdge.Center => (image.Width - width) / 2,
            _ => Along(image.Width, width)
        };
        var y = spec.Edge switch
        {
            WatermarkEdge.Top => margin,
            WatermarkEdge.Bottom => image.Height - margin - boxHeight,
            WatermarkEdge.Center => (image.Height - boxHeight) / 2,
            _ => Along(image.Height, boxHeight)
        };
        if (rotated)
            bounds = spec.Edge == WatermarkEdge.Right
                ? new SKRect(width - bounds.Bottom, bounds.Left, width - bounds.Top, bounds.Right)
                : new SKRect(bounds.Top, boxHeight - bounds.Right, bounds.Bottom, boxHeight - bounds.Left);
        // Keep layout-box placement for text that fits. A shrunk mark must also
        // bring its overhanging ink inside the margins, without clipping it.
        if (scale < 1)
        {
            x = Math.Max(margin - bounds.Left, Math.Min(x, image.Width - margin - bounds.Right));
            y = Math.Max(margin - bounds.Top, Math.Min(y, image.Height - margin - bounds.Bottom));
        }
        var left = Math.Max(0, (int)Math.Floor(x + bounds.Left));
        var top = Math.Max(0, (int)Math.Floor(y + bounds.Top));
        var maskWidth = Math.Min((int)image.Width, (int)Math.Ceiling(x + bounds.Right)) - left;
        var maskHeight = Math.Min((int)image.Height, (int)Math.Ceiling(y + bounds.Bottom)) - top;
        if (maskWidth <= 0 || maskHeight <= 0) return;
        using var mask = new SKBitmap(new SKImageInfo(maskWidth, maskHeight,
            SKColorType.Alpha8, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(mask))
        using (var paint = new SKPaint { Color = SKColors.White, IsAntialias = true })
        {
            canvas.Clear(SKColors.Transparent);
            canvas.Translate(x - left, y - top);
            if (rotated)
            {
                canvas.Translate(spec.Edge == WatermarkEdge.Right ? width : 0,
                    spec.Edge == WatermarkEdge.Left ? boxHeight : 0);
                canvas.RotateDegrees(spec.Edge == WatermarkEdge.Left ? -90 : 90);
            }
            canvas.Scale(scale);
            canvas.DrawText(spec.Text, 0, -font.Metrics.Ascent, font, paint);
        }
        Blend(image, mask, left, top, spec);
    }

    private static unsafe void Blend(MagickImage image, SKBitmap mask, int x, int y, WatermarkSpec spec)
    {
        // Detach a shared cache before Magick queues a partial-area write.
        image.CopyPixels(image, new MagickGeometry(x, y, 1, 1), x, y);
        using var pixels = image.GetPixelsUnsafe();
        var layout = GetLayout(pixels);
        var values = pixels.GetArea(x, y, (uint)mask.Width, (uint)mask.Height)!;
        var coverage = (byte*)mask.GetPixels();
        var opacity = spec.Opacity / 25500;
        var color = spec.Color == WatermarkColor.White ? ushort.MaxValue : 0;
        int[] channels = [layout.Red, layout.Green, layout.Blue];
        for (var row = 0; row < mask.Height; row++)
        for (var column = 0; column < mask.Width; column++)
        {
            var a = coverage[row * mask.RowBytes + column] * opacity;
            if (a == 0) continue;
            var offset = (row * mask.Width + column) * layout.Channels;
            foreach (var channel in channels)
            {
                var index = offset + channel;
                values[index] = (ushort)(values[index] * (1 - a) + color * a + 0.5);
            }
        }
        pixels.SetArea(x, y, (uint)mask.Width, (uint)mask.Height, values);
    }
}
