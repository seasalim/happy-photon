using System.Collections.Concurrent;
using HappyPhoton.Models;

namespace HappyPhoton.Services;

internal sealed class DcpProfileService
{
    private readonly ISourceAvailabilityService _availability;
    private readonly DcpProfileReader _reader;
    private readonly ConcurrentDictionary<string, Lazy<Task<DcpProfileResolution>>>
        _resolutions = new(StringComparer.Ordinal);

    internal DcpProfileService(
        ISourceAvailabilityService availability,
        DcpProfileReader? reader = null)
    {
        _availability = availability ?? throw new ArgumentNullException(nameof(availability));
        _reader = reader ?? new DcpProfileReader();
    }

    internal async Task<DcpProfileResolution> ResolveAsync(
        ImageFile image,
        RawProfileSelection? selection,
        bool forceRefresh,
        CancellationToken cancellationToken)
    {
        var resolution = await ResolveCoreAsync(
            image,
            selection,
            forceRefresh,
            cancellationToken).ConfigureAwait(false);
        ImageServiceHelpers.LogDisplayTrace(
            $"profile resolve token={resolution.Token} " +
            $"status={resolution.Status} " +
            $"payload={resolution.Profile != null} " +
            $"selection={selection?.CacheToken ?? "none"} " +
            $"isRaw={image.IsRaw} force={forceRefresh}");
        return resolution;
    }

    private Task<DcpProfileResolution> ResolveCoreAsync(
        ImageFile image,
        RawProfileSelection? selection,
        bool forceRefresh,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(image);
        cancellationToken.ThrowIfCancellationRequested();
        if (selection == null || !image.IsRaw)
        {
            return Task.FromResult(DcpProfileResolution.BuiltIn);
        }

        var path = selection.Source == RawProfileSource.Embedded
            ? image.FilePath
            : selection.Location;
        if (string.IsNullOrWhiteSpace(path))
        {
            return Task.FromResult(DcpProfileResolution.Rejected(
                selection,
                DcpProfileErrorCode.Missing,
                "The selected camera profile has no file location."));
        }
        var availability = _availability.GetAvailability(path);
        if (!SourceAccessPolicy.CanRead(availability, SourceReadIntent.Background))
        {
            var message = availability == SourceAvailability.RequiresHydration
                ? "The selected camera profile is online-only. Download it before use."
                : "The selected camera profile is unavailable.";
            return Task.FromResult(DcpProfileResolution.Rejected(
                selection,
                DcpProfileErrorCode.Unavailable,
                message));
        }

        var stamp = GetStamp(path);
        if (stamp == null)
        {
            return Task.FromResult(DcpProfileResolution.Rejected(
                selection,
                DcpProfileErrorCode.Missing,
                "The selected camera profile no longer exists."));
        }
        var key = $"{selection.CacheToken}|{Path.GetFullPath(path)}|{stamp}";
        if (forceRefresh)
        {
            _resolutions.TryRemove(key, out _);
        }
        var lazy = _resolutions.GetOrAdd(
            key,
            _ => new Lazy<Task<DcpProfileResolution>>(
                () => Task.Run(
                    () => ResolveCore(image, selection, path),
                    CancellationToken.None),
                LazyThreadSafetyMode.ExecutionAndPublication));
        return AwaitWithCancellation(lazy.Value, cancellationToken);
    }

    internal void Invalidate() => _resolutions.Clear();

    private DcpProfileResolution ResolveCore(
        ImageFile image,
        RawProfileSelection selection,
        string path)
    {
        try
        {
            var availability = _availability.GetAvailability(path);
            if (!SourceAccessPolicy.CanRead(
                availability,
                SourceReadIntent.Background))
            {
                var message = availability == SourceAvailability.RequiresHydration
                    ? "The selected camera profile is online-only. Download it before use."
                    : "The selected camera profile is unavailable.";
                return DcpProfileResolution.Rejected(
                    selection,
                    DcpProfileErrorCode.Unavailable,
                    message);
            }
            DcpProfile profile;
            if (selection.Source == RawProfileSource.Embedded)
            {
                var profiles = _reader.ReadEmbeddedProfiles(image.FilePath);
                profile = profiles.FirstOrDefault(candidate => string.Equals(
                    candidate.ContentHash,
                    selection.ContentHash,
                    StringComparison.OrdinalIgnoreCase)) ??
                    throw new DcpProfileException(
                        DcpProfileErrorCode.HashMismatch,
                        "The selected embedded camera profile has changed or is missing.");
            }
            else
            {
                var snapshot = _reader.ReadExternalSnapshot(path);
                if (!string.Equals(
                    snapshot.ContentHash,
                    selection.ContentHash,
                    StringComparison.OrdinalIgnoreCase))
                {
                    return DcpProfileResolution.Rejected(
                        selection,
                        DcpProfileErrorCode.HashMismatch,
                        "The selected camera profile has changed on disk.",
                        snapshot.ContentHash);
                }
                profile = _reader.ParseExternal(
                    snapshot,
                    Path.GetFileNameWithoutExtension(path));
            }
            return DcpProfileResolution.Success(selection, profile);
        }
        catch (DcpProfileException exception)
        {
            return DcpProfileResolution.Rejected(
                selection,
                exception.Code,
                exception.Message);
        }
        catch (FileNotFoundException exception)
        {
            return DcpProfileResolution.Rejected(
                selection,
                DcpProfileErrorCode.Missing,
                exception.Message);
        }
        catch (Exception exception) when (exception is IOException or
            UnauthorizedAccessException or OverflowException)
        {
            return DcpProfileResolution.Rejected(
                selection,
                DcpProfileErrorCode.Corrupt,
                $"The selected camera profile could not be read: {exception.Message}");
        }
    }

    private static string? GetStamp(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return info.Exists
                ? $"{info.Length}:{info.LastWriteTimeUtc.Ticks}"
                : null;
        }
        catch (Exception exception) when (exception is IOException or
            UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static async Task<T> AwaitWithCancellation<T>(
        Task<T> task,
        CancellationToken cancellationToken) =>
        await task.WaitAsync(cancellationToken).ConfigureAwait(false);
}
