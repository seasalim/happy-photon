using HappyPhoton.Models;
using HappyPhoton.Services;

namespace HappyPhoton.Tests;

internal static class LocalsBrushCoverage
{
    internal static (long[] Geometry, long[] Support) Scan(BaseImage basis, EditSettings settings, BrushDocument[] documents, int? surfaceWidth = null, int? surfaceHeight = null)
    {
        using var geometry = RenderGeometry.Apply(basis.Pixels, settings, out _);
        if (surfaceWidth.HasValue && surfaceHeight.HasValue && (geometry.Width != surfaceWidth || geometry.Height != surfaceHeight))
            geometry.Resize(new ImageMagick.MagickGeometry((uint)surfaceWidth.Value, (uint)surfaceHeight.Value) { IgnoreAspectRatio = true });
        DcpHueSatRenderer.Apply(geometry, basis.Info.DcpProfile?.HueSatMap);
        using var pixels = geometry.GetPixels();
        var values = pixels.GetArea(0, 0, geometry.Width, geometry.Height)!;
        var width = (int)geometry.Width; var height = (int)geometry.Height;
        var wb = new AgxCrossing.Matrix3x3(RenderChromaticStage.CreateWhiteBalanceMatrix(basis.Info, settings));
        var grids = documents.Select(d => new LocalsBrushOptimizedGrid(d, width, height)).ToArray();
        var geometric = new long[grids.Length]; var support = new long[grids.Length];
        Parallel.For(0, height, y =>
        {
            var a = new long[grids.Length]; var b = new long[grids.Length];
            for (var x = 0; x < width; x++)
            {
                OklabColor.Classification? lab = null;
                for (var j = 0; j < grids.Length; j++)
                {
                    if (grids[j].Weight((x + .5) / width, (y + .5) / height) == 0) continue;
                    a[j]++;
                    if (lab == null)
                    {
                        var o = (y * width + x) * 3;
                        var r = values[o] / 65535d; var g = values[o + 1] / 65535d; var blue = values[o + 2] / 65535d;
                        lab = OklabColor.Classify(wb.Row0(r, g, blue), wb.Row1(r, g, blue), wb.Row2(r, g, blue));
                    }
                    if (LocalsBrushPlan.RangeWeight(settings.Locals![j], lab.Value, basis.Info.IsMonochrome) > 0) b[j]++;
                }
            }
            for (var j = 0; j < grids.Length; j++)
            { Interlocked.Add(ref geometric[j], a[j]); Interlocked.Add(ref support[j], b[j]); }
        });
        return (geometric, support);
    }
}
