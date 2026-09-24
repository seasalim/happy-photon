using HappyPhoton.Models;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public sealed partial class LocalsContractPrototypeTests
{
    private static LocalBrushEvaluator[] BrushGrids(RenderLocals plan) =>
        ((LocalBrushEvaluator?[])typeof(RenderLocals).GetField("_brushes",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(plan)!)
        .OfType<LocalBrushEvaluator>().ToArray();

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(-1)]
    public void UndersizedBrushBudgetTerminatesAtOneCell(int requestedBudget)
    {
        var document = AdversarialBrush("long-segments");
        var strokes = LocalsBrushProduction.Strokes(document);
        var minimum = LocalBrushEvaluator.MinimumBudget(strokes);
        var budget = requestedBudget < 0 ? minimum - 1 : requestedBudget;
        GC.KeepAlive(new LocalBrushEvaluator(strokes, 1600, 1200, budget));
        var before = GC.GetAllocatedBytesForCurrentThread();
        var grid = new LocalBrushEvaluator(strokes, 1600, 1200, budget);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal((1, 1), (grid.Columns, grid.Rows));
        Assert.InRange(grid.PayloadBytes, 0, minimum);
        Assert.InRange(allocated, 0, minimum);
        output.WriteLine($"one_cell budget={budget} minimum={minimum} payload_bytes={grid.PayloadBytes} caller_alloc_bytes={allocated}");
        CheckAdversarialBrushOracle(document, grid, 1600, 1200, "undersized-budget");
    }

    [Theory]
    [InlineData(false, 1600, 1068)] [InlineData(true, 1600, 1068)]
    [InlineData(false, 1200, 1600)] [InlineData(true, 1200, 1600)]
    public void BrushQualifyingGridsKeepTheirDimensions(bool eight, int width, int height)
    {
        var documents = LocalsBrushWorkloads.Create(eight, width, height);
        var settings = LocalsBrushProduction.Attach(LocalsBrushWorkloads.Settings(eight), documents);
        GC.KeepAlive(RenderLocals.Create(settings, default, width, height, new(width, height, 0, 0, 1, 1)));
        var measurement = new LocalsBrushIndexMeasurement(settings, width, height);
        output.WriteLine(measurement.Report);
        Assert.InRange(measurement.AllocatedBytes, 0, 1024 * 1024);
        var grids = BrushGrids(measurement.Plan);
        var actual = string.Join(";", grids.Select(g => $"{g.Columns}x{g.Rows}"));
        output.WriteLine($"{(eight ? "BCap" : "B1")} size={width}x{height} grids={actual}");
        // Frozen dimensions at the recorded RAW/HEIC gate sizes, with the original allowance.
        var expected = (eight, width) switch
        {
            (false, 1600) => "67x45",
            (false, 1200) => "50x67",
            (true, 1600) => "28x30;35x28;36x28;23x29;31x26;32x28;27x21;33x25",
            _ => "24x31;33x29;33x30;20x32;27x29;30x31;27x22;29x29"
        };
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData(1600, 1200)] [InlineData(1200, 1600)] [InlineData(1600, 1600)]
    public void EightBrushDocumentIndexStaysBoundedAndAgreesWithOracle(int width, int height)
    {
        var strokes = AdversarialBrush("long-segments").Strokes;
        var documents = strokes.Chunk(12).Select(s => new BrushDocument(s)).ToArray();
        LocalsBrushOracle.ValidateDocuments(documents);
        Assert.Equal(96, documents.Sum(d => d.Strokes.Length));
        Assert.Equal(4000, documents.Sum(d => d.Strokes.Sum(s => s.Points.Length)));
        var settings = LocalsBrushProduction.Attach(LocalsBrushWorkloads.Settings(true), documents);
        foreach (var local in settings.Locals!)
        { local.Exposure = .125; local.Temperature = local.Tint = local.Saturation = 0; local.Luminance = null; local.Hue = null; }
        long? previewPayload = null;
        foreach (var scale in new[] { 1, 4 })
        {
            var w = width * scale; var h = height * scale;
            var frame = new LocalsFrame(w, h, 0, 0, 1, 1);
            GC.KeepAlive(RenderLocals.Create(settings, default, w, h, frame));
            var before = GC.GetAllocatedBytesForCurrentThread();
            var plan = RenderLocals.Create(settings, default, w, h, frame)!;
            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            var grids = BrushGrids(plan);
            var payload = grids.Sum(g => g.PayloadBytes);
            output.WriteLine($"eight_brush_index size={w}x{h} payload_bytes={payload} caller_alloc_bytes={allocated}");
            double worst = 0;
            for (var i = 0; i < 8; i++) CheckAdversarialBrushOracle(documents[i], grids[i], w, h, "eight-long-segments");
            var random = new Random(273031);
            for (var i = 0; i < 512; i++)
            {
                var x = random.Next(w); var y = i % 2 == 0 ? (int)(x * (long)h / w) : random.Next(h);
                var expected = documents.Aggregate(1d, (gain, doc) => gain * (1 +
                    LocalsBrushOracle.Weight(doc, (x + .5) / w, (y + .5) / h, w, h) * (Math.Pow(2, .125) - 1)));
                worst = Math.Max(worst, Math.Abs(expected - plan.Gain(y * w + x)));
            }
            output.WriteLine($"eight_brush_render_oracle max_absolute_error={worst:R}");
            Assert.InRange(worst, 0, BrushAgreementTolerance);
            if (previewPayload != null) Assert.Equal(previewPayload.Value, payload);
            previewPayload = payload;
            Assert.InRange(payload, 0, 1024 * 1024);
            Assert.InRange(allocated, 0, 1024 * 1024);
        }
    }

    [Theory]
    [InlineData("corner-dabs")]
    [InlineData("mixed-radii")]
    [InlineData("separated-dabs")]
    [InlineData("dabs-at-point-cap")]
    [InlineData("long-segments")]
    public void BrushAdversarialIndexStaysBoundedAndAgreesWithOracle(string shape)
    {
        var document = LocalsBrushProduction.Quantized(AdversarialBrush(shape));
        LocalsBrushOracle.ValidateDocuments([document]);
        const long budget = 1024 * 1024;
        foreach (var (width, height) in new[] { (1600, 1200), (1200, 1600), (1600, 1600) })
        {
            LocalBrushEvaluator? preview = null;
            foreach (var scale in new[] { 1, 4 }) // 1600 px and 6400 px export, identical aspect.
            {
                var strokes = LocalsBrushProduction.Strokes(document);
                GC.KeepAlive(new LocalBrushEvaluator(strokes, width, height)); // Warm static/JIT setup.
                var w = width * scale; var h = height * scale;
                var before = GC.GetAllocatedBytesForCurrentThread();
                var grid = new LocalBrushEvaluator(strokes, w, h);
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

    private void CheckAdversarialBrushOracle(BrushDocument document, LocalBrushEvaluator grid,
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
