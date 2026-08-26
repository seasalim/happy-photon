using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class WorkflowTourTests : IDisposable
{
    private readonly CatalogVmFixture _fx = new("tour");

    [Fact]
    public async Task FirstRunStartTourChoice_StartsTourInBrowse()
    {
        using var catalog = CreateCatalog();
        await using var vm = CreateViewModel(catalog);
        vm.PersistFirstRunCompletionAsync = _ => Task.CompletedTask;
        vm.ShowFirstRunWelcome(_fx.Root);

        await vm.CompleteFirstRunFromLocationAsync(_fx.Root);
        await vm.StartFirstRunTourCommand.ExecuteAsync(null);

        Assert.Equal(WorkflowTourStep.ChooseWhatMatters, vm.WorkflowTourStep);
        Assert.True(vm.IsChooseWhatMattersTourVisible);
        Assert.Equal(1, MainWindowViewModel.CurrentFirstRunExperienceVersion);
    }

    [Fact]
    public async Task TourStepThree_IsPinnedToExportWorkspace()
    {
        using var catalog = CreateCatalog();
        await using var vm = CreateViewModel(catalog);
        var image = new ImageFile(_fx.Path("photo.jpg"))
        {
            Flag = ImageFlag.Picked,
            Rating = 4,
            EditSettings = new EditSettings { Exposure = 1.25 }
        };
        vm.Browse.SetImages([image]);
        vm.Browse.ToggleSelection(image);
        vm.RefreshSelectedCount();

        vm.StartWorkflowTour();
        vm.ShowDevelopTourStepCommand.Execute(null);
        vm.ShowExportTourStepCommand.Execute(null);

        Assert.Equal(WorkflowTourStep.DeliverCopies, vm.WorkflowTourStep);
        Assert.Equal(WorkspaceMode.Export, vm.WorkspaceMode);
        Assert.True(vm.IsDeliverCopiesTourVisible);
        Assert.False(vm.IsChooseWhatMattersTourVisible);
        Assert.False(vm.IsShapePhotographTourVisible);
        Assert.True(image.IsSelected);
        Assert.Equal(ImageFlag.Picked, image.Flag);
        Assert.Equal(4, image.Rating);
        Assert.Equal(1.25, image.EditSettings.Exposure);
    }

    [Fact]
    public async Task TourTip_SuspendsOutsideItsWorkspaceAndReturns()
    {
        using var catalog = CreateCatalog();
        await using var vm = CreateViewModel(catalog);
        vm.StartWorkflowTour();

        vm.SwitchToDevelopCommand.Execute(null);
        Assert.False(vm.IsChooseWhatMattersTourVisible);

        vm.SwitchToBrowseCommand.Execute(null);
        Assert.True(vm.IsChooseWhatMattersTourVisible);
    }

    [Fact]
    public async Task ExportWorkspaceCommand_FollowsAutomaticDestinationUntilCustomized()
    {
        using var catalog = CreateCatalog();
        await using var vm = CreateViewModel(catalog);
        var folderA = _fx.Path("a");
        var folderB = _fx.Path("b");
        var folderC = _fx.Path("c");

        vm.CurrentFolderPath = folderA;
        vm.SwitchToExportCommand.Execute(null);
        Assert.Equal(Path.Combine(folderA, "export"), vm.ExportSettings.OutputFolder);

        vm.HandleEscapeCommand.Execute(null);
        vm.CurrentFolderPath = folderB;
        vm.SwitchToExportCommand.Execute(null);
        Assert.Equal(Path.Combine(folderB, "export"), vm.ExportSettings.OutputFolder);

        vm.HandleEscapeCommand.Execute(null);
        var customFolder = _fx.Path("deliveries");
        vm.ExportSettings.OutputFolder = customFolder;
        vm.CurrentFolderPath = folderC;
        vm.SwitchToExportCommand.Execute(null);
        Assert.Equal(customFolder, vm.ExportSettings.OutputFolder);
    }

    [Fact]
    public async Task ExportWorkspaceCommand_IsIgnoredInFullScreen()
    {
        using var catalog = CreateCatalog();
        await using var vm = CreateViewModel(catalog);
        vm.IsFullScreenMode = true;

        vm.SwitchToExportCommand.Execute(null);

        Assert.False(vm.IsExportMode);
        Assert.Empty(vm.ExportSettings.OutputFolder);
    }

    [Fact]
    public async Task EndTour_DismissesStepWithoutChangingWorkspace()
    {
        using var catalog = CreateCatalog();
        await using var vm = CreateViewModel(catalog);
        vm.StartWorkflowTour();
        vm.ShowDevelopTourStepCommand.Execute(null);
        vm.ShowExportTourStepCommand.Execute(null);

        vm.EndWorkflowTourCommand.Execute(null);

        Assert.Equal(WorkflowTourStep.None, vm.WorkflowTourStep);
        Assert.True(vm.IsExportMode);
    }

    private CatalogService CreateCatalog() =>
        _fx.CreateCatalog(Guid.NewGuid().ToString("N"));

    private MainWindowViewModel CreateViewModel(CatalogService catalog) =>
        _fx.CreateViewModel(
            catalog,
            new NullBaseLoader(),
            loadMetadataAsync: _ => Task.CompletedTask);

    public void Dispose() => _fx.Dispose();
}
