using Xunit;

namespace HappyPhoton.Tests;

public sealed partial class LocalsContractPrototypeTests
{
    [Theory]
    [InlineData("corner-dabs")]
    [InlineData("mixed-radii")]
    [InlineData("separated-dabs")]
    [InlineData("dabs-at-point-cap")]
    [InlineData("long-segments")]
    public void BrushAdversarialIndexStaysBoundedAndAgreesWithOracle(string shape)
    {
        var document = AdversarialBrush(shape);
        LocalsBrushOracle.ValidateDocuments([document]);
        const long budget = 1024 * 1024;
        foreach (var (width, height) in new[] { (1600, 1200), (1200, 1600), (1600, 1600) })
        {
            LocalsBrushOptimizedGrid? preview = null;
            foreach (var scale in new[] { 1, 4 }) // 1600 px and 6400 px export, identical aspect.
            {
                GC.KeepAlive(new LocalsBrushOptimizedGrid(document, width, height)); // Warm static/JIT setup.
                var w = width * scale; var h = height * scale;
                var before = GC.GetAllocatedBytesForCurrentThread();
                var grid = new LocalsBrushOptimizedGrid(document, w, h);
                var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
                output.WriteLine($"brush_index_adversarial shape={shape} size={w}x{h} " +
                    $"cells={grid.Columns * grid.Rows} segments={grid.SegmentCount} entries={grid.EntryCount} " +
                    $"payload_bytes={grid.PayloadBytes} construction_bytes={allocated}");
                Assert.True(grid.PayloadBytes <= budget, $"{shape}: retained payload {grid.PayloadBytes} exceeds 1 MiB");
                Assert.True(allocated <= budget, $"{shape}: construction {allocated} exceeds 1 MiB");
                Assert.True((long)grid.Columns * grid.Rows <= Math.Max(4096, 4 * grid.SegmentCount));
                if (preview != null)
                {
                    Assert.Equal(preview.PayloadBytes, grid.PayloadBytes);
                    Assert.Equal(preview.EntryCount, grid.EntryCount);
                    Assert.Equal(preview.Columns, grid.Columns); Assert.Equal(preview.Rows, grid.Rows);
                }
                preview = grid;
                CheckAdversarialBrushOracle(document, grid, w, h, shape);
            }
        }
    }

    private void CheckAdversarialBrushOracle(BrushDocument document, LocalsBrushOptimizedGrid grid,
        int width, int height, string shape)
    {
        double worst = 0;
        var comparisons = 0;
        // Sample actual pixel centers without allocating or scanning an export-sized plane.
        var random = new Random(271043);
        for (var i = 0; i < 512; i++)
            Check((random.Next(width) + .5) / width, (random.Next(height) + .5) / height);
        // Bound the oracle workload by sampling at most about 512 cell intersections.
        var boundaryStride = Math.Max(1, (grid.Columns + 1) * (grid.Rows + 1) / 512);
        foreach (var (u, v) in grid.Boundaries().Where((_, i) => i % boundaryStride == 0))
        {
            Check(u, v);
            Check(Math.BitDecrement(u), Math.BitIncrement(v));
            Check(Math.BitIncrement(u), Math.BitDecrement(v));
        }
        // Tiny dabs are unlikely to be hit by uniform sampling: probe their core, feather and edge.
        var edge = Math.Max(width, height);
        foreach (var stroke in document.Strokes)
        foreach (var point in new[] { stroke.Points[0], stroke.Points[^1] })
        foreach (var fraction in new[] { 0, .5, .75, 1, 1.01 })
        {
            Check(point.U + fraction * stroke.Radius * edge / width, point.V);
            Check(point.U, point.V - fraction * stroke.Radius * edge / height);
        }
        output.WriteLine($"brush_index_oracle shape={shape} size={width}x{height} comparisons={comparisons} " +
            $"max_absolute_error={worst:R} fixed_tolerance={BrushAgreementTolerance:R}");
        Assert.True(worst <= BrushAgreementTolerance, $"{shape}: oracle error {worst:R}");

        void Check(double u, double v)
        {
            if (u < 0 || u > 1 || v < 0 || v > 1) return;
            var expected = LocalsBrushOracle.Weight(document, u, v, width, height);
            worst = Math.Max(worst, Math.Abs(expected - grid.Weight(u, v)));
            comparisons++;
        }
    }

    private static BrushDocument AdversarialBrush(string shape)
    {
        if (shape == "corner-dabs") return new([
            new([new(0, 0)], .001, .5, .35, false), new([new(1, 1)], .001, .5, .8, false)]);
        if (shape == "mixed-radii") return new([
            new([new(.25, .5)], .001, .5, .35, false),
            new([new(.5, .5)], .25, .5, .8, false),
            new([new(.5, .5)], .001, 1, .6, true)]);
        var strokes = Enumerable.Range(0, 96).Select(s =>
        {
            var count = shape == "separated-dabs" ? 1 : 41 + (s < 64 ? 1 : 0);
            var center = new BrushPoint((s % 12) / 11d, (s / 12) / 7d);
            // Repeated points retain disc semantics while exercising the entire point budget.
            // Alternating endpoints stress entry duplication for long crossing segments.
            var points = Enumerable.Range(0, count).Select(p => shape == "long-segments"
                ? new BrushPoint(p % 2, s % 2 == 0 ? p % 2 : 1 - p % 2) : center).ToArray();
            return new BrushStroke(points, .001, .5, .35, s % 4 == 3);
        }).ToArray();
        Assert.Equal(shape == "separated-dabs" ? 96 : 4000, strokes.Sum(s => s.Points.Length));
        return new(strokes);
    }
}
