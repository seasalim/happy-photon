using HappyPhoton.Models;
using HappyPhoton.Services;

namespace HappyPhoton.Tests;

// Adapts the pinned workload data to production's absolute quantized points.
internal static class LocalsBrushProduction
{
    internal static LocalBrushStroke[] Strokes(BrushDocument document) => document.Strokes.Select(s => new LocalBrushStroke
    {
        Mode = s.Erase ? "erase" : "paint", Radius = s.Radius, Feather = s.Feather, Flow = s.Flow,
        Points = s.Points.Select(p => new LocalBrushPoint(Quantize(p.U), Quantize(p.V))).ToArray()
    }).ToArray();
    private static int Quantize(double value) => (int)Math.Round(Math.Clamp(value, -1, 2) * 16384, MidpointRounding.AwayFromZero);
    internal static BrushDocument Quantized(BrushDocument document) => new(Strokes(document).Select(s => new BrushStroke(
        s.Points.Select(p => new BrushPoint(p.U / 16384d, p.V / 16384d)).ToArray(),
        s.Radius, s.Feather, s.Flow, s.Mode == "erase")).ToArray());
    internal static EditSettings Attach(EditSettings settings, BrushDocument[]? documents)
    {
        var result = settings.Clone();
        result.Locals = documents?.Select((d, i) => settings.Locals![i] with { Type = "brush", Strokes = Strokes(d) }).ToList();
        return result;
    }
    internal static string SerializeDocuments(BrushDocument[] documents) => EditSettingsJson.Serialize(new()
    {
        Locals = documents.Select((d, i) => new LocalAdjustment
        { Id = $"2710000000000000000000000000000{i}", Ordinal = i + 1, Type = "brush", Exposure = 2, Strokes = Strokes(d) }).ToList()
    });
    internal static double RangeWeight(LocalAdjustment local, OklabColor.Classification lab, bool monochrome = false) =>
        (local.Luminance is { Enabled: true } l ? LuminanceWindow.Weight(l, lab.L) : 1) *
        (!monochrome && local.Hue is { Enabled: true } h ? HueWindow.Weight(h, lab.Hue, lab.Chroma) : 1);
}
