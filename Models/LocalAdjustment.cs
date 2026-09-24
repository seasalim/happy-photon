using System.Text.Json.Serialization;

namespace HappyPhoton.Models;

public sealed record LocalAdjustment
{
    public const int MaximumCount = 8;

    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    [JsonPropertyName("type")]
    public string Type { get; set; } = "linear";
    [JsonPropertyName("ordinal")]
    public int Ordinal { get; set; } = 1;
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;
    [JsonPropertyName("cu")]
    public double Cu { get; set; } = 0.5;
    [JsonPropertyName("cv")]
    public double Cv { get; set; } = 0.5;
    [JsonPropertyName("angle")]
    public double Angle { get; set; } = 90;
    [JsonPropertyName("feather")]
    public double Feather { get; set; } = 0.25;
    [JsonPropertyName("exposure")]
    public double Exposure { get; set; }
    [JsonPropertyName("temperature")]
    public double Temperature { get; set; }
    [JsonPropertyName("tint")]
    public double Tint { get; set; }
    [JsonPropertyName("saturation")]
    public double Saturation { get; set; }
    [JsonPropertyName("rx")]
    public double Rx { get; set; } = .25;
    [JsonPropertyName("ry")]
    public double Ry { get; set; } = .25;
    [JsonPropertyName("outside")]
    public bool Outside { get; set; }

    [JsonPropertyName("luminance")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public LuminanceRange? Luminance { get; set; }

    [JsonPropertyName("hue")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public HueRange? Hue { get; set; }

    [JsonPropertyName("strokes")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ValueArray<LocalBrushStroke>? Strokes { get; set; }

    [JsonIgnore]
    public bool IsBrush => Type == "brush";
    [JsonIgnore]
    public bool IsRadial => Type == "radial";
    [JsonIgnore]
    public string Name => $"{(IsBrush ? "Brush" : IsRadial ? "Radial" : "Linear")} {Ordinal}";

    public void Rotate(int clockwiseDegrees)
    {
        var turns = ((clockwiseDegrees / 90) % 4 + 4) % 4;
        for (var i = 0; i < turns; i++)
        {
            if (!IsBrush) (Cu, Cv) = (1 - Cv, Cu);
            if (IsBrush && Strokes != null)
                Strokes = [.. Strokes.Select(stroke => stroke with
                {
                    Points = [.. stroke.Points.Select(p => new LocalBrushPoint(LocalBrushPoint.Scale - p.V, p.U))]
                })];
        }
        if (!IsBrush) Angle = ((Angle + turns * 90) % 360 + 360) % 360;
    }
}
