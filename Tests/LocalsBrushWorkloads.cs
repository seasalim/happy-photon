using HappyPhoton.Models;

namespace HappyPhoton.Tests;

internal static class LocalsBrushWorkloads
{
    internal const int Seed = 271031;
    internal const double Radius = .03, Feather = .5, Flow = .35, Spacing = .15 * Radius;
    internal const int SegmentsPerStroke = 40; // Arc length .18 long-edge units before persistence quantization.
    internal const int EraseEvery = 4; // Exactly 25%, including the first erase at stroke 4.

    internal const int CapStrokesPerLocal = 12;

    internal static BrushDocument[] Create(bool eight, int width, int height)
    {
        var random = new Random(Seed);
        var edge = (double)Math.Max(width, height);
        return Enumerable.Range(0, eight ? 8 : 1).Select(local => new BrushDocument(
            Enumerable.Range(0, eight ? CapStrokesPerLocal : 40).Select(stroke =>
            {
                var u = eight ? .2 * (local % 4 + 1) + (random.NextDouble() - .5) * .18 : random.NextDouble();
                var v = eight ? (local / 4 + 1) / 3d + (random.NextDouble() - .5) * .18 : random.NextDouble();
                var angle = random.NextDouble() * 2 * Math.PI;
                var points = new BrushPoint[SegmentsPerStroke + 1];
                points[0] = new(u, v);
                for (var p = 1; p < points.Length; p++)
                {
                    angle += (random.NextDouble() - .5) * .4;
                    u += Math.Cos(angle) * Spacing * edge / width;
                    v += Math.Sin(angle) * Spacing * edge / height;
                    points[p] = new(u, v);
                }
                return new BrushStroke(points, Radius, Feather, Flow, stroke % EraseEvery == EraseEvery - 1);
            }).ToArray())).ToArray();
    }

    internal static string Description(bool eight) =>
        $"workload={(eight ? "BCap" : "B1")} seed={Seed} brushes={(eight ? 8 : 1)} strokes_each={(eight ? CapStrokesPerLocal : 40)} " +
        $"points_each={SegmentsPerStroke + 1} length={SegmentsPerStroke * Spacing:R} spacing={Spacing:R} " +
        $"r={Radius} feather={Feather} flow={Flow} erase_ratio=.25 quantized=False";

    // WP6 LH8 controls; BCap carries these same edits and windows, with brush geometry replacing radial.
    internal static EditSettings Settings(bool eight) => new()
    {
        Locals = Enumerable.Range(0, eight ? 8 : 1).Select(i => new LocalAdjustment
        {
            Id = $"2710000000000000000000000000000{i}", Type = "radial", Ordinal = i + 1,
            Cu = .2 * (i % 4 + 1), Cv = (i / 4 + 1) / 3d, Rx = .11, Ry = .11, Angle = 0,
            Feather = .5, Exposure = 2, Temperature = 50, Tint = -50, Saturation = 100,
            Luminance = eight ? new() { Enabled = true, Lower = i * .08, Upper = .4 + i * .08,
                Softness = i % 3 == 0 ? 0 : .1 } : null,
            Hue = eight ? new() { Enabled = true, Center = i * 45, Width = i == 7 ? 360 : 60,
                Softness = i % 3 == 0 ? 0 : 30 } : null
        }).ToList()
    };
}
