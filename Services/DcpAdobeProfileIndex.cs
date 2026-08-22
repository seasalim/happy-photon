using System.Collections.Concurrent;

namespace HappyPhoton.Services;

internal sealed record DcpAdobeScanResult(IReadOnlyList<string> Matches,
    int ProfilesScanned, int IdentityMatchCount);

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

    internal DcpAdobeScanResult FindMatches(
        string identity,
        CancellationToken cancellationToken)
    {
        var matches = new ConcurrentBag<string>();
        var profilesScanned = 0;
        Parallel.ForEach(
            EnumerateProfiles(cancellationToken),
            new ParallelOptions
            {
                CancellationToken = cancellationToken,
                MaxDegreeOfParallelism = ProbeParallelism
            },
            file =>
            {
                var probe = ReadCameraModel(file);
                if (!probe.IsReadable) return;
                Interlocked.Increment(ref profilesScanned);
                if (probe.UniqueCameraModel != null && string.Equals(
                    DcpProfileDiscovery.NormalizeCameraIdentity(
                        null,
                        probe.UniqueCameraModel),
                    identity,
                    StringComparison.Ordinal))
                {
                    matches.Add(file.Path);
                }
            });
        var orderedMatches = matches
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return new DcpAdobeScanResult(
            orderedMatches,
            profilesScanned,
            orderedMatches.Count);
    }

    internal void Invalidate() => _cache.Clear();

    private CameraModelProbe ReadCameraModel(ExternalProfileFile file)
    {
        var key = CacheKey(file);
        if (_cache.TryGetValue(key, out var cached))
        {
            return new CameraModelProbe(true, cached.UniqueCameraModel);
        }
        if (!SourceAccessPolicy.CanRead(
            _availability.GetAvailability(file.Path),
            SourceReadIntent.Background))
        {
            return CameraModelProbe.Unreadable;
        }

        try
        {
            cached = _cache.GetOrAdd(
                key,
                _ => new CachedCameraModel(
                    _reader.ReadExternalUniqueCameraModel(file.Path)));
            return new CameraModelProbe(true, cached.UniqueCameraModel);
        }
        catch (Exception exception) when (exception is DcpProfileException or
            IOException or UnauthorizedAccessException)
        {
            return CameraModelProbe.Unreadable;
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
    private sealed record CameraModelProbe(
        bool IsReadable,
        string? UniqueCameraModel)
    {
        internal static CameraModelProbe Unreadable { get; } = new(false, null);
    }
}
