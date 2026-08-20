using System.Security.Cryptography;
using System.Text;
using HappyPhoton.Models;

namespace HappyPhoton.Services;

public static class RenderSettingsHash
{
    public static string Compute(EditSettings settings) =>
        Compute(
            settings,
            settings.RawProfile?.CacheToken,
            RenderPipeline.Version,
            BaseImage.Version);

    internal static string Compute(
        EditSettings settings,
        string? profileOutcomeToken) =>
        Compute(
            settings,
            profileOutcomeToken,
            RenderPipeline.Version,
            BaseImage.Version);

    public static string Compute(
        EditSettings settings,
        int renderVersion,
        int baseVersion) => Compute(
            settings,
            settings.RawProfile?.CacheToken,
            renderVersion,
            baseVersion);

    private static string Compute(
        EditSettings settings,
        string? profileOutcomeToken,
        int renderVersion,
        int baseVersion)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var payload =
            $"{{\"renderVersion\":{renderVersion}," +
            $"\"baseVersion\":{baseVersion}," +
            $"\"settings\":{EditSettingsJson.Serialize(settings)}}}" +
            (string.IsNullOrEmpty(profileOutcomeToken)
                ? string.Empty
                : $"|dcp={profileOutcomeToken}");
        return Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(payload)))
            .ToLowerInvariant();
    }
}
