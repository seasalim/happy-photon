using System.Text.Json;
using Xunit;

namespace HappyPhoton.Tests;

internal sealed record RestoreCrashPathMap(string[] Kept, Dictionary<string, string> Pruned)
{
    internal void Validate(string path, string[] boundaries, bool requireComplete)
    {
        var names = Kept.Concat(Pruned.Keys).ToArray();
        Assert.Equal(names.Length, names.Distinct(StringComparer.Ordinal).Count());

        foreach (var name in names)
        {
            Assert.True(boundaries.Contains(name, StringComparer.Ordinal),
                $"Restore map boundary not discovered on {path}: {name}");
        }

        foreach (var (boundary, equivalent) in Pruned)
        {
            Assert.True(Kept.Contains(equivalent, StringComparer.Ordinal),
                $"Restore map {path}: {boundary} must map to a kept boundary: {equivalent}");
            Assert.True(Array.IndexOf(boundaries, equivalent) < Array.IndexOf(boundaries, boundary),
                $"Restore map {path}: {equivalent} must precede {boundary}");
        }

        Assert.False(Pruned.TryGetValue("after:cache:thumbs", out var kept) && kept == "before:cache:thumbs",
            "before:cache:thumbs and after:cache:thumbs must not be mapped as equal.");

        if (!requireComplete) return;

        foreach (var boundary in boundaries)
        {
            Assert.True(names.Contains(boundary, StringComparer.Ordinal),
                $"Unclassified restore boundary on {path}: {boundary}");
        }

        if (boundaries.Contains("after:journal-delete"))
            Assert.Contains("after:journal-delete", Kept);
    }
}

internal static class RestoreCrashEquivalence
{
    internal static bool FullMatrix =>
        Environment.GetEnvironmentVariable("HAPPY_PHOTON_RESTORE_FULL_MATRIX") == "1";

    internal static string PathName(bool corrupt, bool errorScreen, bool matchingPreset) =>
        $"corrupt={corrupt},errorScreen={errorScreen},matchingPreset={matchingPreset}";

    internal static void ValidateCaseOnlyCoverage(string path, string[] discovered, string[] selected,
        string[] matchingPresetBoundaries)
    {
        foreach (var boundary in discovered)
        {
            Assert.True(selected.Contains(boundary, StringComparer.Ordinal) ||
                matchingPresetBoundaries.Contains(boundary, StringComparer.Ordinal),
                $"Unclassified case-only restore boundary on {path}: {boundary}");
        }
    }

    internal static RestoreCrashPathMap? Load(string path)
    {
        // Only Windows' case-insensitive catalog path has a proven equivalence map.
        if (!OperatingSystem.IsWindows()) return null;

        var file = Path.Combine(AppContext.BaseDirectory, "RestoreCrashEquivalence.json");
        var maps = JsonSerializer.Deserialize<Dictionary<string, RestoreCrashPathMap>>(
            File.ReadAllText(file), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

        Assert.True(maps.TryGetValue(path, out var map), $"Missing restore equivalence path: {path}");

        return map;
    }
}
