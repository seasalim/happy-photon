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

    [JsonIgnore]
    public string Name => $"Linear {Ordinal}";

    public void Rotate(int clockwiseDegrees)
    {
        var turns = ((clockwiseDegrees / 90) % 4 + 4) % 4;
        for (var i = 0; i < turns; i++) (Cu, Cv) = (1 - Cv, Cu);
        Angle = ((Angle + turns * 90) % 360 + 360) % 360;
    }
}
