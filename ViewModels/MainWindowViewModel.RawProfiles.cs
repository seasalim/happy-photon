using System.Collections.Immutable;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HappyPhoton.Models;
using HappyPhoton.Services;

namespace HappyPhoton.ViewModels;

public partial class MainWindowViewModel
{
    [ObservableProperty]
    private RawProfilePickerState _rawProfilePickerState =
        RawProfilePickerState.Empty;

    private bool _isRawProfileCapable;
    private ImmutableArray<RawProfileOptionViewModel>
        _rawProfileDiscoverySnapshot = [];
    private CameraIdentity? _rawProfileCameraIdentity;
    private RawProfileDiscoveryState _rawProfileDiscoveryState =
        RawProfileDiscoveryState.Empty;
    private CancellationTokenSource? _rawProfilePickerCts;
    private bool _isRawProfileDiscoveryActive;
    private CancellationTokenSource? _rawProfileSelectionCts;
    private long _rawProfileDiscoveryGeneration;
    private RawProfileRenderState? _renderDerivedRawProfileState;
    private string? _rawProfileTransientError;

    internal void ResetRawProfilePicker(ImageFile? image)
    {
        SupersedeRawProfileDiscovery();
        _rawProfileSelectionCts?.Cancel();
        _rawProfileCameraIdentity = null;
        _rawProfileDiscoveryState = RawProfileDiscoveryState.Empty;
        _rawProfileDiscoverySnapshot = [];
        _renderDerivedRawProfileState = null;
        _rawProfileTransientError = null;
        _isRawProfileCapable = image?.IsRaw == true;
        PublishRawProfilePickerState();
    }

    internal void ApplyRawProfileState(
        ImageFile image,
        bool isRawSource,
        DcpProfileState? state)
    {
        if (!ReferenceEquals(SelectedImage, image)) return;

        var presentationChanged = _isRawProfileCapable != isRawSource;
        _isRawProfileCapable = isRawSource;
        if (!isRawSource)
        {
            presentationChanged |= _isRawProfileDiscoveryActive;
            SupersedeRawProfileDiscovery();
        }

        var identityChanged = false;
        if (state != null && RawProfilePickerProjector.ProfilesEqual(
                image.EditSettings.RawProfile,
                state.RequestedSelection))
        {
            identityChanged = !Equals(
                _rawProfileCameraIdentity,
                state.CameraIdentity);
            _rawProfileCameraIdentity = state.CameraIdentity;
            if (identityChanged)
            {
                _rawProfileDiscoveryState = _rawProfileDiscoveryState with
                {
                    AdobeScanCompleted = false,
                    AdobeProfilesScanned = 0,
                    AdobeIdentityMatchCount = 0
                };
            }
            _renderDerivedRawProfileState = new RawProfileRenderState(
                state.RequestedSelection,
                state);
            presentationChanged = true;
        }
        if (presentationChanged)
        {
            PublishRawProfilePickerState();
        }

        if (isRawSource && identityChanged &&
            !string.IsNullOrWhiteSpace(state?.CameraIdentity?.Normalized))
        {
            _ = RefreshRawProfilesCoreAsync(
                confirmSelection: false,
                includeImageProfiles: false);
        }
    }

    [RelayCommand]
    private async Task OpenRawProfilePickerAsync()
    {
        if (!IsColorEditingEnabled) return;
        await RefreshRawProfilesCoreAsync(confirmSelection: true);
    }

    private async Task RefreshRawProfilesCoreAsync(
        bool confirmSelection,
        bool includeImageProfiles = true)
    {
        var image = SelectedImage;
        if (image == null || !IsColorEditingEnabled ||
            !RawProfilePickerState.IsVisible) return;
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

        var generation = Interlocked.Increment(
            ref _rawProfileDiscoveryGeneration);
        var cts = new CancellationTokenSource();
        var previous = Interlocked.Exchange(ref _rawProfilePickerCts, cts);
        previous?.Cancel();
        previous?.Dispose();
        _isRawProfileDiscoveryActive = true;
        _rawProfileTransientError = null;
        PublishRawProfilePickerState();
        try
        {
            var result = await ImageService.DcpDiscovery.DiscoverAsync(
                image,
                _rawProfileCameraIdentity,
                cts.Token,
                includeImageProfiles);
            if (!IsCurrentRawProfileDiscovery(image, generation, cts))
            {
                return;
            }

            var options = result.Options
                .Select(option => new RawProfileOptionViewModel(option));
            _rawProfileDiscoverySnapshot = includeImageProfiles
                ? RawProfilePickerProjector.InstallOptions(
                    options,
                    image.EditSettings.RawProfile)
                : RawProfilePickerProjector.MergeOptions(
                    _rawProfileDiscoverySnapshot,
                    options,
                    image.EditSettings.RawProfile);
            _rawProfileDiscoveryState = new RawProfileDiscoveryState(
                result.AdobeScanAttempted ||
                    _rawProfileDiscoveryState.AdobeScanCompleted,
                includeImageProfiles ||
                    _rawProfileDiscoveryState.ImageProfilesCompleted,
                result.AdobeScanAttempted
                    ? result.AdobeProfilesScanned
                    : _rawProfileDiscoveryState.AdobeProfilesScanned,
                result.AdobeScanAttempted
                    ? result.AdobeIdentityMatchCount
                    : _rawProfileDiscoveryState.AdobeIdentityMatchCount);
            _isRawProfileDiscoveryActive = false;
            PublishRawProfilePickerState();

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
        }
        catch (Exception exception)
        {
            if (!IsCurrentRawProfileDiscovery(image, generation, cts))
            {
                return;
            }
            _isRawProfileDiscoveryActive = false;
            if (surfaceGeneration.HasValue)
            {
                RollbackEditReservation(
                    image,
                    previousSettings,
                    surfaceGeneration.Value,
                    previousIntent);
            }
            if (IsCurrentRawProfileDiscovery(image, generation, cts))
            {
                _rawProfileTransientError = exception.Message;
                PublishRawProfilePickerState();
            }
        }
        finally
        {
            if (IsCurrentRawProfileDiscovery(image, generation, cts))
            {
                _rawProfilePickerCts = null;
            }
            cts.Dispose();
        }
    }

    internal async Task SelectRawProfileAsync(RawProfileOptionViewModel? option)
    {
        var image = SelectedImage;
        if (image == null || !IsColorEditingEnabled || option == null ||
            !option.IsProfile || !option.CanSelect) return;
        if (RawProfilePickerProjector.ProfilesEqual(
            image.EditSettings.RawProfile,
            option.Selection)) return;
        var previousSettings = image.EditSettings.Clone();
        var previousIntent = _requestedPreviewIntent;
        var surfaceGeneration = RequestEditedRender();

        _history.PushEdit(image.EditSettings.Clone());
        SyncHistoryFlags();
        WriteRawProfileSelection(image, option.Selection, option);
        image.HasEdits = image.EditSettings.HasEdits;
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

    internal async Task AddRawProfileFileAsync(string? path)
    {
        var image = SelectedImage;
        if (image == null || !IsColorEditingEnabled) return;
        if (string.IsNullOrWhiteSpace(path))
        {
            _rawProfileTransientError =
                "The selected camera profile is not available as a local file.";
            PublishRawProfilePickerState();
            return;
        }
        var option = ImageService.DcpDiscovery.InspectUserFile(path);
        if (!option.CanSelect)
        {
            _rawProfileTransientError = option.Message ??
                "The selected file is not a supported camera profile.";
            PublishRawProfilePickerState();
            return;
        }
        ImageService.InvalidateRawProfiles();
        var viewModel = new RawProfileOptionViewModel(option);
        await SelectRawProfileAsync(viewModel);
        await RefreshRawProfilesCoreAsync(confirmSelection: false);
    }

    private void WriteRawProfileSelection(
        ImageFile image,
        RawProfileSelection? selection,
        RawProfileOptionViewModel? selectedOption = null)
    {
        var identityChanged = !RawProfilePickerProjector.ProfilesEqual(
            image.EditSettings.RawProfile,
            selection);
        if (identityChanged)
        {
            SupersedeRawProfileDiscovery();
        }
        image.EditSettings.RawProfile = selection?.Clone();
        if (selectedOption != null)
        {
            _rawProfileDiscoverySnapshot =
                RawProfilePickerProjector.ReplaceOption(
                    _rawProfileDiscoverySnapshot,
                    selectedOption);
        }
        _rawProfileTransientError = null;
        PublishRawProfilePickerState();
    }

    private void ResyncRawProfilePickerAfterRollback(
        ImageFile image,
        RawProfileSelection? replacedSelection)
    {
        if (!RawProfilePickerProjector.ProfilesEqual(
            replacedSelection,
            image.EditSettings.RawProfile))
        {
            SupersedeRawProfileDiscovery();
        }
        _rawProfileTransientError = null;
        PublishRawProfilePickerState();
    }

    private void SupersedeRawProfileDiscovery()
    {
        Interlocked.Increment(ref _rawProfileDiscoveryGeneration);
        _isRawProfileDiscoveryActive = false;
        var cts = Interlocked.Exchange(ref _rawProfilePickerCts, null);
        cts?.Cancel();
        cts?.Dispose();
    }

    private bool IsCurrentRawProfileDiscovery(
        ImageFile image,
        long generation,
        CancellationTokenSource cts) =>
        generation == Volatile.Read(ref _rawProfileDiscoveryGeneration) &&
        ReferenceEquals(SelectedImage, image) &&
        ReferenceEquals(_rawProfilePickerCts, cts);

    private void PublishRawProfilePickerState()
    {
        var selection = SelectedImage?.EditSettings.RawProfile;
        RawProfilePickerState = RawProfilePickerProjector.Project(
            _isRawProfileCapable,
            selection,
            _rawProfileDiscoverySnapshot,
            _rawProfileCameraIdentity,
            _renderDerivedRawProfileState,
            _rawProfileDiscoveryState,
            _isRawProfileDiscoveryActive,
            _rawProfileTransientError);
    }

    internal static IReadOnlyList<RawProfileOptionViewModel> BuildRawProfileMenu(
        IReadOnlyList<RawProfileOptionViewModel> profiles) =>
        RawProfilePickerProjector.BuildMenu(profiles);
}
