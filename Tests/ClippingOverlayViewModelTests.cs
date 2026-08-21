using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class ClippingOverlayViewModelTests : IDisposable
{
    private readonly CatalogVmFixture _fx = new("clipping-vm");

    [Fact]
    public async Task Shortcut_IsDevelopOnlyAndLatchIsSessionState()
    {
        using var catalog = _fx.CreateCatalog("catalog");
        await using var vm = _fx.CreateViewModel(
            catalog,
            new NullBaseLoader(),
            loadMetadataAsync: _ => Task.CompletedTask,
            availabilityService: new TestSourceAvailabilityService(
                SourceAvailability.AvailableLocally));
        vm.SelectedImage = new ImageFile(_fx.Path("photo.jpg"));

        Assert.False(vm.ToggleClippingOverlayCommand.CanExecute(null));
        vm.ShowWorkspaceReady(
            MainWindowViewModel.CurrentFirstRunExperienceVersion);
        Assert.False(vm.ToggleClippingOverlayCommand.CanExecute(null));

        vm.IsDevelopMode = true;
        Assert.True(vm.ToggleClippingOverlayCommand.CanExecute(null));
        vm.ToggleClippingOverlayCommand.Execute(null);

        Assert.True(vm.IsClippingOverlayLatched);
        Assert.Equal("Clipping indicators on", vm.AssessmentFeedback);
        Assert.Equal(
            ClippingOverlaySide.DisplayFloor,
            vm.RequestedClippingOverlaySides);

        vm.IsFullScreenMode = true;
        Assert.False(vm.ToggleClippingOverlayCommand.CanExecute(null));
        Assert.Equal(
            ClippingOverlaySide.None,
            vm.RequestedClippingOverlaySides);
        Assert.True(vm.IsClippingOverlayLatched);

        vm.IsFullScreenMode = false;
        Assert.True(vm.ToggleClippingOverlayCommand.CanExecute(null));
        vm.ToggleClippingOverlayCommand.Execute(null);
        Assert.False(vm.IsClippingOverlayLatched);
        Assert.Equal("Clipping indicators off", vm.AssessmentFeedback);

        vm.IsDevelopMode = false;
        Assert.False(vm.ToggleClippingOverlayCommand.CanExecute(null));
        Assert.False(vm.IsClippingOverlayLatched);
    }

    public void Dispose() => _fx.Dispose();
}
