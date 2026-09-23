using System.Text.Json;
using Avalonia;
using HappyPhoton.Models;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class HueRangeTests
{
    [Fact]
    public void WindowAndClassificationMatchOracle()
    {
        foreach (var center in new[] { 0d, 240, 350 })
        foreach (var width in new[] { 0d, 60, 360 })
        foreach (var softness in new[] { 0d, 30, 90 })
        foreach (var chroma in new[] { 0d, .01, .025, .04, .2 })
        {
            var range = new HueRange { Enabled = true, Center = center, Width = width, Softness = softness };
            var oracle = new LocalsRangeOracle.HueWindow(center, width, softness);
            for (var hue = -360; hue <= 720; hue++)
                Assert.InRange(Math.Abs(oracle.Weight(hue) * LocalsRangeOracle.Reliability(chroma) -
                    HueWindow.Weight(range, hue, chroma)), 0, 1e-14);
        }
        foreach (var r in new[] { -.2, 0, .18, 1, 2 })
        foreach (var g in new[] { -.1, 0, .3, 1, 4 })
        foreach (var b in new[] { -.3, 0, .5, 1, 3 })
        {
            var oracle = LocalsRangeOracle.Classify(r, g, b); var actual = OklabColor.Classify(r, g, b);
            Assert.InRange(Math.Abs(oracle.L - actual.L), 0, 1e-14);
            Assert.InRange(Math.Abs(oracle.A - actual.A), 0, 1e-14);
            Assert.InRange(Math.Abs(oracle.B - actual.B), 0, 1e-14);
        }
    }

    [Fact]
    public void OptionalPersistenceClampsRejectsAndPreservesValueEquality()
    {
        var settings = new EditSettings { Locals = [new()] };
        var absent = EditSettingsJson.Serialize(settings);
        Assert.DoesNotContain("\"hue\":", absent);
        settings.Locals[0].Hue = new() { Enabled = false, Center = 350 };
        var saved = EditSettingsJson.Serialize(settings);
        var copy = EditSettingsJson.Deserialize(saved, out var clamped);
        Assert.False(clamped); Assert.Equal(saved, EditSettingsJson.Serialize(copy));
        var clone = copy.Clone(); clone.Locals![0].Hue = clone.Locals[0].Hue! with { Center = 10 };
        Assert.Equal(350, copy.Locals![0].Hue!.Center); Assert.False(copy.HasSameEdits(clone));
        EditSettingsTransfer.ApplySubset(new() { Exposure = 2 }, copy);
        Assert.Equal(350, copy.Locals[0].Hue!.Center); Assert.Null(EditSettingsTransfer.CopySubset(copy).Locals);
        foreach (var center in new[] { -10d, 360, 720 })
        {
            settings.Locals[0].Hue = new() { Enabled = true, Center = center, Width = -1, Softness = 120 };
            var bounded = EditSettingsJson.Deserialize(JsonSerializer.Serialize(settings), out clamped).Locals![0].Hue!;
            Assert.True(clamped); Assert.Equal(Math.Clamp(center, 0, Math.BitDecrement(360d)), bounded.Center);
            Assert.Equal(0, bounded.Width); Assert.Equal(90, bounded.Softness);
        }
        foreach (var property in new[] { "center", "width", "softness" })
        {
            var node = System.Text.Json.Nodes.JsonNode.Parse(saved)!;
            node["locals"]![0]!["hue"]![property] = 12345;
            Assert.Throws<JsonException>(() => EditSettingsJson.Deserialize(node.ToJsonString().Replace("12345", "1e999"), out _));
        }
        settings.Locals[0].Hue = new() { Center = double.NaN };
        Assert.Throws<JsonException>(() => EditSettingsJson.Serialize(settings));
        settings.Locals[0].Hue = null; Assert.Equal(absent, EditSettingsJson.Serialize(settings));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void PickerMatchesIndependentFootprintAndBasis(bool raw)
    {
        const int width = 320, height = 240;
        var random = new Random(263);
        var source = Enumerable.Range(0, width * height * 3).Select(_ => (ushort)random.Next(65536)).ToArray();
        var table = Enumerable.Range(0, 12).SelectMany(i => new[] { i % 2 == 0 ? 0f : 20f, .8f, .9f }).ToArray();
        var map = raw ? DcpHueSatRenderer.Prepare(new(6, 2, 1, false, table, null, 0)) : null;
        using var basis = RenderPipelineTestSupport.CreateBase(source, raw, height, hueSatMap: map);
        var settings = new EditSettings { Wb = new() { Mode = WbMode.Custom, Kelvin = 8500, Tint = 30 },
            Crop = new() { Left = .1, Top = .1, Right = .9, Bottom = .9 } };
        using var geometry = RenderGeometry.Apply(basis.Pixels, settings, out var trace);
        DcpHueSatRenderer.Apply(geometry, map);
        var values = RenderPipelineTestSupport.ReadPixels(geometry);
        var wb = new AgxCrossing.Matrix3x3(RenderChromaticStage.CreateWhiteBalanceMatrix(basis.Info, settings));
        foreach (var point in new[] { new Point(.4, .4), new Point(.1, .1), new Point(-.1, .5), new Point(.9, .9) })
        {
            var x = point.X * width - trace.CropX; var y = point.Y * height - trace.CropY;
            // Oracle footprint uses the corrected full frame; translate cropped samples into it.
            var oracle = LocalsRangeOracle.Pick(width, height, point.X * width, point.Y * height, pixel =>
            {
                var col = pixel % width - trace.CropX; var row = pixel / width - trace.CropY;
                col = Math.Clamp(col, 0, trace.Width - 1); row = Math.Clamp(row, 0, trace.Height - 1);
                var o = (row * trace.Width + col) * 3;
                var r = values[o] / 65535d; var g = values[o + 1] / 65535d; var b = values[o + 2] / 65535d;
                return LocalsRangeOracle.Classify(wb.Row0(r, g, b), wb.Row1(r, g, b), wb.Row2(r, g, b));
            });
            var actual = LocalRangeSampling.Pick(basis, settings, point, new(trace.Width, trace.Height));
            if (x < 0 || y < 0 || x >= trace.Width || y >= trace.Height) Assert.Equal("off-image", actual.Rejection);
            else if (point.X == .4)
            {
                Assert.Equal(oracle.Count, actual.Count); Assert.Equal(oracle.Rejection, actual.Rejection);
                if (oracle.Hue is { } hue) Assert.InRange(LocalsRangeOracle.Distance(hue, actual.Hue!.Value), 0, 1e-10);
            }
        }
        Assert.Equal(source, RenderPipelineTestSupport.ReadPixels(basis.Pixels));
        Assert.Equal("no matching base", LocalRangeSampling.Pick(null, settings, new(.5, .5), new(1, 1)).Rejection);
    }

    [Fact]
    public void ResizedPickerMapsCropAndDiskIntoSurfacePixels()
    {
        const int width = 1000, height = 800;
        var source = new ushort[width * height * 3];
        for (var i = 0; i < width * height; i++)
        {
            var rgb = OklabColor.ToLinearRec2020(new Oklch(.65, .15, (i % width) / 1000d));
            source[i * 3] = (ushort)Math.Round(rgb.Red * 65535);
            source[i * 3 + 1] = (ushort)Math.Round(rgb.Green * 65535);
            source[i * 3 + 2] = (ushort)Math.Round(rgb.Blue * 65535);
        }
        using var basis = RenderPipelineTestSupport.CreateBase(source, height: height);
        var settings = new EditSettings { Crop = new() { Left = .2, Top = .15, Right = .85, Bottom = .9 } };
        var size = new PixelSize(257, 239);
        using var surface = RenderGeometry.Apply(basis.Pixels, settings, out var trace);
        surface.Resize(new ImageMagick.MagickGeometry((uint)size.Width, (uint)size.Height) { IgnoreAspectRatio = true });
        var pixels = RenderPipelineTestSupport.ReadPixels(surface);
        foreach (var point in new[] { new Point(.4, .4), new Point(.2001, .1501) })
        {
            var clickX = (int)((point.X * width - trace.CropX) * size.Width / trace.Width);
            var clickY = (int)((point.Y * height - trace.CropY) * size.Height / trace.Height);
            double a = 0, b = 0;
            var count = 0;
            for (var row = 0; row < size.Height; row++)
            for (var col = 0; col < size.Width; col++)
            {
                var dx = ((col + .5) * trace.Width / size.Width + trace.CropX) / width - point.X;
                var dy = ((row + .5) * trace.Height / size.Height + trace.CropY - point.Y * height) / width;
                if (dx * dx + dy * dy > .004 * .004 && (col != clickX || row != clickY)) continue;
                var offset = (row * size.Width + col) * 3;
                var lab = LocalsRangeOracle.Classify(pixels[offset] / 65535d, pixels[offset + 1] / 65535d, pixels[offset + 2] / 65535d);
                a += lab.A; b += lab.B; count++;
            }
            var pick = LocalRangeSampling.Pick(basis, settings, point, size);
            Assert.Null(pick.Rejection); Assert.Equal(count, pick.Count);
            Assert.InRange(LocalsRangeOracle.Distance((Math.Atan2(b, a) * 180 / Math.PI + 360) % 360, pick.Hue!.Value), 0, 1e-10);
        }
        Assert.Equal("off-image", LocalRangeSampling.Pick(basis, settings, new(.19, .4), size).Rejection);
        Assert.Equal(source, RenderPipelineTestSupport.ReadPixels(basis.Pixels));
    }

    [Fact]
    public void PickerIncludesClickedPixelAndRejectsNeutralAndMixedColors()
    {
        using var tiny = RenderPipelineTestSupport.CreateBase([65535, 0, 0]);
        var picked = LocalRangeSampling.Pick(tiny, new(), new(.01, .01), new(1, 1));
        Assert.NotNull(picked.Hue); Assert.Equal(1, picked.Count);
        using var gray = RenderPipelineTestSupport.CreateBase([32768, 32768, 32768]);
        Assert.Equal("too neutral", LocalRangeSampling.Pick(gray, new(), new(.5, .5), new(1, 1)).Rejection);
        var colors = new[] { new Oklch(.65, .15, 0), new Oklch(.65, .15, 2 * Math.PI / 3) };
        var source = new ushort[1000 * 3];
        for (var i = 0; i < 1000; i++)
        {
            var rgb = OklabColor.ToLinearRec2020(colors[i % 2]);
            source[i * 3] = (ushort)Math.Round(rgb.Red * 65535);
            source[i * 3 + 1] = (ushort)Math.Round(rgb.Green * 65535);
            source[i * 3 + 2] = (ushort)Math.Round(rgb.Blue * 65535);
        }
        using var mixed = RenderPipelineTestSupport.CreateBase(source);
        Assert.Equal("mixed colors", LocalRangeSampling.Pick(mixed, new(), new(.5, .5), new(1000, 1)).Rejection);
    }
    [Theory]
    [InlineData(RenderIntent.Preview)] [InlineData(RenderIntent.Export)]
    public void MonochromeHueIsDormantWithoutDisablingGeometryOrLuminance(RenderIntent intent)
    {
        var source = Enumerable.Range(0, 256).SelectMany(i => new[] { (ushort)(i * 257), (ushort)(i * 257), (ushort)(i * 257) }).ToArray();
        using var basis = RenderPipelineTestSupport.CreateBase(source, true, 16, isMonochrome: true);
        var settings = new EditSettings { Locals = [new() { Exposure = 2, Luminance = new() { Enabled = true, Lower = .4, Upper = .8 } }] };
        var pipeline = new RenderPipeline();
        using var absent = pipeline.Render(new(basis, settings, intent, null, new(false, false)));
        settings.Locals[0].Hue = new() { Enabled = true, Width = 0, Softness = 0 };
        using var hue = pipeline.Render(new(basis, settings, intent, null, new(false, false)));
        Assert.Equal(RenderPipelineTestSupport.ReadPixels(absent.Image), RenderPipelineTestSupport.ReadPixels(hue.Image));
        settings.Locals[0].Luminance = null;
        using var noLight = pipeline.Render(new(basis, settings, intent, null, new(false, false)));
        Assert.NotEqual(RenderPipelineTestSupport.ReadPixels(hue.Image), RenderPipelineTestSupport.ReadPixels(noLight.Image));
    }

}
