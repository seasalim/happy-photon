using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace HappyPhoton.Services;

internal sealed record DcpAdobeScanResult(IReadOnlyList<string> Matches,
    int ProfilesScanned, int IdentityMatchCount, int Candidates, bool EnumerationComplete);

internal enum DcpAdobeProfilePresence
{
    Found,
    None,
    Unknown
}

internal sealed class DcpAdobeProfileIndex
{
    private const int ProbeParallelism = 16;
    private readonly ISourceAvailabilityService _availability;
    private readonly DcpProfileReader _reader;
    private readonly IReadOnlyList<string> _roots;
    private readonly ConcurrentDictionary<string, CachedCameraModel> _cache =
        new(StringComparer.Ordinal);

    internal Func<string, IEnumerable<FileSystemInfo>>? EnumerateDirectory { get; set; }

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
        var enumeration = new DcpProfileEnumeration(EnumerateDirectory);
        var candidates = 0;
        var files = EnumerateProfiles(enumeration, _roots, cancellationToken, ref candidates);
        Parallel.ForEach(
            files,
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
            orderedMatches.Count,
            candidates,
            enumeration.IsComplete);
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

    internal static async Task<DcpAdobeProfilePresence> ProbeAsync(
        IReadOnlyList<string>? roots = null,
        CancellationToken cancellationToken = default,
        TimeSpan? budget = null,
        Func<string, IEnumerable<FileSystemInfo>>? enumerateDirectory = null)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(budget ?? TimeSpan.FromSeconds(1));
        var token = deadline.Token;

        try
        {
            return await Task.Run(() =>
            {
                var enumeration = new DcpProfileEnumeration(enumerateDirectory);
                var found = enumeration.Enumerate(roots ?? GetDefaultRoots(), token).Any();
                token.ThrowIfCancellationRequested();

                return found ? DcpAdobeProfilePresence.Found :
                    enumeration.IsComplete ? DcpAdobeProfilePresence.None : DcpAdobeProfilePresence.Unknown;
            }, token).WaitAsync(token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return DcpAdobeProfilePresence.Unknown;
        }
        finally
        {
            // A blocked filesystem call may outlive the budget; stop walking when it returns.
            deadline.Cancel();
        }
    }

    private static IReadOnlyList<ExternalProfileFile> EnumerateProfiles(
        DcpProfileEnumeration enumeration,
        IEnumerable<string> roots,
        CancellationToken cancellationToken,
        ref int candidates)
    {
        var result = new List<ExternalProfileFile>();

        foreach (var file in enumeration.Enumerate(roots, cancellationToken))
        {
            candidates++;

            try
            {
                if (SourceAvailabilityService.GetEnumerationHint(file) ==
                    SourceAvailability.AvailableLocally || !OperatingSystem.IsWindows())
                {
                    result.Add(new ExternalProfileFile(
                        file.FullName, file.Length, file.LastWriteTimeUtc.Ticks));
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // The candidate was enumerated, but its metadata is not readable.
            }
        }

        return result;
    }

    private static string CacheKey(ExternalProfileFile file) =>
        $"{file.Path}|{file.Length}|{file.LastWriteTicks}";

    internal static IReadOnlyList<string> GetDefaultRoots() => GetDefaultRoots(
        OperatingSystem.IsWindows() ? OSPlatform.Windows :
            OperatingSystem.IsMacOS() ? OSPlatform.OSX : OSPlatform.Linux,
        Environment.GetEnvironmentVariable,
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));

    internal static IReadOnlyList<string> GetDefaultRoots(
        OSPlatform platform,
        Func<string, string?> environment,
        string home,
        Func<Environment.SpecialFolder, string>? folderPath = null)
    {
        folderPath ??= Environment.GetFolderPath;
        var roots = new List<string>();
        var roaming = folderPath(Environment.SpecialFolder.ApplicationData);
        if (roaming.Length > 0)
            roots.Add(Path.Combine(roaming, "Adobe", "CameraRaw", "CameraProfiles"));

        var common = folderPath(Environment.SpecialFolder.CommonApplicationData);
        if (common.Length > 0)
            roots.Add(Path.Combine(common, "Adobe", "CameraRaw", "CameraProfiles"));

        if (platform == OSPlatform.OSX)
        {
            roots.Add(Path.Combine("/Library", "Application Support",
                "Adobe", "CameraRaw", "CameraProfiles"));

            if (home.Length > 0)
                roots.Add(Path.Combine(home, "Library", "Application Support",
                    "Adobe", "CameraRaw", "CameraProfiles"));
        }

        if (platform == OSPlatform.Linux)
        {
            var prefix = environment("WINEPREFIX");

            if (string.IsNullOrEmpty(prefix) && home.Length > 0)
            {
                prefix = Path.Combine(home, ".wine");
            }

            if (!string.IsNullOrEmpty(prefix))
            {
                roots.Add(Path.Combine(prefix, "drive_c", "ProgramData",
                    "Adobe", "CameraRaw", "CameraProfiles"));
                roots.Add(Path.Combine(prefix, "drive_c", "users", "*", "AppData", "Roaming",
                    "Adobe", "CameraRaw", "CameraProfiles"));
            }
        }

        return roots.Distinct(DcpProfileEnumeration.PathComparer(platform)).ToList();
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
