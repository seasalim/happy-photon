using System.Net.Http.Headers;
using System.Text.Json;

namespace HappyPhoton.Services;

public enum UpdateCheckStatus
{
    UpToDate,
    UpdateAvailable,
    Failed
}

public sealed record UpdateCheckResult(
    UpdateCheckStatus Status,
    Version? Version = null,
    Uri? ReleaseUri = null);

public sealed class UpdateCheckService
{
    private static readonly HttpClient HttpClient = CreateHttpClient();

    private readonly string _currentVersion;
    private readonly Uri? _latestReleaseApiUri;
    private readonly Func<Uri, CancellationToken, Task<string>> _fetchAsync;

    public UpdateCheckService()
        : this(
            AppBuildInfo.Identity.FriendlyVersion,
            AppBuildInfo.Identity.RepositoryRoot,
            FetchAsync)
    {
    }

    internal UpdateCheckService(
        string currentVersion,
        string? repositoryRoot,
        Func<Uri, CancellationToken, Task<string>> fetchAsync)
    {
        _currentVersion = currentVersion;
        _latestReleaseApiUri = CreateLatestReleaseApiUri(repositoryRoot);
        _fetchAsync = fetchAsync;
    }

    public async Task<UpdateCheckResult> CheckAsync(
        CancellationToken cancellationToken = default)
    {
        if (_latestReleaseApiUri == null ||
            !TryParseVersion(_currentVersion, out var currentVersion))
        {
            return new UpdateCheckResult(UpdateCheckStatus.Failed);
        }

        try
        {
            var json = await _fetchAsync(_latestReleaseApiUri, cancellationToken)
                .ConfigureAwait(false);
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("tag_name", out var tagElement) ||
                !document.RootElement.TryGetProperty("html_url", out var urlElement) ||
                !TryParseVersion(tagElement.GetString(), out var latestVersion) ||
                !Uri.TryCreate(urlElement.GetString(), UriKind.Absolute, out var releaseUri) ||
                releaseUri.Scheme != Uri.UriSchemeHttps)
            {
                return new UpdateCheckResult(UpdateCheckStatus.Failed);
            }

            return CreateResult(currentVersion, latestVersion, releaseUri);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is HttpRequestException or JsonException or
            InvalidOperationException or NotSupportedException or
            IOException or OperationCanceledException)
        {
            return new UpdateCheckResult(UpdateCheckStatus.Failed);
        }
    }

    public static bool TryParseVersion(string? value, out Version version)
    {
        version = new Version();
        var candidate = value?.Trim();
        if (string.IsNullOrEmpty(candidate))
        {
            return false;
        }

        if (candidate[0] is 'v' or 'V')
        {
            candidate = candidate[1..];
        }

        var metadataIndex = candidate.IndexOf('+');
        if (metadataIndex >= 0)
        {
            candidate = candidate[..metadataIndex];
        }

        var prereleaseIndex = candidate.IndexOf('-');
        if (prereleaseIndex >= 0)
        {
            var prerelease = candidate[(prereleaseIndex + 1)..];
            if (!IsBetaPrerelease(prerelease))
            {
                return false;
            }

            candidate = candidate[..prereleaseIndex];
        }

        var parts = candidate.Split('.');
        if (parts.Length != 3 ||
            parts.Any(part => part.Length == 0 ||
                              !part.All(char.IsAsciiDigit)) ||
            !Version.TryParse(candidate, out var parsed))
        {
            return false;
        }

        version = parsed;
        return true;
    }

    public static bool IsUpdateAvailable(
        string currentVersion,
        string latestVersion) =>
        TryParseVersion(currentVersion, out var current) &&
        TryParseVersion(latestVersion, out var latest) &&
        latest > current;

    private static UpdateCheckResult CreateResult(
        Version currentVersion,
        Version latestVersion,
        Uri releaseUri) =>
        latestVersion > currentVersion
            ? new UpdateCheckResult(
                UpdateCheckStatus.UpdateAvailable,
                latestVersion,
                releaseUri)
            : new UpdateCheckResult(
                UpdateCheckStatus.UpToDate,
                latestVersion,
                releaseUri);

    private static bool IsBetaPrerelease(string value)
    {
        const string prefix = "beta.";
        return value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
               value.Length > prefix.Length &&
               value[prefix.Length..].All(char.IsAsciiDigit);
    }

    private static Uri? CreateLatestReleaseApiUri(string? repositoryRoot)
    {
        if (!Uri.TryCreate(repositoryRoot, UriKind.Absolute, out var repositoryUri) ||
            repositoryUri.Scheme != Uri.UriSchemeHttps ||
            !repositoryUri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var segments = repositoryUri.AbsolutePath.Trim('/').Split('/');
        return segments.Length == 2 && segments.All(segment => segment.Length > 0)
            ? new Uri(
                $"https://api.github.com/repos/{segments[0]}/{segments[1]}/releases/latest")
            : null;
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(10)
        };
        client.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("HappyPhoton", AppBuildInfo.Version.ToString(3)));
        return client;
    }

    private static Task<string> FetchAsync(
        Uri uri,
        CancellationToken cancellationToken) =>
        HttpClient.GetStringAsync(uri, cancellationToken);
}
