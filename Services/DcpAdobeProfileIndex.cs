using System.Collections.Concurrent;

namespace HappyPhoton.Services;

internal sealed class DcpAdobeProfileIndex
{
    private const int ProbeParallelism = 16;
    private readonly ISourceAvailabilityService _availability;
    private readonly DcpProfileReader _reader;
    private readonly IReadOnlyList<string> _roots;
    private readonly ConcurrentDictionary<string, CachedCameraModel> _cache =
        new(StringComparer.Ordinal);

    internal DcpAdobeProfileIndex(
        ISourceAvailabilityService availability,
        DcpProfileReader reader,
        IReadOnlyList<string> roots)
    {
        _availability = availability;
        _reader = reader;
        _roots = roots;
    }

    internal IReadOnlyList<string> FindMatches(
        string identity,
        CancellationToken cancellationToken)
    {
        var matches = new ConcurrentBag<string>();
        Parallel.ForEach(
            EnumerateProfiles(cancellationToken),
            new ParallelOptions
            {
                CancellationToken = cancellationToken,
                MaxDegreeOfParallelism = ProbeParallelism
            },
            file =>
            {
                var model = ReadCameraModel(file);
                if (model != null && string.Equals(
                    DcpProfileDiscovery.NormalizeCameraIdentity(null, model),
                    identity,
                    StringComparison.Ordinal))
                {
                    matches.Add(file.Path);
                }
            });
        return matches.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToList();
    }

    internal void Invalidate() => _cache.Clear();

    private string? ReadCameraModel(ExternalProfileFile file)
    {
        var key = CacheKey(file);
        if (_cache.TryGetValue(key, out var cached))
        {
            return cached.UniqueCameraModel;
        }
        if (!SourceAccessPolicy.CanRead(
            _availability.GetAvailability(file.Path),
            SourceReadIntent.Background))
        {
            return null;
        }

        try
        {
            cached = _cache.GetOrAdd(
                key,
                _ => new CachedCameraModel(
                    _reader.ReadExternalUniqueCameraModel(file.Path)));
            return cached.UniqueCameraModel;
        }
        catch (Exception exception) when (exception is DcpProfileException or
            IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private IReadOnlyList<ExternalProfileFile> EnumerateProfiles(
        CancellationToken cancellationToken)
    {
        var result = new List<ExternalProfileFile>();
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = 0,
            MatchCasing = MatchCasing.CaseInsensitive
        };
        foreach (var root in _roots.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                foreach (var file in new DirectoryInfo(root).EnumerateFiles(
                    "*.dcp",
                    options))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (SourceAvailabilityService.GetEnumerationHint(file) ==
                        SourceAvailability.AvailableLocally || !OperatingSystem.IsWindows())
                    {
                        result.Add(new ExternalProfileFile(
                            file.FullName,
                            file.Length,
                            file.LastWriteTimeUtc.Ticks));
                    }
                }
            }
            catch (Exception exception) when (exception is IOException or
                UnauthorizedAccessException)
            {
            }
        }
        return result;
    }

    private static string CacheKey(ExternalProfileFile file) =>
        $"{file.Path}|{file.Length}|{file.LastWriteTicks}";

    internal static IReadOnlyList<string> GetDefaultRoots()
    {
        var roots = new List<string>();
        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (roaming.Length > 0)
            roots.Add(Path.Combine(roaming, "Adobe", "CameraRaw", "CameraProfiles"));
        var common = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        if (common.Length > 0)
            roots.Add(Path.Combine(common, "Adobe", "CameraRaw", "CameraProfiles"));
        if (OperatingSystem.IsMacOS())
        {
            roots.Add(Path.Combine("/Library", "Application Support",
                "Adobe", "CameraRaw", "CameraProfiles"));
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (home.Length > 0)
                roots.Add(Path.Combine(home, "Library", "Application Support",
                    "Adobe", "CameraRaw", "CameraProfiles"));
        }
        return roots.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private sealed record ExternalProfileFile(
        string Path,
        long Length,
        long LastWriteTicks);
    private sealed record CachedCameraModel(string? UniqueCameraModel);
}
