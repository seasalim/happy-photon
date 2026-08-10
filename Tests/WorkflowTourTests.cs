using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class WorkflowTourTests : IDisposable
{
    private readonly string _testRoot =
        Directory.CreateDirectory(Path.Combine(
            Path.GetTempPath(),
            $"happy-photon-tour-{Guid.NewGuid():N}")).FullName;

    [Fact]
    public async Task FirstRunCompletion_StartsTourInLibrary()
    {
        using var catalog = CreateCatalog();
        var vm = new MainWindowViewModel(catalog)
        {
            PersistFirstRunCompletionAsync = _ => Task.CompletedTask
        };
        vm.ShowFirstRunWelcome(_testRoot);

        await vm.CompleteFirstRunFromLocationAsync(_testRoot);

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
        var vm = new MainWindowViewModel(catalog);
        var image = new ImageFile(Path.Combine(_testRoot, "photo.jpg"))
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
        vm.Library.FileTypeFilter = ImageFileTypeFilter.Jpeg;
        vm.Library.FlagFilter = FlagFilter.Picked;
        vm.Library.MinimumRating = 3;
        vm.Library.SetImages([image]);
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
        Assert.Equal(ImageFileTypeFilter.Jpeg, vm.Library.FileTypeFilter);
        Assert.Equal(FlagFilter.Picked, vm.Library.FlagFilter);
        Assert.Equal(3, vm.Library.MinimumRating);
        await vm.DisposeAsync();
    }

    [Fact]
    public async Task TipSuspendsOutsideItsViewAndReturnsWithWorkspace()
    {
        using var catalog = CreateCatalog();
        var vm = new MainWindowViewModel(catalog);
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
        var vm = new MainWindowViewModel(catalog);

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
        var vm = new MainWindowViewModel(catalog);

        vm.SwitchToDevelopCommand.Execute(null);

        Assert.True(vm.IsDevelopMode);
        Assert.False(vm.HasSelectedImage);
        await vm.DisposeAsync();
    }

    [Fact]
    public async Task ExportCommand_RequestsDialogWithNoSelectedPhotographs()
    {
        using var catalog = CreateCatalog();
        var vm = new MainWindowViewModel(catalog)
        {
            CurrentFolderPath = _testRoot
        };
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
            Path.Combine(_testRoot, "export"),
            vm.ExportSettings.OutputFolder);
        await vm.DisposeAsync();
    }

    [Fact]
    public async Task ExportCommand_CanReopenAfterPreviousDialogCloses()
    {
        using var catalog = CreateCatalog();
        var vm = new MainWindowViewModel(catalog);
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
        var vm = new MainWindowViewModel(catalog)
        {
            IsFullScreenMode = true,
            RequestExportDialogAsync = _ =>
                throw new InvalidOperationException("Dialog must not open in fullscreen")
        };

        await vm.ShowExportDialogCommand.ExecuteAsync(null);

        Assert.Empty(vm.ExportSettings.OutputFolder);
        await vm.DisposeAsync();
    }

    [Fact]
    public async Task ExportCommand_FollowsFolderUntilDestinationIsCustomized()
    {
        using var catalog = CreateCatalog();
        var vm = new MainWindowViewModel(catalog)
        {
            RequestExportDialogAsync = _ => Task.CompletedTask
        };
        var folderA = Path.Combine(_testRoot, "a");
        var folderB = Path.Combine(_testRoot, "b");
        var folderC = Path.Combine(_testRoot, "c");

        vm.CurrentFolderPath = folderA;
        await vm.ShowExportDialogCommand.ExecuteAsync(null);
        Assert.Equal(Path.Combine(folderA, "export"), vm.ExportSettings.OutputFolder);

        vm.CurrentFolderPath = folderB;
        await vm.ShowExportDialogCommand.ExecuteAsync(null);
        Assert.Equal(Path.Combine(folderB, "export"), vm.ExportSettings.OutputFolder);

        var customFolder = Path.Combine(_testRoot, "deliveries");
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
        var vm = new MainWindowViewModel(catalog);

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
        new(Path.Combine(_testRoot, Guid.NewGuid().ToString("N")));

    public void Dispose()
    {
        if (Directory.Exists(_testRoot))
        {
            Directory.Delete(_testRoot, recursive: true);
        }
    }
}
