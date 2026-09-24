using System.Text.Json.Serialization;

namespace HappyPhoton.Models;

public readonly record struct LocalBrushPoint(int U, int V)
{
    public const int Scale = 16384;
}

public sealed record LocalBrushStroke
{
    public const int MaximumStrokes = 96, MaximumPoints = 4000;
    [JsonPropertyName("mode"), JsonRequired]
    public string Mode { get; init; } = "paint";
    [JsonPropertyName("radius"), JsonRequired]
    public double Radius { get; init; } = .03;
    [JsonPropertyName("feather"), JsonRequired]
    public double Feather { get; init; } = .5;
    [JsonPropertyName("flow"), JsonRequired]
    public double Flow { get; init; } = 1;
    [JsonPropertyName("points"), JsonRequired]
    public ValueArray<LocalBrushPoint> Points { get; init; } = [];
}
