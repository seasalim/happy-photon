using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HappyPhoton.Models;
using HappyPhoton.Services;

namespace HappyPhoton.Tests;

internal static class ConstructionCompatibilityHash
{
    internal static string Compute(EditSettings settings) => Hash(
        RestoreStandardBaseline(EditSettingsJson.Serialize(settings)),
        settings.RawProfile?.CacheToken);

    internal static string RestoreStandardBaseline(string json)
    {
        using var document = JsonDocument.Parse(json);
        var lens = document.RootElement.GetProperty("lens");
        if (lens.TryGetProperty("baseline", out _)) return json;
        var original = lens.GetRawText();
        var value = "\"vignetting\":" + lens.GetProperty("vignetting").GetRawText();
        var restored = original.Replace(value, value + ",\"baseline\":\"standard\"",
            StringComparison.Ordinal);
        return json.Replace("\"lens\":" + original, "\"lens\":" + restored,
            StringComparison.Ordinal);
    }

    private static string Hash(string json, string? token)
    {
        var payload = $"{{\"renderVersion\":{RenderPipeline.Version}," +
            $"\"baseVersion\":{BaseImage.Version},\"settings\":{json}}}" +
            (string.IsNullOrEmpty(token) ? string.Empty : $"|dcp={token}");
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)))
            .ToLowerInvariant();
    }
}
