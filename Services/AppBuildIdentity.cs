using System.Globalization;
using System.Text;

namespace HappyPhoton.Services;

public enum BuildIdentityProvenance
{
    Stamped,
    UnstampedLocalFallback,
    IncompleteOrInvalidStamp,
}

public sealed record AppBuildIdentity(
    string FriendlyVersion,
    string? SourceRevision,
    string? ShortSourceRevision,
    DateTimeOffset? CommitTimestampUtc,
    DateTimeOffset? LocalExecutableTimestampUtc,
    BuildIdentityProvenance Provenance,
    string DateLabel,
    string DateDisplayText,
    string Copyright,
    string? RepositoryRoot,
    string? ProjectUrl,
    string? LicenseUrl,
    string? ThirdPartyNoticesUrl,
    string SupportText);

internal sealed record AppBuildInfoInputs(
    string? InformationalVersion,
    string FallbackVersion,
    string? SourceRevision,
    string? BuildTimestampUtc,
    string? RepositoryUrl,
    string? Copyright,
    DateTimeOffset? LocalExecutableTimestampUtc,
    string OperatingSystem,
    string ProcessArchitecture);

internal static class AppBuildIdentityFactory
{
    private const int ShortRevisionLength = 8;

    internal static AppBuildIdentity Create(AppBuildInfoInputs inputs)
    {
        var friendlyVersion = FormatFriendlyVersion(
            inputs.InformationalVersion,
            inputs.FallbackVersion);
        var revision = NormalizeRevision(inputs.SourceRevision);
        var timestamp = ParseCommitTimestamp(inputs.BuildTimestampUtc);
        var hasRevisionMetadata = !string.IsNullOrWhiteSpace(inputs.SourceRevision);
        var hasTimestampMetadata = !string.IsNullOrWhiteSpace(inputs.BuildTimestampUtc);
        var provenance = revision != null && timestamp != null
            ? BuildIdentityProvenance.Stamped
            : !hasRevisionMetadata && !hasTimestampMetadata
                ? BuildIdentityProvenance.UnstampedLocalFallback
                : BuildIdentityProvenance.IncompleteOrInvalidStamp;
        var repositoryUrl = NormalizeRepositoryUrl(inputs.RepositoryUrl);
        var urls = CreateUrls(repositoryUrl, revision, provenance);
        var localTimestamp = inputs.LocalExecutableTimestampUtc?.ToUniversalTime();

        return new AppBuildIdentity(
            friendlyVersion,
            revision,
            ShortenRevision(revision),
            timestamp,
            localTimestamp,
            provenance,
            FormatDateLabel(provenance),
            FormatDateDisplay(provenance, timestamp, localTimestamp),
            inputs.Copyright?.Trim() ?? string.Empty,
            repositoryUrl,
            urls.Project,
            urls.License,
            urls.ThirdPartyNotices,
            FormatSupportText(
                friendlyVersion,
                revision,
                timestamp,
                localTimestamp,
                provenance,
                inputs.OperatingSystem,
                inputs.ProcessArchitecture));
    }

    internal static string FormatFriendlyVersion(
        string? informationalVersion,
        string fallbackVersion)
    {
        var candidate = string.IsNullOrWhiteSpace(informationalVersion)
            ? fallbackVersion
            : informationalVersion.Trim();
        var metadataIndex = candidate.IndexOf('+');
        return metadataIndex < 0 ? candidate : candidate[..metadataIndex];
    }

    internal static string? ShortenRevision(string? revision)
    {
        var normalized = NormalizeRevision(revision);
        return normalized == null
            ? null
            : normalized[..Math.Min(normalized.Length, ShortRevisionLength)];
    }

    internal static DateTimeOffset? ParseCommitTimestamp(string? value)
    {
        if (!DateTimeOffset.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal,
                out var timestamp))
        {
            return null;
        }

        return timestamp.ToUniversalTime();
    }

    private static string? NormalizeRevision(string? revision)
    {
        var trimmed = revision?.Trim();
        return trimmed is { Length: >= 7 and <= 64 } &&
               trimmed.All(Uri.IsHexDigit)
            ? trimmed.ToLowerInvariant()
            : null;
    }

    private static string? NormalizeRepositoryUrl(string? repositoryUrl)
    {
        var trimmed = repositoryUrl?.Trim().TrimEnd('/');
        if (string.IsNullOrEmpty(trimmed) ||
            !Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps)
        {
            return null;
        }

        return trimmed.EndsWith(".git", StringComparison.OrdinalIgnoreCase)
            ? trimmed[..^4]
            : trimmed;
    }

    private static (string? Project, string? License, string? ThirdPartyNotices)
        CreateUrls(
            string? repositoryUrl,
            string? revision,
            BuildIdentityProvenance provenance)
    {
        if (repositoryUrl == null)
        {
            return (null, null, null);
        }

        if (provenance == BuildIdentityProvenance.Stamped && revision != null)
        {
            return (
                $"{repositoryUrl}/tree/{revision}",
                $"{repositoryUrl}/blob/{revision}/LICENSE",
                $"{repositoryUrl}/blob/{revision}/THIRD_PARTY_NOTICES.md");
        }

        return (
            repositoryUrl,
            $"{repositoryUrl}/blob/main/LICENSE",
            $"{repositoryUrl}/blob/main/THIRD_PARTY_NOTICES.md");
    }

    private static string FormatDateDisplay(
        BuildIdentityProvenance provenance,
        DateTimeOffset? commitTimestamp,
        DateTimeOffset? localTimestamp)
    {
        if (provenance == BuildIdentityProvenance.Stamped && commitTimestamp != null)
        {
            return $"Commit date (UTC) · {commitTimestamp:yyyy-MM-dd HH:mm}";
        }

        if (localTimestamp != null)
        {
            var prefix = provenance == BuildIdentityProvenance.UnstampedLocalFallback
                ? "Local build"
                : "Incomplete build stamp";
            return $"{prefix} · file date {localTimestamp:yyyy-MM-dd HH:mm} UTC";
        }

        return provenance == BuildIdentityProvenance.UnstampedLocalFallback
            ? "Local build · file date unavailable"
            : "Incomplete build stamp · file date unavailable";
    }

    private static string FormatDateLabel(BuildIdentityProvenance provenance) =>
        provenance switch
        {
            BuildIdentityProvenance.Stamped => "Commit date (UTC)",
            BuildIdentityProvenance.UnstampedLocalFallback => "Local build",
            _ => "Incomplete build stamp",
        };

    private static string FormatSupportText(
        string version,
        string? revision,
        DateTimeOffset? commitTimestamp,
        DateTimeOffset? localTimestamp,
        BuildIdentityProvenance provenance,
        string operatingSystem,
        string processArchitecture)
    {
        var builder = new StringBuilder()
            .AppendLine("Happy Photon")
            .Append("Version: ").AppendLine(version);

        if (provenance == BuildIdentityProvenance.Stamped)
        {
            builder.Append("Source revision: ").AppendLine(revision);
            builder.Append("Commit timestamp: ")
                .AppendLine(commitTimestamp?.ToString("O", CultureInfo.InvariantCulture));
        }
        else
        {
            builder.AppendLine(provenance == BuildIdentityProvenance.UnstampedLocalFallback
                ? "Build identity: unstamped local build"
                : "Build identity: incomplete or invalid build stamp");
            builder.Append("Local executable file timestamp: ")
                .AppendLine(localTimestamp?.ToString("O", CultureInfo.InvariantCulture) ??
                            "unavailable");
        }

        return builder
            .Append("Operating system: ").AppendLine(operatingSystem)
            .Append("Process architecture: ").Append(processArchitecture)
            .ToString();
    }
}
