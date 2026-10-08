using System.Collections.Immutable;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using Xunit;

namespace HappyPhoton.Tests;

public sealed partial class RawProfilePickerProjectorTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReplaceOptionPreservesExistingAdobePositionAndAppendsAbsentOption(bool alreadyPresent)
    {
        var preferred = Selection("wide.dcp", 'b');
        preferred.Source = RawProfileSource.Adobe;
        var other = Selection("front.dcp", 'a');
        other.Source = RawProfileSource.Adobe;
        var wide = new RawProfileOptionViewModel(new DcpProfileOption("Wide", preferred,
            DcpProfileErrorCode.None, null));
        var front = new RawProfileOptionViewModel(new DcpProfileOption("Front", other,
            DcpProfileErrorCode.None, null));
        var current = Project(other, alreadyPresent ? [wide, front] : [front], null);
        var replacement = wide.WithLabel("Updated Wide");

        var replaced = RawProfilePickerProjector.ReplaceOption(current.Options, replacement);
        var projected = Project(preferred, replaced, null);

        Assert.Equal(alreadyPresent ? ["Updated Wide", "Front"] : ["Front", "Updated Wide"],
            projected.Options
                .Where(option => option.IsProfile && option.Selection?.Source == RawProfileSource.Adobe)
                .Select(option => option.Label));
        Assert.Same(replacement, projected.SelectedOption);
        Assert.True(RawProfilePickerProjector.ProfilesEqual(preferred, projected.SelectedOption?.Selection));
        Assert.Single(projected.Options.Where(option => option.IsBuiltIn));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AdobeDiscoveryOrderSurvivesRetainedSelectionMerge(bool missingAnchor)
    {
        var selected = Selection("front.dcp", 'a');
        selected.Source = RawProfileSource.Adobe;
        var preferred = Selection("wide.dcp", 'b');
        preferred.Source = RawProfileSource.Adobe;
        var front = new RawProfileOptionViewModel(new DcpProfileOption("Front", selected,
            DcpProfileErrorCode.None, null) { Fingerprint = "front" });
        var wide = new RawProfileOptionViewModel(new DcpProfileOption("Wide", preferred,
            DcpProfileErrorCode.None, null) { Fingerprint = "wide" });
        var retained = missingAnchor ? RawProfileOptionViewModel.Anchor(selected) : front;
        var merged = RawProfilePickerProjector.MergeOptions([retained], [wide, front], selected);

        var projected = Project(selected, merged, null, discoveryState: Completed(2, 2));

        Assert.Equal(["Wide", retained.Label], projected.Options
            .Where(option => option.IsProfile && option.Selection?.Source == RawProfileSource.Adobe)
            .Select(option => option.Label));
        Assert.True(RawProfilePickerProjector.ProfilesEqual(selected, projected.SelectedOption?.Selection));
        Assert.DoesNotContain("NONE DECLARE", projected.StatusMessage);
    }

    [Fact]
    public void AdobeMenuKeepsDiscoveryOrderWithoutChangingBuiltInSelection()
    {
        var front = Selection("front.dcp", 'a');
        front.Source = RawProfileSource.Adobe;
        var wide = Selection("wide.dcp", 'b');
        wide.Source = RawProfileSource.Adobe;
        var projected = Project(null, ImmutableArray.Create(
            new RawProfileOptionViewModel(new DcpProfileOption("Wide", wide, DcpProfileErrorCode.None, null)),
            new RawProfileOptionViewModel(new DcpProfileOption("Front", front, DcpProfileErrorCode.None, null))), null);

        Assert.Equal(["Wide", "Front"], projected.Options
            .Where(option => option.IsProfile && option.Selection?.Source == RawProfileSource.Adobe)
            .Select(option => option.Label));
        Assert.True(projected.SelectedOption?.IsBuiltIn);
    }
}
