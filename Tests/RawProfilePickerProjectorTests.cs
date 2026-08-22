using System.Collections.Immutable;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class RawProfilePickerProjectorTests
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
            RawProfilePickerProjector.NoProfilesMessage,
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
    public void EmptyClaimsRequireTheirBackingDiscoveryScopes()
    {
        Assert.Equal(
            RawProfilePickerProjector.ScanningMessage,
            Project(
                selection: null,
                discovered: [],
                renderState: null,
                discoveryState: RawProfileDiscoveryState.Empty).StatusMessage);
        Assert.Equal(
            "3 LOCAL CAMERA PROFILES SCANNED · NONE DECLARE CANON EOS R5",
            Project(
                selection: null,
                discovered: [],
                renderState: null,
                discoveryState: Completed(
                    profilesScanned: 3,
                    identityMatches: 0,
                    imageProfilesCompleted: false)).StatusMessage);
        Assert.Equal(
            RawProfilePickerProjector.ScanningMessage,
            Project(
                selection: null,
                discovered: [],
                renderState: null,
                discoveryState: Completed(
                    profilesScanned: 0,
                    identityMatches: 0,
                    imageProfilesCompleted: false)).StatusMessage);
        Assert.Equal(
            RawProfilePickerProjector.NoProfilesMessage,
            Project(
                selection: null,
                discovered: [],
                renderState: null,
                discoveryState: Completed()).StatusMessage);
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
        int identityMatches = 0,
        bool imageProfilesCompleted = true) => new(
            AdobeScanCompleted: true,
            imageProfilesCompleted,
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
