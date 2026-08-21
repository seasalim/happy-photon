using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HappyPhoton.Models;
using HappyPhoton.Services;

namespace HappyPhoton.ViewModels;

public partial class MainWindowViewModel
{
    [ObservableProperty]
    private bool _isRawProfilePickerVisible;

    [ObservableProperty]
    private bool _isRawProfileLoading;

    [ObservableProperty]
    private string _rawProfileStatusMessage = string.Empty;

    [ObservableProperty]
    private IReadOnlyList<RawProfileOptionViewModel> _rawProfileOptions = [];

    [ObservableProperty]
    private RawProfileOptionViewModel? _selectedRawProfileOption;

    private CameraIdentity? _rawProfileCameraIdentity;
    private CancellationTokenSource? _rawProfilePickerCts;
    private CancellationTokenSource? _rawProfileSelectionCts;
    private long _rawProfileDiscoveryGeneration;
    private DcpProfileState? _renderDerivedRawProfileState;

    private const string NoRawProfilesMessage =
        "NO CAMERA PROFILES FOUND ON THIS PC";
    private const string ScanningRawProfilesMessage =
        "SCANNING LOCAL CAMERA PROFILES…";

    internal void ResetRawProfilePicker(ImageFile? image)
    {
        _rawProfilePickerCts?.Cancel();
        _rawProfileSelectionCts?.Cancel();
        _rawProfileCameraIdentity = null;
        _renderDerivedRawProfileState = null;
        var selection = image?.EditSettings.RawProfile;
        var profiles = new List<RawProfileOptionViewModel>
        {
            RawProfileOptionViewModel.BuiltIn()
        };
        if (selection != null)
        {
            profiles.Add(RawProfileOptionViewModel.Anchor(selection));
        }
        InstallRawProfileOptions(profiles, selection);
        RawProfileStatusMessage = image?.IsRaw == true
            ? selection == null
                ? NoRawProfilesMessage
                : ScanningRawProfilesMessage
            : string.Empty;
        IsRawProfileLoading = false;
        IsRawProfilePickerVisible = image?.IsRaw == true;
    }

    internal void ApplyRawProfileState(
        ImageFile image,
        bool isRawSource,
        DcpProfileState? state)
    {
        if (!ReferenceEquals(SelectedImage, image)) return;
        _renderDerivedRawProfileState = state;
        IsRawProfilePickerVisible = isRawSource;
        if (!isRawSource)
        {
            _rawProfilePickerCts?.Cancel();
            RawProfileStatusMessage = string.Empty;
            return;
        }
        if (state == null) return;

        var identityChanged = !Equals(
            _rawProfileCameraIdentity,
            state.CameraIdentity);
        _rawProfileCameraIdentity = state.CameraIdentity;
        if (state.ProfileName != null && image.EditSettings.RawProfile != null)
        {
            UpdateSelectedRawProfileLabel(
                image.EditSettings.RawProfile,
                state.ProfileName);
        }
        if (!IsRawProfileLoading)
        {
            RawProfileStatusMessage = state.Status == DcpProfileErrorCode.None
                ? ProfileSummaryLine()
                : UppercaseStatus(state.Message ??
                    "The selected profile was rejected; using built-in characterization.");
        }
        if (identityChanged &&
            !string.IsNullOrWhiteSpace(state.CameraIdentity?.Normalized))
        {
            _ = RefreshRawProfilesCoreAsync(
                confirmSelection: false,
                includeImageProfiles: false);
        }
    }

    [RelayCommand]
    private async Task OpenRawProfilePickerAsync()
    {
        await RefreshRawProfilesCoreAsync(confirmSelection: true);
    }

    private async Task RefreshRawProfilesCoreAsync(
        bool confirmSelection,
        bool includeImageProfiles = true)
    {
        var image = SelectedImage;
        if (image == null || !IsRawProfilePickerVisible) return;
        var previousSettings = image.EditSettings.Clone();
        var previousIntent = _requestedPreviewIntent;
        long? surfaceGeneration = confirmSelection &&
            image.EditSettings.RawProfile != null
                ? ReserveRenderOutcome()
                : null;
        if (confirmSelection)
        {
            ImageService.InvalidateRawProfiles();
        }
        var generation = Interlocked.Increment(ref _rawProfileDiscoveryGeneration);
        var cts = new CancellationTokenSource();
        var previous = Interlocked.Exchange(ref _rawProfilePickerCts, cts);
        previous?.Cancel();
        previous?.Dispose();
        IsRawProfileLoading = true;
        RawProfileStatusMessage = ScanningRawProfilesMessage;
        try
        {
            var result = await ImageService.DcpDiscovery.DiscoverAsync(
                image,
                _rawProfileCameraIdentity,
                cts.Token,
                includeImageProfiles);
            if (generation != Volatile.Read(ref _rawProfileDiscoveryGeneration) ||
                !ReferenceEquals(SelectedImage, image))
            {
                if (surfaceGeneration.HasValue)
                {
                    RollbackEditReservation(
                        image,
                        previousSettings,
                        surfaceGeneration.Value,
                        previousIntent);
                }
                return;
            }
            var options = result.Options
                .Select(option => new RawProfileOptionViewModel(option))
                .ToList();
            if (includeImageProfiles)
            {
                InstallRawProfileOptions(
                    options,
                    image.EditSettings.RawProfile);
            }
            else
            {
                MergeRawProfileOptions(
                    options,
                    image.EditSettings.RawProfile);
            }
            RawProfileStatusMessage = SelectedRawProfileOption?.Status is { } status
                ? UppercaseStatus(status)
                : ProfileSummaryLine();
            RestoreRenderDerivedRawProfilePresentation(image);
            if (confirmSelection && image.EditSettings.RawProfile != null)
            {
                await UpdatePreviewWithCurrentSliders(
                    cts.Token,
                    generation: surfaceGeneration,
                    intent: _requestedPreviewIntent);
            }
        }
        catch (OperationCanceledException)
        {
            if (surfaceGeneration.HasValue)
            {
                RollbackEditReservation(
                    image,
                    previousSettings,
                    surfaceGeneration.Value,
                    previousIntent);
            }
        }
        catch
        {
            if (surfaceGeneration.HasValue)
            {
                RollbackEditReservation(
                    image,
                    previousSettings,
                    surfaceGeneration.Value,
                    previousIntent);
            }
            throw;
        }
        finally
        {
            if (ReferenceEquals(_rawProfilePickerCts, cts))
            {
                _rawProfilePickerCts = null;
                IsRawProfileLoading = false;
            }
            cts.Dispose();
        }
    }

    internal async Task SelectRawProfileAsync(RawProfileOptionViewModel? option)
    {
        var image = SelectedImage;
        if (image == null || option == null ||
            !option.IsProfile || !option.CanSelect) return;
        if (ProfilesEqual(image.EditSettings.RawProfile, option.Selection)) return;
        var previousSettings = image.EditSettings.Clone();
        var previousIntent = _requestedPreviewIntent;
        var surfaceGeneration = RequestEditedRender();

        _history.PushEdit(image.EditSettings.Clone());
        SyncHistoryFlags();
        image.EditSettings.RawProfile = option.Selection?.Clone();
        image.HasEdits = image.EditSettings.HasEdits;
        SelectedRawProfileOption = option;
        RawProfileStatusMessage = ProfileSummaryLine();
        UpdateCanReset();
        try
        {
            await SaveEditSettingsAsync(image);
        }
        catch
        {
            RollbackEditReservation(
                image,
                previousSettings,
                surfaceGeneration,
                previousIntent);
            throw;
        }
        _lastSavedState = image.EditSettings.Clone();

        var cts = new CancellationTokenSource();
        var previous = Interlocked.Exchange(ref _rawProfileSelectionCts, cts);
        previous?.Cancel();
        previous?.Dispose();
        try
        {
            await UpdatePreviewWithCurrentSliders(
                cts.Token,
                surfaceGeneration);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (ReferenceEquals(_rawProfileSelectionCts, cts))
            {
                _rawProfileSelectionCts = null;
            }
            cts.Dispose();
        }
    }

    internal async Task AddRawProfileFileAsync(string path)
    {
        var image = SelectedImage;
        if (image == null) return;
        var option = ImageService.DcpDiscovery.InspectUserFile(path);
        if (!option.CanSelect)
        {
            RawProfileStatusMessage = UppercaseStatus(option.Message ??
                "The selected file is not a supported camera profile.");
            return;
        }
        ImageService.InvalidateRawProfiles();
        var viewModel = new RawProfileOptionViewModel(option);
        await SelectRawProfileAsync(viewModel);
        await RefreshRawProfilesCoreAsync(confirmSelection: false);
    }

    private void SyncRawProfilePickerSelection(RawProfileSelection? selection)
    {
        InstallRawProfileOptions(
            RawProfileOptions.Where(option => option.IsProfile),
            selection);
    }

    private void InstallRawProfileOptions(
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
        RawProfileOptions = BuildRawProfileMenu(profiles);
        SelectedRawProfileOption = FindSelectedOption(
            RawProfileOptions,
            selection);
    }

    private void MergeRawProfileOptions(
        IEnumerable<RawProfileOptionViewModel> discovered,
        RawProfileSelection? selection)
    {
        var retained = RawProfileOptions.Where(option =>
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
        InstallRawProfileOptions(merged, selection);
    }

    internal static IReadOnlyList<RawProfileOptionViewModel> BuildRawProfileMenu(
        IReadOnlyList<RawProfileOptionViewModel> profiles)
    {
        var menu = new List<RawProfileOptionViewModel>();
        AddRawProfileGroup(
            menu,
            "CHOSEN FILE",
            profiles.Where(option => option.Selection?.Source ==
                RawProfileSource.UserFile));
        AddRawProfileGroup(
            menu,
            "DNG · EMBEDDED",
            profiles.Where(option => option.Selection?.Source ==
                RawProfileSource.Embedded));
        AddRawProfileGroup(
            menu,
            "ADOBE · CAMERAPROFILES",
            profiles.Where(option => option.Selection?.Source ==
                RawProfileSource.Adobe));
        AddRawProfileGroup(
            menu,
            "BUILT-IN",
            profiles.Where(option => option.IsBuiltIn));
        menu.Add(RawProfileOptionViewModel.Divider());
        menu.Add(RawProfileOptionViewModel.ChooseFile());
        return menu;
    }

    private static void AddRawProfileGroup(
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

    private void UpdateSelectedRawProfileLabel(
        RawProfileSelection selection,
        string label)
    {
        var profiles = RawProfileOptions
            .Where(option => option.IsProfile)
            .Select(option => ProfilesEqual(option.Selection, selection)
                ? option.WithLabel(label)
                : option)
            .ToList();
        InstallRawProfileOptions(profiles, selection);
    }

    private void RestoreRenderDerivedRawProfilePresentation(ImageFile image)
    {
        var state = _renderDerivedRawProfileState;
        if (state == null || !ReferenceEquals(SelectedImage, image))
        {
            return;
        }
        if (state.ProfileName != null && image.EditSettings.RawProfile != null)
        {
            UpdateSelectedRawProfileLabel(
                image.EditSettings.RawProfile,
                state.ProfileName);
        }
        RawProfileStatusMessage = state.Status == DcpProfileErrorCode.None
            ? ProfileSummaryLine()
            : UppercaseStatus(state.Message ??
                "The selected profile was rejected; using built-in characterization.");
    }

    private static RawProfileOptionViewModel? FindSelectedOption(
        IReadOnlyList<RawProfileOptionViewModel> options,
        RawProfileSelection? selection) => options.FirstOrDefault(option =>
            option.IsProfile && ProfilesEqual(option.Selection, selection)) ??
            options.FirstOrDefault(option => option.IsBuiltIn);

    private static bool ProfilesEqual(
        RawProfileSelection? first,
        RawProfileSelection? second) => first == null && second == null ||
        first != null && second != null &&
        first.Source == second.Source &&
        string.Equals(first.Location, second.Location, StringComparison.Ordinal) &&
        string.Equals(
            first.ContentHash,
            second.ContentHash,
            StringComparison.OrdinalIgnoreCase);

    private string ProfileSummaryLine()
    {
        var count = RawProfileOptions.Count(option =>
            option.IsProfile && !option.IsBuiltIn && option.CanSelect);
        if (count == 0) return NoRawProfilesMessage;
        var camera = _rawProfileCameraIdentity?.Normalized;
        if (string.IsNullOrWhiteSpace(camera)) camera = "RAW CAMERA";
        return $"{camera} · {count} {(count == 1 ? "PROFILE" : "PROFILES")}";
    }

    private static string UppercaseStatus(string status) =>
        status.ToUpperInvariant();
}
