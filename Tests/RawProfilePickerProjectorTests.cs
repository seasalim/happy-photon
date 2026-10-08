using System.Collections.Immutable;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using Xunit;

namespace HappyPhoton.Tests;

public sealed partial class RawProfilePickerProjectorTests
{
    [Fact]
    public void StatusUsesCanonicalPrecedence()
    {
        var selection = Selection("selected.dcp", 'a');
        var warning = Option(
            selection,
            DcpProfileErrorCode.Missing,
            "Option warning");
        var render = new RawProfileRenderState(
            selection,
            State(
                selection,
                DcpProfileErrorCode.HashMismatch,
                "Render rejection"));
        var discovered = ImmutableArray.Create(warning);

        Assert.Equal(
            "VALIDATION ERROR",
            Project(
                selection,
                discovered,
                render,
                loading: true,
                error: "Validation error").StatusMessage);
        Assert.Equal(
            RawProfilePickerProjector.ScanningMessage,
            Project(selection, discovered, render, loading: true).StatusMessage);
        Assert.Equal(
            "RENDER REJECTION",
            Project(selection, discovered, render).StatusMessage);
        Assert.Equal(
            "OPTION WARNING",
            Project(selection, discovered, renderState: null).StatusMessage);

        var valid = ImmutableArray.Create(Option(selection));
        Assert.Equal(
            "CANON EOS R5 · 1 PROFILE",
            Project(selection, valid, renderState: null).StatusMessage);
        Assert.Equal(
            RawProfilePickerProjector.NoAdobeProfilesMessage,
            Project(
                selection: null,
                discovered: [],
                renderState: null).StatusMessage);
    }

    [Fact]
    public void NonRawCapabilityGatesErrorAndScanningStatus()
    {
        var projected = RawProfilePickerProjector.Project(
            isRawCapable: false,
            selection: null,
            discovered: [],
            new CameraIdentity("Canon", "EOS R5"),
            renderState: null,
            RawProfileDiscoveryState.Empty,
            isLoading: true,
            transientError: "Discovery error");

        Assert.False(projected.IsVisible);
        Assert.False(projected.IsLoading);
        Assert.Empty(projected.StatusMessage);
    }

    [Fact]
    public void RenderStateCorrelatesBySourceLocationAndHash()
    {
        var selected = Selection("current/profile.dcp", 'a');
        var oldLocation = Selection("old/profile.dcp", 'a');
        var discovered = ImmutableArray.Create(Option(selected));
        var oldRender = new RawProfileRenderState(
            oldLocation,
            State(
                oldLocation,
                DcpProfileErrorCode.HashMismatch,
                "Old rejection",
                "Old label"));

        var projected = Project(selected, discovered, oldRender);

        Assert.Equal("Selected", projected.SelectedOption?.Label);
        Assert.Equal("CANON EOS R5 · 1 PROFILE", projected.StatusMessage);
    }

    [Fact]
    public void KeyedRenderStateStaysDormantUntilSelectionReturns()
    {
        var first = Selection("first.dcp", 'a');
        var second = Selection("second.dcp", 'b');
        var render = new RawProfileRenderState(
            first,
            State(
                first,
                DcpProfileErrorCode.Corrupt,
                "First rejected",
                "Resolved first"));
        var discovered = ImmutableArray.Create(Option(first), Option(second));

        var changed = Project(second, discovered, render);
        var restored = Project(first, discovered, render);

        Assert.Equal("Selected", changed.SelectedOption?.Label);
        Assert.DoesNotContain("REJECTED", changed.StatusMessage);
        Assert.Equal("Resolved first", restored.SelectedOption?.Label);
        Assert.Equal("FIRST REJECTED", restored.StatusMessage);
    }

    [Fact]
    public void EmptyClaimsRequireOnlyTheirBackingAdobeScope()
    {
        Assert.Equal(
            RawProfilePickerProjector.AwaitingIdentityMessage,
            RawProfilePickerProjector.Project(
                isRawCapable: true,
                selection: null,
                discovered: [],
                cameraIdentity: null,
                renderState: null,
                RawProfileDiscoveryState.Empty,
                isLoading: false,
                transientError: null).StatusMessage);
        Assert.Equal(
            RawProfilePickerProjector.OpenToScanMessage,
            Project(
                selection: null,
                discovered: [],
                renderState: null,
                discoveryState: RawProfileDiscoveryState.Empty).StatusMessage);
        Assert.Equal(
            RawProfilePickerProjector.IdentityUnavailableMessage,
            RawProfilePickerProjector.Project(true, null, [], cameraIdentity: null,
                new RawProfileRenderState(null, new DcpProfileState(string.Empty,
                    DcpProfileErrorCode.None, null, null, null, null)),
                RawProfileDiscoveryState.Empty, isLoading: false, transientError: null).StatusMessage);
        Assert.Equal(
            "MATCHING LOCAL CAMERA PROFILES COULD NOT BE LOADED",
            Project(null, [], null,
                discoveryState: Completed(profilesScanned: 1, identityMatches: 1)).StatusMessage);
        Assert.Equal(
            "3 LOCAL CAMERA PROFILES SCANNED · NONE DECLARE CANON EOS R5",
            Project(
                selection: null,
                discovered: [],
                renderState: null,
                discoveryState: Completed(
                    profilesScanned: 3,
                    identityMatches: 0)).StatusMessage);
        Assert.Equal(
            RawProfilePickerProjector.NoAdobeProfilesMessage,
            Project(
                selection: null,
                discovered: [],
                renderState: null,
                discoveryState: Completed()).StatusMessage);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IdleDiscoveryStatesNeverReportScanning(bool adobeCompleted)
    {
        foreach (var identity in new CameraIdentity?[] { null, new("Canon", "EOS R5") })
        {
            foreach (var counts in new[] { (0, 0), (3, 0), (3, 1) })
            {
                var state = RawProfilePickerProjector.Project(true, null, [], identity,
                    renderState: null,
                    new RawProfileDiscoveryState(adobeCompleted, counts.Item1, counts.Item2),
                    isLoading: false, transientError: null);

                Assert.NotEqual(RawProfilePickerProjector.ScanningMessage, state.StatusMessage);
            }
        }
    }

    [Fact]
    public void RejectedChoiceOverridesEmptyClaimsButNotSelectableProfiles()
    {
        var discovery = Completed() with { ChosenFileRejection = "Chosen file rejection" };

        Assert.Equal("CHOSEN FILE REJECTION", Project(null, [], null,
            discoveryState: discovery).StatusMessage);
        Assert.Equal("CANON EOS R5 · 1 PROFILE", Project(null,
            [Option(Selection("valid.dcp", 'a'))], null,
            discoveryState: discovery).StatusMessage);
        Assert.Equal(RawProfilePickerProjector.ScanningMessage, Project(null, [], null,
            loading: true, discoveryState: discovery).StatusMessage);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RejectedChoiceOverridesStaleWarningsBelowLoading(bool hasRenderRejection)
    {
        var selection = Selection("missing.dcp", 'a');
        var warning = Option(selection, DcpProfileErrorCode.Missing, "Option warning");
        var render = hasRenderRejection
            ? new RawProfileRenderState(selection,
                State(selection, DcpProfileErrorCode.Missing, "Render rejection"))
            : null;
        var discovery = Completed() with { ChosenFileRejection = "Latest rejection" };

        Assert.Equal("LATEST REJECTION", Project(selection, [warning], render,
            discoveryState: discovery).StatusMessage);
        Assert.Equal(RawProfilePickerProjector.ScanningMessage,
            Project(selection, [warning], render, loading: true,
                discoveryState: discovery).StatusMessage);
        Assert.Equal(hasRenderRejection ? "RENDER REJECTION" : "OPTION WARNING",
            Project(selection, [warning, Option(Selection("valid.dcp", 'b'))], render,
                discoveryState: discovery).StatusMessage);
    }

    [Theory]
    [InlineData(false, 2, 1, "ADOBE PROFILE FOLDERS COULD NOT BE READ")]
    [InlineData(true, 0, 0, "NO ADOBE CAMERA PROFILES FOUND")]
    [InlineData(true, 2, 0, "2 ADOBE PROFILE FILES FOUND · NONE READABLE")]
    [InlineData(true, 2, 2, "2 LOCAL CAMERA PROFILES SCANNED · NONE DECLARE CANON EOS R5")]
    public void EmptyScanMessagesAndLinkFollowEvidence(bool complete, int candidates, int readable, string message)
    {
        var discovery = Completed(readable) with
        {
            AdobeCandidates = candidates,
            AdobeEnumerationComplete = complete
        };
        var state = Project(null, [], null, discoveryState: discovery);

        Assert.Equal(message, state.StatusMessage);
        Assert.True(state.ShowGetAdobeProfilesLink);
    }

    [Fact]
    public void LinkRequiresCompletedEmptyScanAndSurvivesChosenFileRejection()
    {
        Assert.False(Project(null, [], null,
            discoveryState: RawProfileDiscoveryState.Empty).ShowGetAdobeProfilesLink);
        Assert.False(Project(null, [], null, loading: true).ShowGetAdobeProfilesLink);
        Assert.False(Project(null, [], null, error: "error").ShowGetAdobeProfilesLink);
        Assert.False(Project(null, [], null,
            discoveryState: Completed(1, 1)).ShowGetAdobeProfilesLink);
        Assert.False(RawProfilePickerProjector.Project(false, null, [], null, null,
            Completed(), false, null).ShowGetAdobeProfilesLink);
        Assert.True(Project(null, [], null, discoveryState: Completed() with
        {
            ChosenFileRejection = "Chosen file rejection"
        }).ShowGetAdobeProfilesLink);

        foreach (var source in new[] { RawProfileSource.Adobe, RawProfileSource.UserFile, RawProfileSource.Embedded })
        {
            var selection = Selection("valid.dcp", 'a');
            selection.Source = source;
            Assert.False(Project(null, [Option(selection)], null).ShowGetAdobeProfilesLink);
        }
    }

    private static RawProfilePickerState Project(
        RawProfileSelection? selection,
        ImmutableArray<RawProfileOptionViewModel> discovered,
        RawProfileRenderState? renderState,
        bool loading = false,
        string? error = null,
        RawProfileDiscoveryState? discoveryState = null) =>
        RawProfilePickerProjector.Project(
            isRawCapable: true,
            selection,
            discovered,
            new CameraIdentity("Canon", "EOS R5"),
            renderState,
            discoveryState ?? Completed(),
            loading,
            error);

    private static RawProfileDiscoveryState Completed(
        int profilesScanned = 0,
        int identityMatches = 0) => new(
            AdobeScanCompleted: true,
            profilesScanned,
            identityMatches);

    private static RawProfileOptionViewModel Option(
        RawProfileSelection selection,
        DcpProfileErrorCode status = DcpProfileErrorCode.None,
        string? message = null) => new(new DcpProfileOption(
            "Selected",
            selection,
            status,
            message));

    private static DcpProfileState State(
        RawProfileSelection selection,
        DcpProfileErrorCode status,
        string? message,
        string? label = null) => new(
            "token",
            status,
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
}
