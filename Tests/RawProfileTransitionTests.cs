using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class RawProfileTransitionTests : IDisposable
{
    private readonly CatalogVmFixture _fx = new("profile-transitions");

    [Fact]
    public async Task ImageSwitchSupersedesDiscoveryAndItsLaterCompletion()
    {
        using var catalog = await _fx.CreateCatalogAsync("switch");
        await using var vm = CreateViewModel(catalog);
        var first = new ImageFile(_fx.Path("first.dng"));
        var second = new ImageFile(_fx.Path("second.dng"));
        vm.SelectedImage = first;
        var gate = DiscoveryGate(vm);

        var discovery = vm.OpenRawProfilePickerCommand.ExecuteAsync(null);
        await gate.Started.Task.WaitAsync(TestWaits.Condition);
        Assert.True(vm.RawProfilePickerState.IsLoading);

        vm.SelectedImage = second;
        var replacement = vm.RawProfilePickerState;
        Assert.False(replacement.IsLoading);

        gate.Release.TrySetResult();
        await discovery;

        Assert.Same(replacement, vm.RawProfilePickerState);
        Assert.Same(second, vm.SelectedImage);
    }

    [Fact]
    public async Task ImageSwitchNeverClaimsEmptyBeforeScanCompletes()
    {
        using var catalog = await _fx.CreateCatalogAsync("switch-honesty");
        await using var vm = CreateViewModel(catalog);
        var emissions = new List<string>();
        vm.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(vm.RawProfilePickerState))
            {
                emissions.Add(vm.RawProfilePickerState.StatusMessage);
            }
        };

        vm.SelectedImage = new ImageFile(_fx.Path("first.dng"));
        vm.SelectedImage = new ImageFile(_fx.Path("second.dng"));

        var falseEmpty = emissions.Concat([
                vm.RawProfilePickerState.StatusMessage])
            .Count(status =>
                status == RawProfilePickerProjector.NoProfilesMessage);
        Assert.Equal(0, falseEmpty);
    }

    [Fact]
    public async Task CurrentIdentityScanSettlesOnlyAfterItsCompletion()
    {
        using var catalog = await _fx.CreateCatalogAsync("identity-settlement");
        await using var vm = CreateViewModel(catalog);
        var image = new ImageFile(_fx.Path("identity.cr2"));
        vm.SelectedImage = image;
        await vm.OpenRawProfilePickerCommand.ExecuteAsync(null);
        Assert.Equal(
            RawProfilePickerProjector.AwaitingIdentityMessage,
            vm.RawProfilePickerState.StatusMessage);
        var gate = DiscoveryGate(vm);

        vm.ApplyRawProfileState(
            image,
            isRawSource: true,
            new DcpProfileState(
                string.Empty,
                DcpProfileErrorCode.None,
                null,
                null,
                new CameraIdentity("Fixture", "No Match"),
                RequestedSelection: null));
        await gate.Started.Task.WaitAsync(TestWaits.Condition);

        Assert.Equal(
            RawProfilePickerProjector.ScanningMessage,
            vm.RawProfilePickerState.StatusMessage);
        gate.Release.TrySetResult();
        await TestWaits.UntilAsync(() =>
            !vm.RawProfilePickerState.IsLoading);
        Assert.NotEqual(
            RawProfilePickerProjector.ScanningMessage,
            vm.RawProfilePickerState.StatusMessage);
    }

    [Fact]
    public async Task DecodedMonochromeRawWithoutIdentityReportsUnavailable()
    {
        using var catalog = await _fx.CreateCatalogAsync("monochrome-status");
        await using var vm = CreateViewModel(catalog);
        var image = new ImageFile(_fx.Path("monochrome.dng"));
        vm.SelectedImage = image;

        vm.ReconcileMonochromeCapability(image, isMonochrome: true);
        vm.ApplyRawProfileState(
            image,
            isRawSource: true,
            new DcpProfileState(
                string.Empty,
                DcpProfileErrorCode.None,
                null,
                null,
                CameraIdentity: null,
                RequestedSelection: null));

        Assert.False(vm.IsColorEditingEnabled);
        Assert.False(vm.RawProfilePickerState.IsLoading);
        Assert.Equal(
            "CAMERA IDENTITY UNAVAILABLE",
            vm.RawProfilePickerState.StatusMessage);
    }

    [Fact]
    public async Task DecodedColorRawWithoutIdentityReportsUnavailable()
    {
        using var catalog = await _fx.CreateCatalogAsync("missing-identity");
        await using var vm = CreateViewModel(catalog);
        var image = new ImageFile(_fx.Path("missing-identity.dng"));
        vm.SelectedImage = image;

        vm.ApplyRawProfileState(
            image,
            isRawSource: true,
            new DcpProfileState(
                string.Empty,
                DcpProfileErrorCode.None,
                null,
                null,
                CameraIdentity: null,
                RequestedSelection: null));

        Assert.True(vm.IsColorEditingEnabled);
        Assert.False(vm.RawProfilePickerState.IsLoading);
        Assert.Equal(
            "CAMERA IDENTITY UNAVAILABLE",
            vm.RawProfilePickerState.StatusMessage);
    }

    [Fact]
    public async Task SelectionSupersedesBlockedDiscoverySynchronously()
    {
        using var catalog = await _fx.CreateCatalogAsync("selection");
        await using var vm = CreateViewModel(catalog);
        var image = new ImageFile(_fx.Path("selection.dng"));
        vm.SelectedImage = image;
        var gate = DiscoveryGate(vm);
        var discovery = vm.OpenRawProfilePickerCommand.ExecuteAsync(null);
        await gate.Started.Task.WaitAsync(TestWaits.Condition);

        var option = Option("selection.dcp", 'a');
        await vm.SelectRawProfileAsync(option);
        var selected = vm.RawProfilePickerState;

        Assert.False(selected.IsLoading);
        Assert.Equal(
            option.Selection?.ContentHash,
            image.EditSettings.RawProfile?.ContentHash);
        gate.Release.TrySetResult();
        await discovery;
        Assert.Same(selected, vm.RawProfilePickerState);
    }

    [Fact]
    public async Task ResetAndHistorySupersedeBlockedDiscovery()
    {
        using var catalog = await _fx.CreateCatalogAsync("history");
        await using var vm = CreateViewModel(catalog);
        var image = new ImageFile(_fx.Path("history.dng"));
        vm.SelectedImage = image;
        var option = Option("history.dcp", 'b');
        await vm.SelectRawProfileAsync(option);

        var resetGate = DiscoveryGate(vm);
        var resetDiscovery = vm.OpenRawProfilePickerCommand.ExecuteAsync(null);
        await resetGate.Started.Task.WaitAsync(TestWaits.Condition);
        await vm.ResetEditsCommand.ExecuteAsync(null);
        var reset = vm.RawProfilePickerState;
        Assert.False(reset.IsLoading);
        Assert.Null(image.EditSettings.RawProfile);
        resetGate.Release.TrySetResult();
        await resetDiscovery;
        Assert.Same(reset, vm.RawProfilePickerState);

        var historyGate = DiscoveryGate(vm);
        var historyDiscovery = vm.OpenRawProfilePickerCommand.ExecuteAsync(null);
        await historyGate.Started.Task.WaitAsync(TestWaits.Condition);
        await vm.UndoCommand.ExecuteAsync(null);
        var restored = vm.RawProfilePickerState;
        Assert.False(restored.IsLoading);
        Assert.Equal(
            option.Selection?.ContentHash,
            image.EditSettings.RawProfile?.ContentHash);
        historyGate.Release.TrySetResult();
        await historyDiscovery;
        Assert.Same(restored, vm.RawProfilePickerState);
    }

    [Fact]
    public async Task DiscoveryFailureIsTransientAndNeverEscapesCommand()
    {
        using var catalog = await _fx.CreateCatalogAsync("failure");
        await using var vm = CreateViewModel(catalog);
        vm.SelectedImage = new ImageFile(_fx.Path("failure.dng"));
        vm.ImageService.DcpDiscovery.DiscoveryGateAsync = () =>
            Task.FromException(new InvalidOperationException("Discovery failed"));

        await vm.OpenRawProfilePickerCommand.ExecuteAsync(null);

        Assert.False(vm.RawProfilePickerState.IsLoading);
        Assert.Equal(
            "DISCOVERY FAILED",
            vm.RawProfilePickerState.StatusMessage);
    }

    [Fact]
    public async Task SupersededDiscoveryFailurePublishesNothing()
    {
        using var catalog = await _fx.CreateCatalogAsync("stale-failure");
        await using var vm = CreateViewModel(catalog);
        var image = new ImageFile(_fx.Path("stale-failure.dng"));
        vm.SelectedImage = image;
        var gate = DiscoveryGate(vm);
        var discovery = vm.OpenRawProfilePickerCommand.ExecuteAsync(null);
        await gate.Started.Task.WaitAsync(TestWaits.Condition);

        await vm.SelectRawProfileAsync(Option("new.dcp", 'c'));
        var selected = vm.RawProfilePickerState;
        gate.Release.TrySetException(
            new InvalidOperationException("Stale discovery failed"));

        await discovery;
        Assert.Same(selected, vm.RawProfilePickerState);
        Assert.DoesNotContain(
            "STALE DISCOVERY FAILED",
            vm.RawProfilePickerState.StatusMessage);
    }

    [Fact]
    public async Task InvalidFileErrorClearsWhenRefreshStarts()
    {
        using var catalog = await _fx.CreateCatalogAsync("recovery");
        await using var vm = CreateViewModel(catalog);
        vm.SelectedImage = new ImageFile(_fx.Path("recovery.dng"));
        await vm.AddRawProfileFileAsync(_fx.Path("invalid.dcp"));
        Assert.Contains(
            "NOT A SUPPORTED CAMERA PROFILE",
            vm.RawProfilePickerState.StatusMessage);
        var gate = DiscoveryGate(vm);

        var refresh = vm.OpenRawProfilePickerCommand.ExecuteAsync(null);
        await gate.Started.Task.WaitAsync(TestWaits.Condition);
        Assert.Equal(
            RawProfilePickerProjector.ScanningMessage,
            vm.RawProfilePickerState.StatusMessage);

        gate.Release.TrySetResult();
        await refresh;
        Assert.DoesNotContain(
            "NOT A SUPPORTED CAMERA PROFILE",
            vm.RawProfilePickerState.StatusMessage);
    }

    [Fact]
    public async Task DiscoverySettlesAndShowsRejectionBeforeConfirmationRender()
    {
        using var catalog = await _fx.CreateCatalogAsync("confirm-render");
        await using var vm = CreateViewModel(catalog, new CountingPairLoader());
        var image = new ImageFile(_fx.Path("confirm-render.cr2"));
        var initialRenderCompleted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        vm.ImageService.Previews.RenderRequestCompleted += _ =>
            initialRenderCompleted.TrySetResult();
        vm.SelectedImage = image;
        await initialRenderCompleted.Task.WaitAsync(TestWaits.Condition);
        await TestWaits.UntilAsync(() =>
            vm.ImageService.Previews.PreviewActivityCount == 0);
        var selection = Selection("selected.dcp", '9');
        image.EditSettings.RawProfile = selection;
        vm.ResetRawProfilePicker(image);
        vm.ApplyRawProfileState(
            image,
            isRawSource: true,
            new DcpProfileState(
                "rejected",
                DcpProfileErrorCode.Corrupt,
                "Confirm rejected",
                "Selected profile",
                null,
                selection));
        var expectedStatus = vm.RawProfilePickerState.StatusMessage;
        var discoveryGate = DiscoveryGate(vm);
        var renderStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseRender = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var refresh = vm.OpenRawProfilePickerCommand.ExecuteAsync(null);
        await discoveryGate.Started.Task.WaitAsync(TestWaits.Condition);
        Assert.True(vm.RawProfilePickerState.IsLoading);
        vm.ImageService.Previews.SourceWorkGateAsync = () =>
        {
            renderStarted.TrySetResult();
            return releaseRender.Task;
        };
        discoveryGate.Release.TrySetResult();
        await renderStarted.Task.WaitAsync(TestWaits.Condition);

        var isLoadingDuringRender = vm.RawProfilePickerState.IsLoading;
        var statusDuringRender = vm.RawProfilePickerState.StatusMessage;
        releaseRender.TrySetResult();
        await refresh;

        Assert.False(isLoadingDuringRender);
        Assert.Equal(expectedStatus, statusDuringRender);
    }

    [Fact]
    public async Task AcceptedNonRawCapabilityEmptiesActiveScanningStatus()
    {
        using var catalog = await _fx.CreateCatalogAsync("capability");
        await using var vm = CreateViewModel(catalog);
        var image = new ImageFile(_fx.Path("capability.dng"));
        vm.SelectedImage = image;
        var gate = DiscoveryGate(vm);
        var discovery = vm.OpenRawProfilePickerCommand.ExecuteAsync(null);
        await gate.Started.Task.WaitAsync(TestWaits.Condition);

        vm.ApplyRawProfileState(image, isRawSource: false, state: null);
        var nonRaw = vm.RawProfilePickerState;

        Assert.False(nonRaw.IsVisible);
        Assert.False(nonRaw.IsLoading);
        Assert.Empty(nonRaw.StatusMessage);
        gate.Release.TrySetResult();
        await discovery;
        Assert.Same(nonRaw, vm.RawProfilePickerState);
    }

    [Fact]
    public async Task MismatchedAndOriginalRenderOutcomesLeaveSnapshotUntouched()
    {
        using var catalog = await _fx.CreateCatalogAsync("correlation");
        await using var vm = CreateViewModel(catalog);
        var image = new ImageFile(_fx.Path("correlation.dng"));
        vm.SelectedImage = image;
        var current = Option("current/profile.dcp", 'd');
        await vm.SelectRawProfileAsync(current);
        var snapshot = vm.RawProfilePickerState;
        var wrongLocation = Selection("old/profile.dcp", 'd');

        vm.ApplyRawProfileState(
            image,
            isRawSource: true,
            Rejection(wrongLocation, "Old rejection", "Old label"));
        Assert.Same(snapshot, vm.RawProfilePickerState);

        vm.ApplyRawProfileState(
            image,
            isRawSource: true,
            Rejection(null, "Original rejection", "Original label"));
        Assert.Same(snapshot, vm.RawProfilePickerState);
    }

    [Fact]
    public async Task RejectionSurvivesNonProfileUndoAndSaveRollback()
    {
        var catalog = await _fx.CreateCatalogAsync("rollback");
        var vm = CreateViewModel(catalog);
        try
        {
            var image = new ImageFile(_fx.Path("rollback.dng"));
            vm.SelectedImage = image;
            var first = Option("first.dcp", 'e');
            await vm.SelectRawProfileAsync(first);
            vm.ApplyRawProfileState(
                image,
                isRawSource: true,
                Rejection(first.Selection, "First rejected", "First resolved"));
            await TestWaits.UntilAsync(() =>
                !vm.RawProfilePickerState.IsLoading);
            Assert.Equal(
                "FIRST REJECTED",
                vm.RawProfilePickerState.StatusMessage);

            vm.Exposure = 1;
            await vm.UndoCommand.ExecuteAsync(null);
            Assert.Equal(
                "FIRST REJECTED",
                vm.RawProfilePickerState.StatusMessage);

            catalog.Dispose();
            await Assert.ThrowsAnyAsync<Exception>(() =>
                vm.SelectRawProfileAsync(Option("second.dcp", 'f')));

            Assert.Equal(
                first.Selection?.ContentHash,
                image.EditSettings.RawProfile?.ContentHash);
            Assert.Equal(
                "First resolved",
                vm.RawProfilePickerState.SelectedOption?.Label);
            Assert.Equal(
                "FIRST REJECTED",
                vm.RawProfilePickerState.StatusMessage);
        }
        finally
        {
            await vm.DisposeAsync();
            catalog.Dispose();
        }
    }

    [Fact]
    public async Task PastePreservesTargetProfileAndSelectedIdentity()
    {
        using var catalog = await _fx.CreateCatalogAsync("paste");
        await using var vm = CreateViewModel(catalog);
        var source = new ImageFile(_fx.Path("source.dng"))
        {
            EditSettings = new EditSettings
            {
                Exposure = 1,
                RawProfile = Selection("source.dcp", '1')
            }
        };
        var targetProfile = Selection("target.dcp", '2');
        var target = new ImageFile(_fx.Path("target.dng"))
        {
            EditSettings = new EditSettings { RawProfile = targetProfile }
        };
        vm.SelectedImage = source;
        vm.CopyEditSettingsCommand.Execute(null);
        vm.SelectedImage = target;

        await vm.PasteEditSettingsCommand.ExecuteAsync(null);

        Assert.Equal(1, target.EditSettings.Exposure);
        Assert.True(RawProfilePickerProjector.ProfilesEqual(
            targetProfile,
            target.EditSettings.RawProfile));
        Assert.True(RawProfilePickerProjector.ProfilesEqual(
            targetProfile,
            vm.RawProfilePickerState.SelectedOption?.Selection));
    }

    private MainWindowViewModel CreateViewModel(
        CatalogService catalog,
        IBaseImageLoader? baseLoader = null)
    {
        var viewModel = _fx.CreateViewModel(
            catalog,
            baseLoader ?? new NullBaseLoader(),
            loadMetadataAsync: _ => Task.CompletedTask,
            availabilityService: new TestSourceAvailabilityService(
                SourceAvailability.AvailableLocally));
        viewModel.IsDevelopMode = true;
        return viewModel;
    }

    private static RawProfileOptionViewModel Option(string location, char hash) =>
        new(new DcpProfileOption(
            $"Profile {hash}",
            Selection(location, hash),
            DcpProfileErrorCode.None,
            null));

    private static DcpProfileState Rejection(
        RawProfileSelection? selection,
        string message,
        string label) => new(
            "rejected",
            DcpProfileErrorCode.Corrupt,
            message,
            label,
            new CameraIdentity("Canon", "EOS R5"),
            selection);

    private static RawProfileSelection Selection(string location, char hash) =>
        new()
        {
            Source = RawProfileSource.UserFile,
            Location = location,
            ContentHash = new string(hash, 64)
        };

    private static DiscoveryControl DiscoveryGate(MainWindowViewModel vm)
    {
        var control = new DiscoveryControl();
        vm.ImageService.DcpDiscovery.DiscoveryGateAsync = () =>
        {
            control.Started.TrySetResult();
            return control.Release.Task;
        };
        return control;
    }

    public void Dispose() => _fx.Dispose();

    private sealed class DiscoveryControl
    {
        internal TaskCompletionSource Started { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
