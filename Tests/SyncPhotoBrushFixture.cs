using HappyPhoton.Models;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

internal static class SyncPhotoBrushFixture
{
    // Frozen copy of the non-worst-case LocalsBrushCatalogGrowthTests script.
    internal static EditSettings Create()
    {
        const int strokeCount = 96;
        const int totalPoints = 4000;
        var random = new Random(271100);
        var perLocal = Enumerable.Range(0, 8).Select(_ => new List<BrushStroke>()).ToArray();

        for (var stroke = 0; stroke < strokeCount; stroke++)
        {
            var u = random.NextDouble();
            var v = random.NextDouble();
            var angle = random.NextDouble() * Math.Tau;
            var points = new BrushPoint[totalPoints / strokeCount + (stroke < totalPoints % strokeCount ? 1 : 0)];
            points[0] = new(u, v);

            for (var point = 1; point < points.Length; point++)
            {
                angle += (random.NextDouble() - .5) * .4;
                u += Math.Cos(angle) * .0045;
                v += Math.Sin(angle) * .0045 * 1.5;
                points[point] = new(u, v);
            }

            perLocal[stroke % 8].Add(new BrushStroke(points, .03, .5, .35, stroke % 4 == 3));
        }

        var json = LocalsBrushProduction.SerializeDocuments(
            perLocal.Select(strokes => new BrushDocument(strokes.ToArray())).ToArray());
        Assert.Equal(36836, System.Text.Encoding.UTF8.GetByteCount(json));

        return EditSettingsJson.Deserialize(json, out _);
    }
}
