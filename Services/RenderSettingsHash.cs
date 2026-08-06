using System.Security.Cryptography;
using System.Text;
using HappyPhoton.Models;

namespace HappyPhoton.Services;

public static class RenderSettingsHash
{
    public static string Compute(EditSettings settings) =>
        Compute(settings, RenderPipeline.Version, BaseImage.Version);

    public static string Compute(
        EditSettings settings,
        int renderVersion,
        int baseVersion)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var payload =
            $"{{\"renderVersion\":{renderVersion}," +
            $"\"baseVersion\":{baseVersion}," +
            $"\"settings\":{EditSettingsJson.Serialize(settings)}}}";
        return Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(payload)))
            .ToLowerInvariant();
    }
}
