using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using HappyPhoton.Services;
using HappyPhoton.Models;

namespace HappyPhoton.Tests;

internal static class FinishingLookHarness
{
    internal static readonly string[] LookFields =
    [
        "contrast", "saturation", "vibrance", "curve", "curveRed", "curveGreen",
        "curveBlue", "mixer", "effects", "texture", "clarity"
    ];

    private static readonly PropertyInfo[] Properties = typeof(EditSettings).GetProperties()
        .Where(property => LookFields.Contains(property.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name))
        .ToArray();

    internal static string Folder => Path.Combine(GoldenTestPaths.AssetDirectory, "finishing-looks");

    internal static string ShippedFolder => Path.Combine(GoldenTestPaths.RepositoryRoot, "Assets", "Looks");

    internal static IEnumerable<string> CandidatePaths => Directory.GetFiles(ShippedFolder, "*.preset.json")
        .Concat(Directory.GetFiles(Folder, "*.preset.json"));

    internal static string CandidatePath(string id) => CandidatePaths.Single(path =>
        Path.GetFileName(path) == id + ".preset.json");

    internal static FinishingCandidate[] Candidates() => CandidatePaths
        .Select(Load)
        .OrderBy(candidate => candidate.Group).ThenBy(candidate => candidate.Order).ToArray();

    internal static string DroppedFolder => Path.Combine(Folder, "dropped");

    internal static IEnumerable<string> DroppedPaths => Directory.GetFiles(DroppedFolder, "*.preset.json");

    // Looks dropped at a gate stay authored and frozen, but no gate evaluates them.
    internal static FinishingCandidate[] AuthoredCandidates() => CandidatePaths.Concat(DroppedPaths)
        .Select(Load)
        .OrderBy(candidate => candidate.Group).ThenBy(candidate => candidate.Order).ToArray();

    internal static FinishingCandidate Load(string path)
    {
        var json = File.ReadAllBytes(path);
        var candidate = JsonSerializer.Deserialize<FinishingCandidate>(json)!;
        candidate.LoadedSha256 = Convert.ToHexString(SHA256.HashData(json));
        var envelope = JsonSerializer.SerializeToNode(candidate)!.AsObject();
        // Sparse look-only files omit corrections. The current personal preset
        // reader requires lens defaults, so expand the schema only in memory.
        var parsed = PresetService.DeserializePresetFile(envelope.ToJsonString(), path)
            ?? throw new JsonException("Candidate has no settings.");
        candidate.Settings = parsed.Settings;

        return candidate;
    }

    internal static EditSettings Apply(EditSettings corrections, EditSettings look)
    {
        var result = corrections.Clone();
        var ownedLook = look.Clone();

        foreach (var property in Properties)
        {
            property.SetValue(result, property.GetValue(ownedLook));
        }

        return result;
    }

    internal static EditSettings Remove(EditSettings settings) => Apply(settings, new());

    internal static byte[] CorrectionBytes(EditSettings settings)
    {
        var node = JsonSerializer.SerializeToNode(settings)!.AsObject();

        foreach (var field in LookFields)
        {
            node.Remove(field);
        }

        return JsonSerializer.SerializeToUtf8Bytes(node);
    }
}

internal sealed class FinishingCandidate : UserPresetFile
{
    internal string LoadedSha256 { get; set; } = "";

    [JsonPropertyName("group")]
    public string Group { get; set; } = "";

    [JsonPropertyName("order")]
    public int Order { get; set; }

    [JsonPropertyName("intent")]
    public string Intent { get; set; } = "";
}

