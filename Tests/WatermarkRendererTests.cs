using HappyPhoton.Models;
using HappyPhoton.Services;
using ImageMagick;
using SkiaSharp;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class WatermarkRendererTests
{
    public static IEnumerable<object[]> Positions =>
        from edge in Enum.GetValues<WatermarkEdge>()
        from alignment in Enum.GetValues<WatermarkAlignment>()
        from rotate in new[] { false, true }
        select new object[] { edge, alignment, rotate };

    [Theory]
    [MemberData(nameof(Positions))]
    public void ChangedPixelsMatchUnclippedFullImageReference(
        WatermarkEdge edge, WatermarkAlignment alignment, bool rotate)
    {
        var spec = new WatermarkSpec("F Jane ©", Size: 10, Edge: edge,
            Alignment: alignment, RotateAlongEdge: rotate, Margin: 7, Opacity: 100);
        using var image = Solid(600, 400);
        var before = Rgba(image);
        WatermarkRenderer.Apply(image, spec);
        var after = Rgba(image);
        var coverage = ExpectedCoverage(600, 400, spec);
        Assert.Contains(coverage, alpha => alpha > 0);
        for (var i = 0; i < 600 * 400; i++)
        {
            Assert.Equal(before[i * 4 + 3], after[i * 4 + 3]);
            var alpha = coverage[i] * (spec.Opacity / 25500);
            for (var channel = 0; channel < 3; channel++)
            {
                var offset = i * 4 + channel;
                var expected = (ushort)(before[offset] * (1 - alpha) + 65535 * alpha + 0.5);
                Assert.Equal(expected, after[offset]);
            }
        }
    }

    [Theory]
    [InlineData(OutputColorSpace.Srgb)]
    [InlineData(OutputColorSpace.DisplayP3)]
    public void ProofEqualsExportAtEachSingleOutputSize(OutputColorSpace color)
    {
        using var source = Solid(600, 400);
        foreach (var size in new int?[] { null, 300, 150 })
        {
            var spec = new WatermarkSpec("© Jane Doe 2026");
            using var export = RenderFinalizer.Finalize(source, size, color,
                OutputSharpeningMode.Screen, false, watermark: spec);
            using var proof = RenderFinalizer.FinalizeProof(source, size, color,
                OutputSharpeningMode.Screen, watermark: spec);
            using var off = RenderFinalizer.Finalize(source, size, color,
                OutputSharpeningMode.Screen, false);
            Assert.Equal(Rgba(export), Rgba(proof));
            Assert.NotEqual(Rgba(off), Rgba(export));
            using var expected = new MagickImage(off);
            Assert.Equal(Rgba(off), Rgba(expected));
            WatermarkRenderer.Apply(expected, spec);
            Assert.Equal(Rgba(expected), Rgba(export));
        }
    }

    [Fact]
    public void CoverageBlendsQ16InOutputEncodingAndPreservesAlpha()
    {
        var spec = new WatermarkSpec("Grayscale", Size: 15, Opacity: 100);
        using var mask = new MagickImage(MagickColors.Black, 600, 400);
        mask.ColorType = ColorType.TrueColor;
        WatermarkRenderer.Apply(mask, spec);
        using var pixels = mask.GetPixelsUnsafe();
        var coverage = pixels.ToShortArray(PixelMapping.RGB)!;
        Assert.Contains(coverage, value => value > 0 && value < 65535);
        foreach (var color in Enum.GetValues<WatermarkColor>())
        {
            using var image = Solid(600, 400);
            var before = Rgba(image);
            WatermarkRenderer.Apply(image, spec with { Color = color, Opacity = 60 });
            var after = Rgba(image);
            for (var i = 0; i < 600 * 400; i++)
            {
                Assert.Equal(before[i * 4 + 3], after[i * 4 + 3]);
                var a = coverage[i * 3] / 65535.0 * 0.6;
                for (var c = 0; c < 3; c++)
                {
                    var expected = (ushort)(before[i * 4 + c] * (1 - a) +
                        (color == WatermarkColor.White ? 65535 : 0) * a + 0.5);
                    Assert.Equal(expected, after[i * 4 + c]);
                }
            }
        }
    }

    [Fact]
    public void StyleTogglesChangeCoverageAndMissingFamilyFallsBack()
    {
        using var regular = Solid(600, 400);
        var spec = new WatermarkSpec("Jane Doe", Size: 12);
        WatermarkRenderer.Apply(regular, spec);
        foreach (var styled in new[] { spec with { Bold = true }, spec with { Italic = true },
                     spec with { FontFamily = "No Such Font 269", Bold = true },
                     spec with { FontFamily = "No Such Font 269", Italic = true } })
        {
            using var image = Solid(600, 400);
            WatermarkRenderer.Apply(image, styled);
            Assert.NotEqual(Rgba(regular), Rgba(image));
        }
        using var missing = Solid(600, 400);
        WatermarkRenderer.Apply(missing, spec with { FontFamily = "No Such Font 269" });
        Assert.Equal(Rgba(regular), Rgba(missing));
    }

    [Theory]
    [InlineData(WatermarkEdge.Bottom)]
    [InlineData(WatermarkEdge.Left)]
    [InlineData(WatermarkEdge.Right)]
    [InlineData(WatermarkEdge.Center)]
    public void LongTextFitsBetweenBothMargins(WatermarkEdge edge)
    {
        using var image = Solid(600, 400);
        var before = Rgba(image);
        WatermarkRenderer.Apply(image, new WatermarkSpec(new string('M', 200),
            Size: 20, Edge: edge, Margin: 10));
        var changed = Changed(image, before);
        Assert.NotEmpty(changed);
        Assert.All(changed, point =>
        {
            Assert.InRange(point.X, 40, 559);
            Assert.InRange(point.Y, 40, 359);
        });
    }

    [Fact]
    public void RotationSwapsBoxAndLettersFaceTheirEdge()
    {
        var spec = new WatermarkSpec("F", Size: 20, Opacity: 100, Alignment: WatermarkAlignment.Middle);
        using var right = Solid(600, 400);
        using var left = Solid(600, 400);
        var before = Rgba(right);
        WatermarkRenderer.Apply(right, spec with { Edge = WatermarkEdge.Right });
        WatermarkRenderer.Apply(left, spec with { Edge = WatermarkEdge.Left });
        var r = Changed(right, before);
        var l = Changed(left, before);
        // F's stem is at the start: top on the right, bottom on the left.
        Assert.True(r.Count(p => p.Y < (r.Min(p => p.Y) + r.Max(p => p.Y)) / 2.0) > r.Count / 2);
        Assert.True(l.Count(p => p.Y > (l.Min(p => p.Y) + l.Max(p => p.Y)) / 2.0) > l.Count / 2);
        var horizontal = ExpectedBox(600, 400, spec);
        var vertical = ExpectedBox(600, 400, spec with { Edge = WatermarkEdge.Right });
        Assert.Equal(horizontal.Width, vertical.Height, 3);
        Assert.Equal(horizontal.Height, vertical.Width, 3);
    }

    [Fact]
    public void InkHeightScalesWithShortEdgeAndCenterIgnoresAlignment()
    {
        double? ratio = null;
        foreach (var width in new[] { 1200, 600, 300 })
        {
            using var image = Solid(width, width * 2 / 3);
            var before = Rgba(image);
            var spec = new WatermarkSpec("Jane Doe", Size: 10, Edge: WatermarkEdge.Center);
            WatermarkRenderer.Apply(image, spec);
            var changed = Changed(image, before);
            var height = changed.Max(p => p.Y) - changed.Min(p => p.Y) + 1;
            ratio ??= height / (width * 2 / 3.0);
            Assert.InRange(Math.Abs(height - ratio.Value * width * 2 / 3), 0, 1);
            using var other = Solid(width, width * 2 / 3);
            WatermarkRenderer.Apply(other, spec with { Alignment = WatermarkAlignment.Start });
            Assert.Equal(Rgba(image), Rgba(other));
        }
    }

    [Theory]
    [InlineData(WatermarkEdge.Top)]
    [InlineData(WatermarkEdge.Left)]
    [InlineData(WatermarkEdge.Right)]
    public void SyntheticItalicMatchesUnclippedFullImageMask(WatermarkEdge edge)
    {
        // Windows may synthesize oblique in MatchFamily itself. Require that the
        // installed family has no italic face, then mirror only the fallback choice.
        var family = SKFontManager.Default.FontFamilies.FirstOrDefault(name =>
        {
            using var styles = SKFontManager.Default.GetFontStyles(name);
            if (styles.Any(style => style.Slant == SKFontStyleSlant.Italic)) return false;
            using var face = SKFontManager.Default.MatchFamily(name, SKFontStyle.Italic);
            if (face == null || face.FamilyName != name) return false;
            using var probe = new SKFont(face, 80) { SkewX = face.IsItalic ? 0 : -0.25f };
            var advance = probe.MeasureText("f", out var ink);
            return ink.Right > advance + 2;
        });
        Assert.NotNull(family);
        using var typeface = SKFontManager.Default.MatchFamily(family, SKFontStyle.Italic);
        using var font = new SKFont(typeface, 80)
        {
            Edging = SKFontEdging.Antialias, Subpixel = false,
            SkewX = typeface.IsItalic ? 0 : -0.25f
        };
        var advance = font.MeasureText("f");
        var spec = new WatermarkSpec("f", family, Italic: true, Size: 20, Opacity: 100,
            Edge: edge, Alignment: WatermarkAlignment.Start, Margin: 10);
        using var expected = new SKBitmap(new SKImageInfo(600, 400, SKColorType.Alpha8, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(expected))
        using (var paint = new SKPaint { Color = SKColors.White, IsAntialias = true })
        {
            canvas.Clear(SKColors.Transparent);
            if (edge == WatermarkEdge.Right)
            {
                canvas.Translate(560, 40);
                canvas.RotateDegrees(90);
            }
            else if (edge == WatermarkEdge.Left)
            {
                canvas.Translate(40, 40 + advance);
                canvas.RotateDegrees(-90);
            }
            else canvas.Translate(40, 40);
            canvas.DrawText("f", 0, -font.Metrics.Ascent, font, paint);
        }
        var coverage = new byte[expected.ByteCount];
        System.Runtime.InteropServices.Marshal.Copy(expected.GetPixels(), coverage, 0, coverage.Length);
        using var actual = new MagickImage(MagickColors.Black, 600, 400);
        WatermarkRenderer.Apply(actual, spec);
        using var pixels = actual.GetPixelsUnsafe();
        var rgb = pixels.ToShortArray(PixelMapping.RGB)!;
        var outsideAdvance = 0;
        for (var y = 0; y < 400; y++)
        for (var x = 0; x < 600; x++)
        {
            var alpha = coverage[y * expected.RowBytes + x];
            Assert.Equal((ushort)(alpha * 257), rgb[(y * 600 + x) * 3]);
            var pastEnd = edge == WatermarkEdge.Top ? x >= Math.Ceiling(40 + advance) :
                edge == WatermarkEdge.Right ? y >= Math.Ceiling(40 + advance) : y < 40;
            if (pastEnd && alpha > 0) outsideAdvance++;
        }
        Assert.True(outsideAdvance > 0, "The reference must contain ink beyond the layout advance.");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OverlongItalicInkFitsAlongEdgeMargins(bool synthetic)
    {
        var family = SKFontManager.Default.FontFamilies.FirstOrDefault(name =>
        {
            using var styles = SKFontManager.Default.GetFontStyles(name);
            if (styles.Any(style => style.Slant == SKFontStyleSlant.Italic) == synthetic) return false;
            using var face = SKFontManager.Default.MatchFamily(name, SKFontStyle.Italic);
            if (face == null || face.FamilyName != name) return false;
            using var font = new SKFont(face, 80) { SkewX = face.IsItalic ? 0 : -0.25f };
            var advance = font.MeasureText("ffffffffffffffffffff", out var ink);
            return advance > 520 && (ink.Left < -1 || ink.Right > advance + 1);
        });
        Assert.NotNull(family);
        foreach (var edge in Enum.GetValues<WatermarkEdge>())
        foreach (var alignment in Enum.GetValues<WatermarkAlignment>())
        foreach (var rotate in new[] { false, true })
        {
            using var image = Solid(600, 400);
            var before = Rgba(image);
            WatermarkRenderer.Apply(image, new WatermarkSpec("ffffffffffffffffffff", family,
                Italic: true, Size: 20, Edge: edge, Alignment: alignment,
                RotateAlongEdge: rotate, Margin: 10));
            var changed = Changed(image, before);
            Assert.NotEmpty(changed);
            var vertical = rotate && edge is WatermarkEdge.Left or WatermarkEdge.Right;
            var minimum = changed.Min(p => vertical ? p.Y : p.X);
            var maximum = changed.Max(p => vertical ? p.Y : p.X);
            var limit = vertical ? 359 : 559;
            Assert.True(minimum >= 40 && maximum <= limit,
                $"{family}, synthetic={synthetic}, {edge}/{alignment}, rotate={rotate}: " +
                $"ink {minimum}…{maximum}, allowed 40…{limit}");
        }
    }

    internal static MagickImage Solid(int width, int height)
    {
        var image = new MagickImage(new MagickColor(14000, 26000, 38000, 45000),
            (uint)width, (uint)height);
        image.ColorType = ColorType.TrueColorAlpha;
        return image;
    }

    internal static ushort[] Rgba(MagickImage image)
    {
        using var pixels = image.GetPixelsUnsafe();
        return pixels.ToShortArray(PixelMapping.RGBA)!;
    }

    private static List<(int X, int Y)> Changed(MagickImage image, ushort[] before)
    {
        var after = Rgba(image);
        return Enumerable.Range(0, (int)(image.Width * image.Height))
            .Where(i => after[i * 4] != before[i * 4])
            .Select(i => (i % (int)image.Width, i / (int)image.Width)).ToList();
    }

    private static byte[] ExpectedCoverage(int width, int height, WatermarkSpec spec)
    {
        // Draw on a full-image canvas: the oracle has no production mask bounds
        // and preserves platform glyph overhangs beyond the advance/metrics box.
        var box = ExpectedBox(width, height, spec);
        using var typeface = SKFontManager.Default.MatchFamily(spec.FontFamily, SKFontStyle.Normal);
        using var font = new SKFont(typeface ?? SKTypeface.Default,
            (float)(Math.Min(width, height) * spec.Size / 100))
        {
            Edging = SKFontEdging.Antialias, Subpixel = false
        };
        using var mask = new SKBitmap(new SKImageInfo(width, height, SKColorType.Alpha8, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(mask))
        using (var paint = new SKPaint { Color = SKColors.White, IsAntialias = true })
        {
            canvas.Clear(SKColors.Transparent);
            if (spec.RotateAlongEdge && spec.Edge == WatermarkEdge.Right)
            {
                canvas.Translate(box.Right, box.Top);
                canvas.RotateDegrees(90);
            }
            else if (spec.RotateAlongEdge && spec.Edge == WatermarkEdge.Left)
            {
                canvas.Translate(box.Left, box.Bottom);
                canvas.RotateDegrees(-90);
            }
            else canvas.Translate(box.Left, box.Top);
            canvas.DrawText(spec.Text, 0, -font.Metrics.Ascent, font, paint);
        }
        var coverage = new byte[mask.ByteCount];
        System.Runtime.InteropServices.Marshal.Copy(mask.GetPixels(), coverage, 0, coverage.Length);
        Assert.Equal(width, mask.RowBytes);
        return coverage;
    }

    private static SKRect ExpectedBox(int width, int height, WatermarkSpec spec)
    {
        using var typeface = SKFontManager.Default.MatchFamily(spec.FontFamily, SKFontStyle.Normal);
        using var font = new SKFont(typeface ?? SKTypeface.Default,
            (float)(Math.Min(width, height) * spec.Size / 100))
        {
            Edging = SKFontEdging.Antialias, Subpixel = false
        };
        var w = font.MeasureText(spec.Text);
        var h = font.Metrics.Descent - font.Metrics.Ascent;
        if (spec.RotateAlongEdge && spec.Edge is WatermarkEdge.Left or WatermarkEdge.Right) (w, h) = (h, w);
        var margin = (float)(Math.Min(width, height) * spec.Margin / 100);
        float Along(float length, float extent) => spec.Alignment switch
        {
            WatermarkAlignment.Start => margin,
            WatermarkAlignment.Middle => (length - extent) / 2,
            _ => length - margin - extent
        };
        var x = spec.Edge == WatermarkEdge.Left ? margin : spec.Edge == WatermarkEdge.Right ? width - margin - w :
            spec.Edge == WatermarkEdge.Center ? (width - w) / 2 : Along(width, w);
        var y = spec.Edge == WatermarkEdge.Top ? margin : spec.Edge == WatermarkEdge.Bottom ? height - margin - h :
            spec.Edge == WatermarkEdge.Center ? (height - h) / 2 : Along(height, h);
        return new SKRect(x, y, x + w, y + h);
    }
}
