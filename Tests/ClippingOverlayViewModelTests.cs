using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class ClippingOverlayViewModelTests : IDisposable
{
    private readonly string _root = Directory.CreateDirectory(Path.Combine(
        Path.GetTempPath(),
        $"happy-photon-clipping-vm-{Guid.NewGuid():N}")).FullName;

    [Fact]
    public async Task Shortcut_IsDevelopOnlyAndLatchIsSessionState()
    {
        using var catalog = new CatalogService(Path.Combine(_root, "catalog"));
        await using var vm = new MainWindowViewModel(
            catalog,
            new NullBaseLoader(),
            loadMetadataAsync: _ => Task.CompletedTask,
            availabilityService: new TestSourceAvailabilityService(
                SourceAvailability.AvailableLocally));
        vm.SelectedImage = new ImageFile(Path.Combine(_root, "photo.jpg"));

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

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.Delete(_root, recursive: true);
    }

    private sealed class NullBaseLoader : IBaseImageLoader
    {
        public bool CanLoad(ImageFile file) => true;

        BaseImageLoadOutcome IBaseImageLoader.LoadPreviewBaseWithOutcome(
            ImageFile file,
            BaseDecodeSettings decode,
            CancellationToken cancellationToken) =>
            BaseImageLoadOutcome.FromImage(null, BaseImageLoadFailure.DecodeFailed);

        public BaseImage? LoadPreviewBase(
            ImageFile file,
            BaseDecodeSettings decode,
            CancellationToken cancellationToken) => null;

        public BaseImage? LoadFullBase(
            ImageFile file,
            BaseDecodeSettings decode,
            CancellationToken cancellationToken) => null;
    }
}
