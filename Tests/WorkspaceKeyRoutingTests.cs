using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class WorkspaceKeyRoutingTests : IDisposable
{
    private readonly string _testRoot =
        Directory.CreateDirectory(Path.Combine(
            Path.GetTempPath(),
            $"happy-photon-keys-{Guid.NewGuid():N}")).FullName;

    [Fact]
    public async Task SpaceShortcut_TogglesSelectionAndConsumesBothKeyPhases()
    {
        using var catalog = new CatalogService(
            Path.Combine(_testRoot, Guid.NewGuid().ToString("N")));
        var vm = new MainWindowViewModel(catalog);
        vm.ShowWorkspaceReady(MainWindowViewModel.CurrentFirstRunExperienceVersion);
        var image = new ImageFile(Path.Combine(_testRoot, "photo.jpg"));
        vm.Library.SetImages([image]);
        vm.SelectedImage = image;
        vm.RefreshSelectedCount();
        var dialogRequests = 0;
        vm.RequestExportDialogAsync = _ =>
        {
            dialogRequests++;
            return Task.CompletedTask;
        };

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
        Assert.Equal(0, dialogRequests);
        await vm.DisposeAsync();
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_testRoot, recursive: true);
        }
        catch
        {
        }
    }
}
