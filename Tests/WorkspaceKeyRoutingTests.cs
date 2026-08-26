using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class WorkspaceKeyRoutingTests : IDisposable
{
    private readonly TemporaryDirectory _testRoot = new();

    [Fact]
    public async Task SpaceShortcut_TogglesSelectionAndConsumesBothKeyPhases()
    {
        using var catalog = new CatalogService(
            Path.Combine(_testRoot.Path, Guid.NewGuid().ToString("N")));
        var vm = new MainWindowViewModel(
            catalog,
            baseLoader: null,
            loadMetadataAsync: _ => Task.CompletedTask);
        vm.ShowWorkspaceReady(MainWindowViewModel.CurrentFirstRunExperienceVersion);
        var image = new ImageFile(Path.Combine(_testRoot.Path, "photo.jpg"));
        vm.Browse.SetImages([image]);
        vm.SelectedImage = image;
        vm.RefreshSelectedCount();

        var handledDown = WorkspaceKeyRouting.TryHandleSpace(
            vm,
            toggleSelection: true);
        var handledUp = WorkspaceKeyRouting.TryHandleSpace(
            vm,
            toggleSelection: false);

        Assert.True(handledDown);
        Assert.True(handledUp);
        Assert.True(image.IsSelected);
        Assert.Equal(1, vm.SelectedCount);
        await vm.DisposeAsync();
    }

    public void Dispose() => _testRoot.Dispose();
}
