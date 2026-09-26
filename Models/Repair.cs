using System.Text.Json.Serialization;

namespace HappyPhoton.Models;

/// <summary>A circular repair in the oriented, lens-corrected base frame.</summary>
public sealed record Repair
{
    public const int MaximumCount = 64;
    public const int CoordinateScale = 16384;
    public const double MinimumRadius = .002, MaximumRadius = .10;
    public const double MaximumArea = 0.18849555921538758;

    [JsonPropertyName("id"), JsonRequired]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    [JsonPropertyName("type"), JsonRequired]
    public string Type { get; set; } = "heal";
    [JsonPropertyName("u"), JsonRequired]
    public double U { get; set; } = .5;
    [JsonPropertyName("v"), JsonRequired]
    public double V { get; set; } = .5;
    [JsonPropertyName("su"), JsonRequired]
    public double Su { get; set; } = .5;
    [JsonPropertyName("sv"), JsonRequired]
    public double Sv { get; set; } = .5;
    [JsonPropertyName("radius"), JsonRequired]
    public double Radius { get; set; } = .03;
    [JsonPropertyName("feather"), JsonRequired]
    public double Feather { get; set; } = .5;
    [JsonPropertyName("opacity"), JsonRequired]
    public double Opacity { get; set; } = 1;
}
