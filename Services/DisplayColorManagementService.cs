namespace HappyPhoton.Services;

internal enum DisplayAcmState
{
    Unavailable,
    Off,
    On,
    Failed,
    OsManaged,
    OsUnmanaged,
    OsIncompatible,
}

internal readonly record struct DisplayPlatformResult(
    string MonitorId,
    string? ProfilePath,
    DisplayAcmState AcmState);

internal interface IDisplayProfilePlatform
{
    DisplayPlatformResult Resolve(nint windowHandle);
}

internal sealed class DisplayColorManagementService
{
    private readonly IDisplayProfilePlatform _platform;
    private readonly Func<string, byte[]> _readProfile;

    internal DisplayColorManagementService(
        IDisplayProfilePlatform? platform = null,
        Func<string, byte[]>? readProfile = null)
    {
        _platform = platform ?? (
            OperatingSystem.IsWindows() ? new WindowsDisplayProfilePlatform()
            : OperatingSystem.IsMacOS() ? new MacOsDisplayProfilePlatform()
            : new NullDisplayProfilePlatform());
        _readProfile = readProfile ?? File.ReadAllBytes;
    }

    internal DisplayTransformSnapshot Resolve(
        nint windowHandle,
        DisplayTransformSnapshot? current = null)
    {
        var resolved = _platform.Resolve(windowHandle);
        var profilePath = resolved.ProfilePath;
        var profileName = string.IsNullOrWhiteSpace(profilePath)
            ? "none"
            : Path.GetFileName(profilePath);
        var profileExists = !string.IsNullOrWhiteSpace(profilePath) &&
            File.Exists(profilePath);
        var profileModified = "none";
        if (profileExists)
        {
            try
            {
                profileModified = File.GetLastWriteTimeUtc(profilePath!).Ticks.ToString();
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or
                ArgumentException)
            {
                profileModified = "unreadable";
            }
        }
        var identity =
            $"{resolved.MonitorId}|{profilePath}|{resolved.AcmState}|{profileModified}";
        if (current is { Support: not DisplayProfileSupport.Invalid } && string.Equals(
                identity,
                current.Identity,
                StringComparison.Ordinal))
        {
            return current;
        }

        var acmDiagnostic = resolved.AcmState == DisplayAcmState.Off
            ? "ACM off"
            : "ACM unavailable";

        if (resolved.AcmState == DisplayAcmState.OsManaged)
        {
            return DisplayTransformSnapshot.CreateTreatedSrgb(
                identity, "none", DisplayProfileSupport.OsManaged,
                "Display profile · managed by macOS (window tagged sRGB)");
        }
        if (resolved.AcmState == DisplayAcmState.OsUnmanaged)
        {
            return DisplayTransformSnapshot.CreateTreatedSrgb(
                identity, "none", DisplayProfileSupport.Absent,
                "Display profile · none (sRGB) · macOS window not tagged; no Metal layer yet");
        }
        if (resolved.AcmState == DisplayAcmState.OsIncompatible)
        {
            return DisplayTransformSnapshot.CreateTreatedSrgb(
                identity, "none", DisplayProfileSupport.Absent,
                "Display profile · none (sRGB) · macOS window layer already tagged with a non-sRGB colorspace; left unmanaged");
        }
        if (resolved.AcmState == DisplayAcmState.On)
        {
            return DisplayTransformSnapshot.CreateTreatedSrgb(
                identity, profileName,
                DisplayProfileSupport.AcmManaged,
                $"Display profile · {profileName} · Windows Auto Color Management active");
        }
        if (resolved.AcmState == DisplayAcmState.Failed)
        {
            return DisplayTransformSnapshot.CreateTreatedSrgb(
                identity, profileName,
                DisplayProfileSupport.AcmQueryFailed,
                $"Display profile · {profileName} · ACM status unavailable; treated as sRGB");
        }
        if (!profileExists)
        {
            return DisplayTransformSnapshot.CreateTreatedSrgb(
                identity, "none", DisplayProfileSupport.Absent,
                $"Display profile · none (sRGB) · {acmDiagnostic}");
        }

        try
        {
            var profile = IccDisplayProfileParser.Parse(
                _readProfile(profilePath!), profileName);
            return profile.Kind switch
            {
                IccDisplayProfileKind.MatrixTrc =>
                    DisplayTransformSnapshot.CreateManaged(
                        identity, profile.Name, profile, acmDiagnostic),
                IccDisplayProfileKind.Srgb =>
                    DisplayTransformSnapshot.CreateTreatedSrgb(
                        identity, profile.Name,
                        DisplayProfileSupport.Srgb,
                        $"Display profile · {profile.Name} · sRGB · {acmDiagnostic}"),
                IccDisplayProfileKind.LutBased => Unsupported(
                    identity, profile.Name,
                    DisplayProfileSupport.LutBased,
                    $"LUT-based · {acmDiagnostic}"),
                IccDisplayProfileKind.Mhc2 => Unsupported(
                    identity, profile.Name,
                    DisplayProfileSupport.Mhc2,
                    $"HDR (MHC2) · {acmDiagnostic}"),
                _ => throw new InvalidDataException("Unknown ICC profile kind."),
            };
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or InvalidDataException or
            ArgumentException or OverflowException)
        {
            return Unsupported(
                identity, profileName,
                DisplayProfileSupport.Invalid,
                $"invalid profile · {acmDiagnostic}");
        }
    }

    private static DisplayTransformSnapshot Unsupported(
        string identity,
        string profileName,
        DisplayProfileSupport support,
        string reason) =>
        DisplayTransformSnapshot.CreateTreatedSrgb(
            identity, profileName, support,
            $"Display profile · {profileName} · {reason}; treated as sRGB");

    private sealed class NullDisplayProfilePlatform : IDisplayProfilePlatform
    {
        public DisplayPlatformResult Resolve(nint windowHandle) =>
            new("none", null, DisplayAcmState.Unavailable);
    }
}
