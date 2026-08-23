using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using HappyPhoton.Models;

namespace HappyPhoton.Services;

internal sealed record DcpProfileOption(
    string DisplayName,
    RawProfileSelection? Selection,
    DcpProfileErrorCode Status,
    string? Message,
    bool IsBuiltIn = false)
{
    internal bool CanSelect => IsBuiltIn || Status == DcpProfileErrorCode.None;
    internal string? Fingerprint { get; init; }
    internal string? DeclaredCameraModel { get; init; }
}

internal sealed record DcpDiscoveryResult(
    IReadOnlyList<DcpProfileOption> Options,
    bool HasProfiles,
    bool AdobeScanAttempted,
    int AdobeProfilesScanned,
    int AdobeIdentityMatchCount);

internal sealed class DcpProfileDiscovery
{
    private readonly ISourceAvailabilityService _availability;
    private readonly DcpProfileReader _reader;
    private readonly DcpAdobeProfileIndex _adobeIndex;
    private readonly ConcurrentDictionary<string, CachedProfile> _externalCache =
        new(StringComparer.Ordinal);
    internal Func<Task>? DiscoveryGateAsync { get; set; }

    internal DcpProfileDiscovery(
        ISourceAvailabilityService availability,
        DcpProfileReader? reader = null,
        IReadOnlyList<string>? adobeRoots = null)
    {
        _availability = availability ?? throw new ArgumentNullException(nameof(availability));
        _reader = reader ?? new DcpProfileReader();
        _adobeIndex = new DcpAdobeProfileIndex(
            _availability,
            _reader,
            adobeRoots ?? DcpAdobeProfileIndex.GetDefaultRoots());
    }

    internal async Task<DcpDiscoveryResult> DiscoverAsync(
        ImageFile image,
        CameraIdentity? cameraIdentity,
        CancellationToken cancellationToken,
        bool includeImageProfiles = true)
    {
        var stopwatch = Stopwatch.StartNew();
        DcpDiscoveryResult? result = null;
        try
        {
            if (DiscoveryGateAsync is { } gate)
            {
                await gate().ConfigureAwait(false);
            }
            result = await Task.Run(
                () => Discover(
                    image,
                    cameraIdentity,
                    includeImageProfiles,
                    cancellationToken),
                cancellationToken).ConfigureAwait(false);
            return result;
        }
        finally
        {
            ImageServiceHelpers.LogPerformance(
                nameof(DcpProfileDiscovery),
                nameof(DiscoverAsync),
                stopwatch.ElapsedMilliseconds,
                image.FilePath,
                result == null
                    ? "completed=false"
                    : $"completed=true;scanned={result.AdobeProfilesScanned};" +
                      $"matches={result.AdobeIdentityMatchCount};" +
                      $"options={result.Options.Count}");
        }
    }

    internal DcpProfileOption InspectUserFile(string path)
    {
        var selection = new RawProfileSelection
        {
            Source = RawProfileSource.UserFile,
            Location = Path.GetFullPath(path),
            ContentHash = new string('0', 64)
        };
        var inspected = InspectExternal(path, RawProfileSource.UserFile);
        return inspected ?? new DcpProfileOption(
            Path.GetFileName(path),
            selection,
            DcpProfileErrorCode.Corrupt,
            "The selected file is not a supported camera profile.");
    }

    internal void Invalidate()
    {
        _externalCache.Clear();
        _adobeIndex.Invalidate();
    }

    internal static string NormalizeCameraIdentity(string? make, string? model)
    {
        var normalizedMake = NormalizePart(make);
        var normalizedModel = NormalizePart(model);
        if (normalizedModel.StartsWith(
            normalizedMake + " ",
            StringComparison.Ordinal))
        {
            normalizedModel = normalizedModel[(normalizedMake.Length + 1)..];
        }
        return string.Join(
            ' ',
            new[] { normalizedMake, normalizedModel }
                .Where(value => value.Length > 0));
    }

    private DcpDiscoveryResult Discover(
        ImageFile image,
        CameraIdentity? cameraIdentity,
        bool includeImageProfiles,
        CancellationToken cancellationToken)
    {
        var options = new List<DcpProfileOption>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var persisted = image.EditSettings.RawProfile;
        if (includeImageProfiles &&
            persisted?.Source == RawProfileSource.UserFile &&
            !string.IsNullOrWhiteSpace(persisted.Location))
        {
            var persistedOption = InspectPersisted(persisted);
            Add(
                options,
                seen,
                persistedOption.Status == DcpProfileErrorCode.None
                    ? persistedOption with { Message = null }
                    : persistedOption);
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (includeImageProfiles &&
            image.Extension.Equals(".dng", StringComparison.OrdinalIgnoreCase))
        {
            var embeddedAvailability = _availability.GetAvailability(image.FilePath);
            if (SourceAccessPolicy.CanRead(
                embeddedAvailability,
                SourceReadIntent.Background))
            {
                try
                {
                    var embedded = _reader.ReadEmbeddedProfiles(image.FilePath);
                    foreach (var profile in embedded)
                    {
                        Add(options, seen, ToOption(
                            profile,
                            RawProfileSource.Embedded,
                            location: null) with { Message = null });
                    }
                    if (persisted?.Source == RawProfileSource.Embedded &&
                        embedded.All(profile => !string.Equals(
                            profile.ContentHash,
                            persisted.ContentHash,
                            StringComparison.OrdinalIgnoreCase)))
                    {
                        Add(options, seen, RejectedEmbedded(
                            persisted,
                            DcpProfileErrorCode.HashMismatch,
                            "The selected embedded camera profile has changed or is missing."));
                    }
                }
                catch (DcpProfileException exception)
                {
                    if (persisted?.Source == RawProfileSource.Embedded)
                    {
                        Add(options, seen, RejectedEmbedded(
                            persisted,
                            exception.Code,
                            exception.Message));
                    }
                }
            }
            else if (persisted?.Source == RawProfileSource.Embedded)
            {
                Add(options, seen, RejectedEmbedded(
                    persisted,
                    DcpProfileErrorCode.Unavailable,
                    "The embedded camera profile is online-only or unavailable."));
            }
        }

        if (includeImageProfiles &&
            persisted?.Source == RawProfileSource.Adobe &&
            !string.IsNullOrWhiteSpace(persisted.Location))
        {
            Add(options, seen, InspectPersisted(persisted));
        }

        var identity = cameraIdentity?.Normalized ?? string.Empty;
        DcpAdobeScanResult? adobeScan = null;
        if (identity.Length > 0)
        {
            adobeScan = _adobeIndex.FindMatches(identity, cancellationToken);
            foreach (var path in adobeScan.Matches)
            {
                var option = InspectExternal(path, RawProfileSource.Adobe);
                if (option?.Selection != null &&
                    option.Status == DcpProfileErrorCode.None)
                {
                    Add(options, seen, option with { Message = null });
                }
            }
        }

        options = options
            .OrderBy(option => SourceRank(option.Selection?.Source))
            .ThenBy(option => option.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var hasProfiles = options.Any(option => !option.IsBuiltIn);
        options.Add(new DcpProfileOption(
            "Built-in camera color",
            null,
            DcpProfileErrorCode.None,
            null,
            IsBuiltIn: true));
        return new DcpDiscoveryResult(
            options,
            hasProfiles,
            adobeScan != null,
            adobeScan?.ProfilesScanned ?? 0,
            adobeScan?.IdentityMatchCount ?? 0);
    }

    private DcpProfileOption InspectPersisted(RawProfileSelection selection)
    {
        var option = InspectExternal(
            selection.Location!,
            selection.Source);
        if (option == null)
        {
            return new DcpProfileOption(
                Path.GetFileName(selection.Location!),
                selection.Clone(),
                DcpProfileErrorCode.Missing,
                "The selected camera profile is missing.");
        }
        if (option.Status != DcpProfileErrorCode.None)
        {
            return option with { Selection = selection.Clone() };
        }
        if (!string.Equals(
            option.Selection?.ContentHash,
            selection.ContentHash,
            StringComparison.OrdinalIgnoreCase))
        {
            return option with
            {
                Selection = selection.Clone(),
                Status = DcpProfileErrorCode.HashMismatch,
                Message = "The selected camera profile has changed on disk."
            };
        }
        return option;
    }

    private DcpProfileOption? InspectExternal(
        string path,
        RawProfileSource source)
    {
        if (!SourceAccessPolicy.CanRead(
            _availability.GetAvailability(path),
            SourceReadIntent.Background))
        {
            return new DcpProfileOption(
                Path.GetFileNameWithoutExtension(path),
                new RawProfileSelection
                {
                    Source = source,
                    Location = Path.GetFullPath(path),
                    ContentHash = new string('0', 64)
                },
                DcpProfileErrorCode.Unavailable,
                "The camera profile is online-only or unavailable.");
        }

        try
        {
            var info = new FileInfo(path);
            if (!info.Exists) return null;
            var key = CacheKey(path, info);
            var cached = _externalCache.GetOrAdd(key, _ =>
            {
                var snapshot = _reader.ReadExternalSnapshot(path);
                return new CachedProfile(
                    _reader.ParseExternal(
                        snapshot,
                        Path.GetFileNameWithoutExtension(path)));
            });
            var option = ToOption(cached.Profile, source, Path.GetFullPath(path));
            return option;
        }
        catch (DcpProfileException exception)
        {
            return new DcpProfileOption(
                Path.GetFileNameWithoutExtension(path),
                new RawProfileSelection
                {
                    Source = source,
                    Location = Path.GetFullPath(path),
                    ContentHash = new string('0', 64)
                },
                exception.Code,
                exception.Message);
        }
        catch (Exception exception) when (exception is IOException or
            UnauthorizedAccessException)
        {
            return new DcpProfileOption(
                Path.GetFileNameWithoutExtension(path),
                null,
                DcpProfileErrorCode.Corrupt,
                exception.Message);
        }
    }

    private static string CacheKey(string path, FileInfo info) =>
        $"{Path.GetFullPath(path)}|{info.Length}|{info.LastWriteTimeUtc.Ticks}";

    private static DcpProfileOption ToOption(
        DcpProfile profile,
        RawProfileSource source,
        string? location)
    {
        var option = new DcpProfileOption(
            profile.Name,
            new RawProfileSelection
            {
                Source = source,
                Location = location,
                ContentHash = profile.ContentHash
            },
            DcpProfileErrorCode.None,
            null)
        {
            Fingerprint = DcpProfileReader.ComputeProfileFingerprint(profile),
            DeclaredCameraModel = profile.UniqueCameraModel
        };
        return option;
    }

    private static DcpProfileOption RejectedEmbedded(
        RawProfileSelection selection,
        DcpProfileErrorCode status,
        string message) => new(
            "Embedded profile",
            selection.Clone(),
            status,
            message);

    private static void Add(
        ICollection<DcpProfileOption> options,
        ISet<string> seen,
        DcpProfileOption option)
    {
        var key = option.Fingerprint ??
            option.Selection?.ContentHash ?? "built-in";
        if (key.All(character => character == '0') || seen.Add(key))
        {
            options.Add(option);
        }
    }

    private static int SourceRank(RawProfileSource? source) => source switch
    {
        RawProfileSource.UserFile => 0,
        RawProfileSource.Embedded => 1,
        RawProfileSource.Adobe => 2,
        _ => 3
    };

    private static string NormalizePart(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var builder = new StringBuilder(value.Length);
        var pendingSpace = false;
        foreach (var character in value.Trim().ToUpperInvariant())
        {
            if (char.IsLetterOrDigit(character))
            {
                if (pendingSpace && builder.Length > 0) builder.Append(' ');
                builder.Append(character);
                pendingSpace = false;
            }
            else
            {
                pendingSpace = true;
            }
        }
        return builder.ToString();
    }

    private sealed record CachedProfile(DcpProfile Profile);
}
