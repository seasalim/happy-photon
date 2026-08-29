using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using HappyPhoton.Views;
using Avalonia.Controls;
using System.Runtime.CompilerServices;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class WorkspaceKeyRoutingTests : IDisposable
{
    private readonly TemporaryDirectory _testRoot = new();

    [Fact]
    public async Task SpaceShortcut_OpensLoupeThenTogglesActualSize()
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

        var focusedGrid = (BrowseGridView)RuntimeHelpers.GetUninitializedObject(
            typeof(BrowseGridView));
        var opened = WorkspaceKeyRouting.TryHandleSpace(vm, focusedGrid);
        var zoomed = WorkspaceKeyRouting.TryHandleSpace(vm, focusedGrid);

        Assert.True(opened);
        Assert.True(zoomed);
        Assert.True(vm.IsLoupeMode);
        Assert.False(vm.IsZoomFitMode);
        Assert.False(image.IsSelected);
        Assert.Equal(0, vm.SelectedCount);
        Assert.False(WorkspaceKeyRouting.TryHandleSpace(vm, new TextBox()));
        Assert.False(WorkspaceKeyRouting.TryHandleSpace(vm, new Button()));
        Assert.False(WorkspaceKeyRouting.TryHandleSpace(vm, new TreeView()));
        await vm.DisposeAsync();
    }

    public void Dispose() => _testRoot.Dispose();
}
