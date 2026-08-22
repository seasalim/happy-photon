using System.Collections.Immutable;
using HappyPhoton.Models;
using HappyPhoton.Services;

namespace HappyPhoton.ViewModels;

public sealed record RawProfilePickerState(
    bool IsVisible,
    bool IsLoading,
    ImmutableArray<RawProfileOptionViewModel> Options,
    RawProfileOptionViewModel? SelectedOption,
    string StatusMessage)
{
    public static RawProfilePickerState Empty { get; } = new(
        false,
        false,
        [],
        null,
        string.Empty);
}

internal sealed record RawProfileRenderState
{
    internal RawProfileSelection? Selection { get; }
    internal DcpProfileState State { get; }

    internal RawProfileRenderState(
        RawProfileSelection? selection,
        DcpProfileState state)
    {
        Selection = selection?.Clone();
        State = state;
    }
}

internal static class RawProfilePickerProjector
{
    internal const string NoProfilesMessage =
        "NO CAMERA PROFILES FOUND ON THIS PC";
    internal const string ScanningMessage =
        "SCANNING LOCAL CAMERA PROFILES…";
    internal const string RejectionFallback =
        "The selected profile was rejected; using built-in characterization.";

    internal static RawProfilePickerState Project(
        bool isRawCapable,
        RawProfileSelection? selection,
        ImmutableArray<RawProfileOptionViewModel> discovered,
        CameraIdentity? cameraIdentity,
        RawProfileRenderState? renderState,
        bool isLoading,
        string? transientError)
    {
        var profiles = InstallOptions(discovered, selection);
        if (selection != null &&
            renderState?.State.ProfileName is { } renderLabel &&
            ProfilesEqual(renderState.Selection, selection))
        {
            profiles = RelabelOption(profiles, selection, renderLabel);
        }

        var options = BuildMenu(profiles);
        var selected = FindSelectedOption(options, selection);
        var status = ProjectStatus(
            isRawCapable,
            isLoading,
            transientError,
            renderState,
            selection,
            selected,
            profiles,
            cameraIdentity);
        return new RawProfilePickerState(
            isRawCapable,
            isRawCapable && isLoading,
            options,
            selected,
            status);
    }

    internal static ImmutableArray<RawProfileOptionViewModel> InstallOptions(
        IEnumerable<RawProfileOptionViewModel> discovered,
        RawProfileSelection? selection)
    {
        var profiles = discovered
            .Where(option => option.IsProfile)
            .ToList();
        if (profiles.All(option => !option.IsBuiltIn))
        {
            profiles.Insert(0, RawProfileOptionViewModel.BuiltIn());
        }
        if (selection != null && profiles.All(option =>
            !ProfilesEqual(option.Selection, selection)))
        {
            profiles.Add(RawProfileOptionViewModel.Anchor(selection));
        }
        return profiles.ToImmutableArray();
    }

    internal static ImmutableArray<RawProfileOptionViewModel> MergeOptions(
        ImmutableArray<RawProfileOptionViewModel> current,
        IEnumerable<RawProfileOptionViewModel> discovered,
        RawProfileSelection? selection)
    {
        var retained = current.Where(option =>
            option.IsProfile &&
            !option.IsBuiltIn &&
            (option.Selection?.Source is RawProfileSource.UserFile or
                RawProfileSource.Embedded ||
             selection != null && ProfilesEqual(option.Selection, selection)))
            .ToList();
        var discoveredProfiles = discovered.Where(option => option.IsProfile)
            .ToList();
        var merged = new List<RawProfileOptionViewModel>();
        var fingerprints = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var anchor in retained.Where(option => option.Fingerprint == null))
        {
            var resolved = discoveredProfiles.FirstOrDefault(option =>
                ProfilesEqual(option.Selection, anchor.Selection));
            if (resolved?.Fingerprint is { Length: > 0 } fingerprint)
            {
                fingerprints.Add(fingerprint);
            }
        }
        foreach (var option in retained.Concat(discoveredProfiles))
        {
            if (merged.Any(existing =>
                existing.IsBuiltIn == option.IsBuiltIn &&
                ProfilesEqual(existing.Selection, option.Selection)))
            {
                continue;
            }
            if (option.Fingerprint is { Length: > 0 } fingerprint &&
                !fingerprints.Add(fingerprint))
            {
                continue;
            }
            merged.Add(option);
        }
        return InstallOptions(merged, selection);
    }

    internal static ImmutableArray<RawProfileOptionViewModel> ReplaceOption(
        ImmutableArray<RawProfileOptionViewModel> current,
        RawProfileOptionViewModel option)
    {
        var profiles = current
            .Where(candidate => candidate.IsProfile &&
                !ProfilesEqual(candidate.Selection, option.Selection))
            .Append(option);
        return InstallOptions(profiles, option.Selection);
    }

    internal static ImmutableArray<RawProfileOptionViewModel> BuildMenu(
        IReadOnlyList<RawProfileOptionViewModel> profiles)
    {
        var menu = new List<RawProfileOptionViewModel>();
        AddGroup(
            menu,
            "CHOSEN FILE",
            profiles.Where(option => option.Selection?.Source ==
                RawProfileSource.UserFile));
        AddGroup(
            menu,
            "DNG · EMBEDDED",
            profiles.Where(option => option.Selection?.Source ==
                RawProfileSource.Embedded));
        AddGroup(
            menu,
            "ADOBE · CAMERAPROFILES",
            profiles.Where(option => option.Selection?.Source ==
                RawProfileSource.Adobe));
        AddGroup(
            menu,
            "BUILT-IN",
            profiles.Where(option => option.IsBuiltIn));
        menu.Add(RawProfileOptionViewModel.Divider());
        menu.Add(RawProfileOptionViewModel.ChooseFile());
        return menu.ToImmutableArray();
    }

    internal static bool ProfilesEqual(
        RawProfileSelection? first,
        RawProfileSelection? second) => first == null && second == null ||
        first != null && second != null &&
        first.Source == second.Source &&
        string.Equals(first.Location, second.Location, StringComparison.Ordinal) &&
        string.Equals(
            first.ContentHash,
            second.ContentHash,
            StringComparison.OrdinalIgnoreCase);

    private static string ProjectStatus(
        bool isRawCapable,
        bool isLoading,
        string? transientError,
        RawProfileRenderState? renderState,
        RawProfileSelection? selection,
        RawProfileOptionViewModel? selected,
        ImmutableArray<RawProfileOptionViewModel> profiles,
        CameraIdentity? cameraIdentity)
    {
        if (!isRawCapable) return string.Empty;
        if (!string.IsNullOrWhiteSpace(transientError))
        {
            return Uppercase(transientError);
        }
        if (isLoading) return ScanningMessage;
        if (renderState != null &&
            ProfilesEqual(renderState.Selection, selection) &&
            renderState.State.Status != DcpProfileErrorCode.None)
        {
            return Uppercase(renderState.State.Message ?? RejectionFallback);
        }
        if (selected?.Status is { } optionWarning)
        {
            return Uppercase(optionWarning);
        }

        var count = profiles.Count(option =>
            !option.IsBuiltIn && option.CanSelect);
        if (count == 0) return NoProfilesMessage;
        var camera = cameraIdentity?.Normalized;
        if (string.IsNullOrWhiteSpace(camera)) camera = "RAW CAMERA";
        return $"{camera} · {count} {(count == 1 ? "PROFILE" : "PROFILES")}";
    }

    private static ImmutableArray<RawProfileOptionViewModel> RelabelOption(
        ImmutableArray<RawProfileOptionViewModel> profiles,
        RawProfileSelection selection,
        string label) => profiles.Select(option =>
            ProfilesEqual(option.Selection, selection)
                ? option.WithLabel(label)
                : option)
            .ToImmutableArray();

    private static RawProfileOptionViewModel? FindSelectedOption(
        ImmutableArray<RawProfileOptionViewModel> options,
        RawProfileSelection? selection) => options.FirstOrDefault(option =>
            option.IsProfile && ProfilesEqual(option.Selection, selection)) ??
            options.FirstOrDefault(option => option.IsBuiltIn);

    private static void AddGroup(
        ICollection<RawProfileOptionViewModel> menu,
        string heading,
        IEnumerable<RawProfileOptionViewModel> options)
    {
        var group = options
            .OrderBy(option => option.Label, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (group.Count == 0) return;
        menu.Add(RawProfileOptionViewModel.GroupHeader(heading));
        foreach (var option in group)
        {
            menu.Add(option);
        }
    }

    private static string Uppercase(string status) => status.ToUpperInvariant();
}
