using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class WorkspaceModeTests : IDisposable
{
    private readonly CatalogVmFixture _fx = new("workspace-mode");

    [Fact]
    public async Task IsDevelopMode_SetterMapsToBrowseAndDevelop()
    {
        await using var vm = CreateViewModel();

        vm.IsDevelopMode = true;

        Assert.Equal(WorkspaceMode.Develop, vm.WorkspaceMode);
        Assert.True(vm.IsDevelopMode);
        Assert.False(vm.IsBrowseMode);

        vm.IsDevelopMode = false;

        Assert.Equal(WorkspaceMode.Browse, vm.WorkspaceMode);
        Assert.False(vm.IsDevelopMode);
        Assert.True(vm.IsBrowseMode);
    }

    [Theory]
    [InlineData(WorkspaceMode.Browse, false, true)]
    [InlineData(WorkspaceMode.Develop, true, false)]
    public async Task WorkspaceMode_DrivesCompatibilityShims(
        WorkspaceMode mode,
        bool isDevelopMode,
        bool isBrowseMode)
    {
        await using var vm = CreateViewModel();

        vm.WorkspaceMode = mode;

        Assert.Equal(isDevelopMode, vm.IsDevelopMode);
        Assert.Equal(isBrowseMode, vm.IsBrowseMode);
    }

    [Fact]
    public async Task ModeTransition_RunsCallbacksBeforeShimNotifications()
    {
        await using var vm = CreateViewModel();
        var events = new List<string>();
        vm.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(vm.IsDevelopPreviewSurfaceActive) or
                nameof(vm.IsDevelopMode) or nameof(vm.IsBrowseMode))
            {
                events.Add(args.PropertyName);
            }
        };
        vm.PasteEditSettingsCommand.CanExecuteChanged +=
            (_, _) => events.Add("PasteEditSettingsCommand");

        vm.WorkspaceMode = WorkspaceMode.Develop;

        Assert.Equal(
            [
                nameof(vm.IsDevelopPreviewSurfaceActive),
                "PasteEditSettingsCommand",
                nameof(vm.IsDevelopMode),
                nameof(vm.IsBrowseMode)
            ],
            events);
    }

    [Fact]
    public async Task RedundantAssignment_RunsNoCallbacksOrNotifications()
    {
        await using var vm = CreateViewModel();
        var hubNotifications = 0;
        var shimNotifications = 0;
        var pasteNotifications = 0;
        vm.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(vm.IsDevelopPreviewSurfaceActive))
            {
                hubNotifications++;
            }
            if (args.PropertyName is nameof(vm.IsDevelopMode) or
                nameof(vm.IsBrowseMode))
            {
                shimNotifications++;
            }
        };
        vm.PasteEditSettingsCommand.CanExecuteChanged +=
            (_, _) => pasteNotifications++;

        vm.WorkspaceMode = WorkspaceMode.Browse;

        Assert.Equal(0, hubNotifications);
        Assert.Equal(0, pasteNotifications);
        Assert.Equal(0, shimNotifications);
    }

    [Fact]
    public async Task LeavingDevelopForExport_RunsDevelopSurfaceTeardown()
    {
        await using var vm = CreateViewModel();
        vm.SelectedImage = new ImageFile(Path.Combine(
            Path.GetTempPath(),
            "workspace-mode-selected.jpg"));
        vm.WorkspaceMode = WorkspaceMode.Develop;
        var clippingCommandNotifications = 0;
        vm.ToggleClippingOverlayCommand.CanExecuteChanged +=
            (_, _) => clippingCommandNotifications++;

        vm.WorkspaceMode = WorkspaceMode.Export;

        Assert.Equal(2, clippingCommandNotifications);
    }

    [Fact]
    public async Task EnteringDevelopWithoutSelection_DoesNotReserveRenderOutcome()
    {
        await using var vm = CreateViewModel();
        var generationBeforeDevelop = vm.LatestPreviewOutcomeGeneration;

        vm.WorkspaceMode = WorkspaceMode.Develop;

        Assert.Equal(
            generationBeforeDevelop,
            vm.LatestPreviewOutcomeGeneration);
    }

    private MainWindowViewModel CreateViewModel() =>
        _fx.CreateViewModel(
            _fx.CreateCatalog(),
            new NullBaseLoader(),
            loadMetadataAsync: _ => Task.CompletedTask,
            availabilityService: new TestSourceAvailabilityService(
                SourceAvailability.AvailableLocally));

    public void Dispose() => _fx.Dispose();
}
