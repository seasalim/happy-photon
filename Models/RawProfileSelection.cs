using System.Text.Json.Serialization;

namespace HappyPhoton.Models;

public enum RawProfileSource
{
    [JsonStringEnumMemberName("userFile")]
    UserFile,
    [JsonStringEnumMemberName("embedded")]
    Embedded,
    [JsonStringEnumMemberName("adobe")]
    Adobe
}

/// <summary>
/// Persisted identity of a camera-specific RAW profile. The payload is always
/// resolved again through the live source-availability gate.
/// </summary>
public sealed class RawProfileSelection
{
    [JsonPropertyName("source")]
    [JsonConverter(typeof(StrictCamelCaseEnumConverter<RawProfileSource>))]
    public RawProfileSource Source { get; set; }

    [JsonPropertyName("location")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Location { get; set; }

    [JsonPropertyName("contentHash")]
    public string ContentHash { get; set; } = string.Empty;

    public RawProfileSelection Clone() => new()
    {
        Source = Source,
        Location = Location,
        ContentHash = ContentHash
    };

    [JsonIgnore]
    public string CacheToken =>
        $"{SourceKey(Source)}:{ContentHash.ToLowerInvariant()}";

    internal static string SourceKey(RawProfileSource source) => source switch
    {
        RawProfileSource.UserFile => "user",
        RawProfileSource.Embedded => "embedded",
        RawProfileSource.Adobe => "adobe",
        _ => throw new InvalidOperationException(
            $"Unsupported RAW profile source: {source}.")
    };
}
