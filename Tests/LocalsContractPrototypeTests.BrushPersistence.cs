using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public sealed partial class LocalsContractPrototypeTests
{
    [Fact]
    public void BrushPersistenceQuantizationAndClamps()
    {
        var doc = new BrushDocument([new([new(-2, 3), new(.5 / 16384, -.5 / 16384), new(1, 1)],
            .03, .5, .01, false)]);
        var encoded = LocalsBrushOracle.Encode(doc);
        Assert.Equal(new[] { -16384, 32768, 16385, -32769, 16383, 16385 }, encoded[0].Points);
        Assert.Equal(.05, encoded[0].Flow);
        var roundtrip = LocalsBrushOracle.Decode(encoded);
        Assert.Equal(new[] { new BrushPoint(-1, 2), new BrushPoint(1d / 16384, -1d / 16384), new BrushPoint(1, 1) },
            roundtrip.Strokes[0].Points);
        var clamped = LocalsBrushOracle.Encode(new([new([new(0, 0)], 1, -1, 2, true)]))[0];
        Assert.Equal(.25, clamped.Radius); Assert.Equal(0, clamped.Feather); Assert.Equal(1, clamped.Flow);
        Assert.True(clamped.Erase);
        Assert.Throws<ArgumentOutOfRangeException>(() => LocalsBrushOracle.Encode(
            new([new([new(double.NaN, 0)], .03, .5, 1, false)])));
    }

    [Fact]
    public void BrushPersistenceCapsApplyAcrossLocals()
    {
        var documents = Enumerable.Range(0, 8).Select(local => new BrushDocument(
            Enumerable.Range(0, 12).Select(stroke => new BrushStroke(
                new BrushPoint[41 + (local * 12 + stroke < 64 ? 1 : 0)], .03, .5, .35, false)).ToArray())).ToArray();
        Assert.Equal(96, documents.Sum(d => d.Strokes.Length));
        Assert.Equal(4000, documents.Sum(d => d.Strokes.Sum(s => s.Points.Length)));
        Assert.NotEmpty(LocalsBrushProduction.SerializeDocuments(documents));
        var all = new BrushDocument(documents.SelectMany(d => d.Strokes).ToArray());
        Assert.Equal(4000, LocalsBrushOracle.Decode(LocalsBrushOracle.Encode(all)).Strokes.Sum(s => s.Points.Length));
        var tooManyPoints = documents.ToArray();
        tooManyPoints[0] = new(documents[0].Strokes.Select((s, i) => i == 0 ? s with { Points = [.. s.Points, new(0, 0)] } : s).ToArray());
        Assert.Throws<System.Text.Json.JsonException>(() => LocalsBrushProduction.SerializeDocuments(tooManyPoints));
        var tooManyStrokes = documents.Select(d => new BrushDocument(d.Strokes.Select(s => s with { Points = [new(0, 0)] }).ToArray())).ToArray();
        tooManyStrokes[0] = new([.. tooManyStrokes[0].Strokes, new([new(0, 0)], .03, .5, 1, false)]);
        Assert.Throws<System.Text.Json.JsonException>(() => LocalsBrushProduction.SerializeDocuments(tooManyStrokes));
        Assert.Throws<ArgumentOutOfRangeException>(() => LocalsBrushOracle.Decode(
            [new(new int[8002], .03, .5, 1, false)]));
        Assert.Throws<ArgumentOutOfRangeException>(() => LocalsBrushOracle.Decode(
            Enumerable.Repeat(new LocalsBrushOracle.StoredStroke([0, 0], .03, .5, 1, false), 97).ToArray()));
        Assert.Throws<ArgumentOutOfRangeException>(() => LocalsBrushOracle.Decode([new([0, 0, 1], .03, .5, 1, false)]));
        output.WriteLine("brush_persistence document_strokes=96 document_points=4000 over_cap_rejected=True");
    }

    [Fact]
    public void BrushGridStorageDoesNotScaleWithPixelCount()
    {
        foreach (var cap in new[] { false, true })
        {
            var documents = LocalsBrushWorkloads.Create(cap, 320, 213);
            Assert.Equal(cap ? 96 : 40, documents.Sum(d => d.Strokes.Length));
            Assert.Equal(cap ? 3936 : 1640, documents.Sum(d => d.Strokes.Sum(s => s.Points.Length)));
            foreach (var doc in documents)
            {
                var preview = new LocalBrushEvaluator(LocalsBrushProduction.Strokes(doc), 320, 213);
                var export = new LocalBrushEvaluator(LocalsBrushProduction.Strokes(doc), 3200, 2130);
                Assert.Equal(preview.PayloadBytes, export.PayloadBytes);
                Assert.Equal(preview.EntryCount, export.EntryCount);
                Assert.Equal(preview.Columns, export.Columns); Assert.Equal(preview.Rows, export.Rows);
            }
        }
    }
}
