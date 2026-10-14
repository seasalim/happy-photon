using System.Collections.Immutable;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using Xunit;

namespace HappyPhoton.Tests;

public sealed partial class RawProfilePickerProjectorTests
{
    [Fact]
    public async Task ModuleLabelSurvivesRenderRelabelAndRediscoveryMerge()
    {
        using var directory = new TemporaryDirectory();
        var modules = new[] { "Wide", "Ultrawide" };

        foreach (var module in modules)
        {
            SyntheticDcpFactory.WriteTemporary(directory.Path, new SyntheticDcpOptions
            {
                Name = "Adobe Standard",
                UniqueCameraModel = $"Google Pixel 9 {module} Camera",
                ColorMatrix1 = [1 + Array.IndexOf(modules, module) * 0.01, 0, 0, 0, 1, 0, 0, 0, 1]
            }, $"{module}.dcp");
        }

        var discovery = new DcpProfileDiscovery(
            new TestSourceAvailabilityService(SourceAvailability.AvailableLocally), adobeRoots: [directory.Path]);
        var result = await discovery.DiscoverAsync(new ImageFile(Path.Combine(directory.Path, "image.raw")),
            new CameraIdentity("Google", "Pixel 9"), CancellationToken.None);
        var discovered = result.Options.Select(option => new RawProfileOptionViewModel(option)).ToImmutableArray();
        var ultrawide = discovered.Single(option => option.Label == "Adobe Standard · Ultrawide Camera");
        var selection = ultrawide.Selection!;
        var render = new RawProfileRenderState(selection,
            State(selection, DcpProfileErrorCode.None, null, DcpRenderedLabel.For(selection)));

        var projected = Project(selection, discovered, render, discoveryState: Completed(2, 2));
        var merged = RawProfilePickerProjector.MergeOptions(projected.Options, discovered, selection);
        var rediscovered = Project(selection, merged, render, discoveryState: Completed(2, 2));

        Assert.Equal(ultrawide.Label, projected.SelectedOption?.Label);
        Assert.Equal(ultrawide.Label, rediscovered.SelectedOption?.Label);
        Assert.Equal(["Adobe Standard · Ultrawide Camera", "Adobe Standard · Wide Camera"], rediscovered.Options
            .Where(option => option.IsProfile && option.Selection?.Source == RawProfileSource.Adobe)
            .Select(option => option.Label)
            .Order());
    }
}
