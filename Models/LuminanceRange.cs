using System.Text.Json.Serialization;

namespace HappyPhoton.Models;

public sealed record LuminanceRange
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; init; }
    [JsonPropertyName("lower")]
    public double Lower { get; init; }
    [JsonPropertyName("upper")]
    public double Upper { get; init; } = 1;
    [JsonPropertyName("softness")]
    public double Softness { get; init; } = .1;
    [JsonIgnore]
    public bool IsEffective => Enabled && (Lower != 0 || Upper != 1);
}
