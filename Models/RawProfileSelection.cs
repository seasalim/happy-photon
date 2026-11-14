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

    public RawProfileSelection Clone() => (RawProfileSelection)MemberwiseClone();

    /// <summary>True when both are null, or both name the same profile content.</summary>
    public static bool Matches(RawProfileSelection? left, RawProfileSelection? right) =>
        ReferenceEquals(left, right) ||
        left != null && right != null &&
        left.Source == right.Source &&
        string.Equals(left.Location, right.Location, StringComparison.Ordinal) &&
        string.Equals(left.ContentHash, right.ContentHash, StringComparison.OrdinalIgnoreCase);

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
