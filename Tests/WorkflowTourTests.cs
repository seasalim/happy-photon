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
        var vm = _fx.CreateViewModel(catalog);
        vm.PersistFirstRunCompletionAsync = _ => Task.CompletedTask;
        vm.ShowFirstRunWelcome(_fx.Root);

        await vm.CompleteFirstRunFromLocationAsync(_fx.Root);
        Assert.Equal(FirstRunStep.AllSet, vm.FirstRunStep);
        Assert.Equal(WorkflowTourStep.None, vm.WorkflowTourStep);

        await vm.StartFirstRunTourCommand.ExecuteAsync(null);

        Assert.Equal(
            WorkflowTourStep.ChooseWhatMatters,
            vm.WorkflowTourStep);
        Assert.True(vm.IsChooseWhatMattersTourVisible);
        Assert.False(vm.IsDevelopMode);
        await vm.DisposeAsync();
    }

    [Fact]
    public async Task TourTransitions_DoNotChangePhotographStateOrSelection()
    {
        using var catalog = CreateCatalog();
        var vm = _fx.CreateViewModel(catalog);
        var image = new ImageFile(_fx.Path("photo.jpg"))
        {
            Flag = ImageFlag.Picked,
            Rating = 4,
            EditSettings = new EditSettings
            {
                Exposure = 1.25,
                Wb = new WhiteBalanceSettings
                {
                    Mode = WbMode.Custom,
                    Kelvin = 6800,
                    Tint = 6
                }
            }
        };
        vm.Browse.FileTypeFilter = ImageFileTypeFilter.Jpeg;
        vm.Browse.FlagFilter = FlagFilter.Picked;
        vm.Browse.MinimumRating = 3;
        vm.Browse.SetImages([image]);
        vm.RefreshSelectedCount();
        var dialogRequests = 0;
        ExportDialogMode? requestedMode = null;
        vm.RequestExportDialogAsync = mode =>
        {
            dialogRequests++;
            requestedMode = mode;
            return Task.CompletedTask;
        };

        vm.StartWorkflowTour();
        vm.ShowDevelopTourStepCommand.Execute(null);

        Assert.Equal(WorkflowTourStep.ShapePhotograph, vm.WorkflowTourStep);
        Assert.True(vm.IsShapePhotographTourVisible);

        vm.ShowExportTourStepCommand.Execute(null);

        Assert.Equal(WorkflowTourStep.DeliverCopies, vm.WorkflowTourStep);
        Assert.True(vm.IsDeliverCopiesTourVisible);

        await vm.OpenExportFromTourCommand.ExecuteAsync(null);

        Assert.Equal(WorkflowTourStep.None, vm.WorkflowTourStep);
        Assert.Equal(1, dialogRequests);
        Assert.Equal(ExportDialogMode.TourPreview, requestedMode);
        Assert.False(image.IsSelected);
        Assert.Equal(0, vm.SelectedCount);
        Assert.Equal(ImageFlag.Picked, image.Flag);
        Assert.Equal(4, image.Rating);
        Assert.Equal(1.25, image.EditSettings.Exposure);
        Assert.Equal(WbMode.Custom, image.EditSettings.Wb.Mode);
        Assert.Equal(6800, image.EditSettings.Wb.Kelvin);
        Assert.Equal(6, image.EditSettings.Wb.Tint);
        Assert.Equal(ImageFileTypeFilter.Jpeg, vm.Browse.FileTypeFilter);
        Assert.Equal(FlagFilter.Picked, vm.Browse.FlagFilter);
        Assert.Equal(3, vm.Browse.MinimumRating);
        await vm.DisposeAsync();
    }

    [Fact]
    public async Task TipSuspendsOutsideItsViewAndReturnsWithWorkspace()
    {
        using var catalog = CreateCatalog();
        var vm = _fx.CreateViewModel(catalog);
        vm.StartWorkflowTour();

        vm.IsDevelopMode = true;

        Assert.Equal(
            WorkflowTourStep.ChooseWhatMatters,
            vm.WorkflowTourStep);
        Assert.False(vm.IsChooseWhatMattersTourVisible);

        vm.IsDevelopMode = false;

        Assert.True(vm.IsChooseWhatMattersTourVisible);
        await vm.DisposeAsync();
    }

    [Fact]
    public async Task PresentedTourState_TracksVisibleCoachmarksOnly()
    {
        using var catalog = CreateCatalog();
        var vm = _fx.CreateViewModel(catalog);

        Assert.False(vm.IsWorkflowTourPresented);

        vm.StartWorkflowTour();
        Assert.True(vm.IsWorkflowTourPresented);

        vm.IsDevelopMode = true;
        Assert.False(vm.IsWorkflowTourPresented);
        Assert.Equal(
            WorkflowTourStep.ChooseWhatMatters,
            vm.WorkflowTourStep);

        vm.IsDevelopMode = false;
        Assert.True(vm.IsWorkflowTourPresented);

        vm.ShowDevelopTourStepCommand.Execute(null);
        Assert.True(vm.IsWorkflowTourPresented);

        vm.IsDevelopMode = false;
        Assert.False(vm.IsWorkflowTourPresented);

        vm.IsDevelopMode = true;
        vm.ShowExportTourStepCommand.Execute(null);
        Assert.True(vm.IsWorkflowTourPresented);

        vm.FinishWorkflowTourCommand.Execute(null);
        Assert.False(vm.IsWorkflowTourPresented);

        vm.StartWorkflowTour();
        vm.EndWorkflowTourCommand.Execute(null);
        Assert.False(vm.IsWorkflowTourPresented);
        await vm.DisposeAsync();
    }

    [Fact]
    public async Task DevelopCommand_OpensWorkspaceWithoutSelectedPhotograph()
    {
        using var catalog = CreateCatalog();
        var vm = _fx.CreateViewModel(catalog);

        vm.SwitchToDevelopCommand.Execute(null);

        Assert.True(vm.IsDevelopMode);
        Assert.False(vm.HasSelectedImage);
        await vm.DisposeAsync();
    }

    [Fact]
    public async Task ExportCommand_RequestsDialogWithNoSelectedPhotographs()
    {
        using var catalog = CreateCatalog();
        var vm = _fx.CreateViewModel(catalog);
        vm.CurrentFolderPath = _fx.Root;
        var dialogRequests = 0;
        ExportDialogMode? requestedMode = null;
        vm.RequestExportDialogAsync = mode =>
        {
            dialogRequests++;
            requestedMode = mode;
            return Task.CompletedTask;
        };

        await vm.ShowExportDialogCommand.ExecuteAsync(null);

        Assert.Equal(1, dialogRequests);
        Assert.Equal(ExportDialogMode.Standard, requestedMode);
        Assert.Equal(0, vm.SelectedCount);
        Assert.Equal(
            _fx.Path("export"),
            vm.ExportSettings.OutputFolder);
        await vm.DisposeAsync();
    }

    [Fact]
    public async Task ExportCommand_CanReopenAfterPreviousDialogCloses()
    {
        using var catalog = CreateCatalog();
        var vm = _fx.CreateViewModel(catalog);
        var dialogRequests = 0;
        vm.RequestExportDialogAsync = _ =>
        {
            dialogRequests++;
            return Task.CompletedTask;
        };

        await vm.ShowExportDialogCommand.ExecuteAsync(null);
        await vm.ShowExportDialogCommand.ExecuteAsync(null);

        Assert.Equal(2, dialogRequests);
        await vm.DisposeAsync();
    }

    [Fact]
    public async Task ExportCommand_IsIgnoredInFullScreen()
    {
        using var catalog = CreateCatalog();
        var vm = _fx.CreateViewModel(catalog);
        vm.IsFullScreenMode = true;
        vm.RequestExportDialogAsync = _ =>
            throw new InvalidOperationException("Dialog must not open in fullscreen");

        await vm.ShowExportDialogCommand.ExecuteAsync(null);

        Assert.Empty(vm.ExportSettings.OutputFolder);
        await vm.DisposeAsync();
    }

    [Fact]
    public async Task ExportCommand_FollowsFolderUntilDestinationIsCustomized()
    {
        using var catalog = CreateCatalog();
        var vm = _fx.CreateViewModel(catalog);
        vm.RequestExportDialogAsync = _ => Task.CompletedTask;
        var folderA = _fx.Path("a");
        var folderB = _fx.Path("b");
        var folderC = _fx.Path("c");

        vm.CurrentFolderPath = folderA;
        await vm.ShowExportDialogCommand.ExecuteAsync(null);
        Assert.Equal(Path.Combine(folderA, "export"), vm.ExportSettings.OutputFolder);

        vm.CurrentFolderPath = folderB;
        await vm.ShowExportDialogCommand.ExecuteAsync(null);
        Assert.Equal(Path.Combine(folderB, "export"), vm.ExportSettings.OutputFolder);

        var customFolder = _fx.Path("deliveries");
        vm.ExportSettings.OutputFolder = customFolder;
        vm.CurrentFolderPath = folderC;
        await vm.ShowExportDialogCommand.ExecuteAsync(null);

        Assert.Equal(customFolder, vm.ExportSettings.OutputFolder);
        await vm.DisposeAsync();
    }

    [Fact]
    public async Task EndTour_DismissesEveryStepWithoutChangingView()
    {
        using var catalog = CreateCatalog();
        var vm = _fx.CreateViewModel(catalog);

        vm.StartWorkflowTour();
        vm.EndWorkflowTourCommand.Execute(null);
        Assert.Equal(WorkflowTourStep.None, vm.WorkflowTourStep);
        Assert.False(vm.IsDevelopMode);

        vm.StartWorkflowTour();
        vm.ShowDevelopTourStepCommand.Execute(null);
        vm.EndWorkflowTourCommand.Execute(null);
        Assert.Equal(WorkflowTourStep.None, vm.WorkflowTourStep);
        Assert.True(vm.IsDevelopMode);

        vm.StartWorkflowTour();
        vm.ShowDevelopTourStepCommand.Execute(null);
        vm.ShowExportTourStepCommand.Execute(null);
        vm.EndWorkflowTourCommand.Execute(null);
        Assert.Equal(WorkflowTourStep.None, vm.WorkflowTourStep);
        Assert.False(vm.IsDevelopMode);
        await vm.DisposeAsync();
    }

    private CatalogService CreateCatalog() =>
        _fx.CreateCatalog(Guid.NewGuid().ToString("N"));

    public void Dispose() => _fx.Dispose();
}
