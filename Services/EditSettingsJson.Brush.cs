using System.Text.Json;
using System.Text.Json.Serialization;
using HappyPhoton.Models;

namespace HappyPhoton.Services;

internal static partial class EditSettingsJson
{
    private static void ClampBrush(LocalAdjustment local, ref int strokes, ref int points, ref bool changed)
    {
        if (local.Strokes == null) throw new JsonException("Brush strokes must be an array.");
        strokes += local.Strokes.Length;
        var clampedStrokes = new LocalBrushStroke[local.Strokes.Length];
        for (var s = 0; s < local.Strokes.Length; s++)
        {
            var stroke = local.Strokes[s];
            if (stroke == null || stroke.Mode is not ("paint" or "erase") || stroke.Points is not { Length: > 0 })
                throw new JsonException("Malformed brush stroke.");
            points += stroke.Points.Length;
            var clampedPoints = new LocalBrushPoint[stroke.Points.Length];
            for (var i = 0; i < stroke.Points.Length; i++)
            {
                var p = stroke.Points[i];
                clampedPoints[i] = new(Clamp(p.U, -16384, 32768, ref changed), Clamp(p.V, -16384, 32768, ref changed));
            }
            clampedStrokes[s] = stroke with
            {
                Radius = Clamp(stroke.Radius, .001, .25, ref changed),
                Feather = Clamp(stroke.Feather, 0, 1, ref changed),
                Flow = Clamp(stroke.Flow, .05, 1, ref changed),
                Points = clampedPoints
            };
        }
        local.Strokes = clampedStrokes;
        if (strokes > LocalBrushStroke.MaximumStrokes || points > LocalBrushStroke.MaximumPoints)
            throw new JsonException("Brush document exceeds 96 strokes or 4,000 points.");
    }

    private sealed class BrushPointsConverter : JsonConverter<ValueArray<LocalBrushPoint>>
    {
        public override ValueArray<LocalBrushPoint> Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.StartArray) throw new JsonException("Brush points must be integer deltas.");
            var points = new List<LocalBrushPoint>();
            long u = 0, v = 0;
            while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
            {
                if (reader.TokenType != JsonTokenType.Number || !reader.TryGetInt32(out var du) || !reader.Read() ||
                    reader.TokenType != JsonTokenType.Number || !reader.TryGetInt32(out var dv))
                    throw new JsonException("Brush points must be paired integer deltas.");
                u += du; v += dv;
                // Preserve out-of-bounds values until Clamp can report the repair.
                points.Add(new((int)Math.Clamp(u, int.MinValue, int.MaxValue), (int)Math.Clamp(v, int.MinValue, int.MaxValue)));
                if (points.Count > LocalBrushStroke.MaximumPoints) throw new JsonException("Too many brush points.");
            }
            return points.ToArray();
        }
        public override void Write(Utf8JsonWriter writer, ValueArray<LocalBrushPoint> points, JsonSerializerOptions options)
        {
            writer.WriteStartArray();
            var previous = new LocalBrushPoint();
            foreach (var point in points)
            {
                writer.WriteNumberValue(point.U - previous.U); writer.WriteNumberValue(point.V - previous.V);
                previous = point;
            }
            writer.WriteEndArray();
        }
    }
}
