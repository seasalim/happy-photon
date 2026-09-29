using System.Text.Json;
using HappyPhoton.Models;

namespace HappyPhoton.Services;

internal sealed class BuiltInLookLibrary
{
    private readonly Lazy<IReadOnlyList<Preset>> _looks = new(Load);

    internal bool IsLoaded => _looks.IsValueCreated;

    internal IReadOnlyList<Preset> Looks => _looks.Value;

    internal static bool ContainsId(string id) => typeof(BuiltInLookLibrary).Assembly
        .GetManifestResourceInfo($"HappyPhoton.Assets.Looks.{id}.preset.json") != null;

    private static Stream Open(string name) => typeof(BuiltInLookLibrary).Assembly
        .GetManifestResourceStream($"HappyPhoton.Assets.Looks.{name}")
        ?? throw new InvalidOperationException($"Missing built-in look resource: {name}");

    private static IReadOnlyList<Preset> Load()
    {
        using var manifestStream = Open("approved.json");
        using var manifest = JsonDocument.Parse(manifestStream);
        var looks = new List<Preset>();

        foreach (var group in manifest.RootElement.GetProperty("groups").EnumerateArray())
        {
            foreach (var id in group.GetProperty("looks").EnumerateArray())
            {
                using var stream = Open($"{id.GetString()}.preset.json");
                using var document = JsonDocument.Parse(stream);
                looks.Add(Read(document.RootElement));
            }
        }

        return looks.AsReadOnly();
    }

    internal static Preset Read(JsonElement root)
    {
        if (root.GetProperty("version").GetInt32() != UserPresetFile.CurrentVersion)
            throw new JsonException("Unsupported built-in preset version.");

        var payload = root.GetProperty("settings");

        foreach (var field in payload.EnumerateObject())
        {
            if (field.Name != "version" && !EditSettingsLook.Fields.Contains(field.Name))
                throw new JsonException($"Built-in look contains a correction: {field.Name}");
        }

        var sparse = payload.Deserialize<EditSettings>() ?? throw new JsonException("Missing look settings.");
        // Canonical validation supplies defaults in memory without changing the sparse resource.
        var settings = EditSettingsJson.Deserialize(EditSettingsJson.Serialize(sparse), out _);
        var description = root.GetProperty("intent").GetString();
        if (string.IsNullOrWhiteSpace(description)) throw new JsonException("Missing look intent.");

        return new Preset(root.GetProperty("id").GetString()!, root.GetProperty("name").GetString()!,
            settings, root.GetProperty("group").GetString(), root.GetProperty("order").GetInt32(), description);
    }
}
