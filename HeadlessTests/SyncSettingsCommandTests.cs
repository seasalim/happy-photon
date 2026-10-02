using System.Reflection;
using Avalonia.Headless.XUnit;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using Xunit;

namespace HappyPhoton.Tests;

public sealed partial class SyncSettingsCommandTests
{
    private const string SelectionTip = "Select two or more photos to sync. The outlined photo is the source.";

    private const string DownloadTip = "Download online-only originals to sync settings";

    [AvaloniaTheory]
    [InlineData("develop", SelectionTip)]
    [InlineData("export", SelectionTip)]
    [InlineData("fullscreen", SelectionTip)]
    [InlineData("compare", "Leave Compare to sync settings")]
    [InlineData("none", SelectionTip)]
    [InlineData("one", SelectionTip)]
    [InlineData("unselected", "Select the outlined photo too; it is the source.")]
    [InlineData("claimed-source", SelectionTip)]
    [InlineData("claimed-target", SelectionTip)]
    [InlineData("online-source", DownloadTip)]
    [InlineData("online-target", DownloadTip)]
    [InlineData("grid", null)]
    [InlineData("loupe", null)]
    public async Task EnablementAndTooltip(string state, string? tooltip)
    {
        await using var fixture = new SyncTransferParityVm();
        await fixture.InitializeAsync();
        var source = await fixture.ImageAsync("source", new());
        var target = await fixture.ImageAsync("target", new());
        var vm = fixture.Vm;
        SelectForSync(vm, state == "unselected"
            ? [source, target, new ImageFile(target.FilePath + ".other.jpg")]
            : [source, target]);

        switch (state)
        {
            case "develop":
                vm.IsDevelopMode = true;
                break;

            case "export":
                vm.WorkspaceMode = WorkspaceMode.Export;
                break;

            case "fullscreen":
                vm.IsFullScreenMode = true;
                break;

            case "compare":
                vm.EnterCompareCommand.Execute(null);
                break;

            case "none":
                vm.DeselectAllCommand.Execute(null);
                vm.SelectedImage = null;
                break;

            case "one":
                vm.ToggleImageSelection(target);
                break;

            case "unselected":
                vm.ToggleImageSelection(source);
                // Explicitly stage the outside-active state; Browse toggles now re-anchor.
                vm.SelectedImage = source;
                Assert.Equal(2, vm.SelectedCount);
                break;

            case "claimed-source":
                Claim(vm, source);
                break;

            case "claimed-target":
                Claim(vm, target);
                break;

            case "online-source":
                source.SourceRequiresHydration = true;
                break;

            case "online-target":
                target.SourceRequiresHydration = true;
                break;

            case "loupe":
                vm.EnterLoupeCommand.Execute(null);
                break;
        }

        Assert.Equal(tooltip == null, vm.SyncSettingsCommand.CanExecute(null));
        Assert.Equal(tooltip ?? "Sync settings from source.jpg to 1 photo (Ctrl+Shift+S)", vm.SyncSettingsToolTip);
    }

    [AvaloniaFact]
    public async Task CompareAndActivePhotoChangesNotifyWithoutCountChanges()
    {
        await using var fixture = new SyncTransferParityVm();
        await fixture.InitializeAsync();
        var source = await fixture.ImageAsync("source", new());
        var target = await fixture.ImageAsync("target", new());
        var vm = fixture.Vm;
        SelectForSync(vm, source, target);
        var commands = 0;
        var tooltips = 0;
        vm.SyncSettingsCommand.CanExecuteChanged += (_, _) => commands++;
        vm.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(vm.SyncSettingsToolTip)) tooltips++;
        };
        vm.EnterCompareCommand.Execute(null);
        Assert.Same(source, vm.SelectedImage);
        Assert.Equal(2, vm.SelectedCount);
        Assert.False(vm.SyncSettingsCommand.CanExecute(null));
        Assert.True(commands > 0 && tooltips > 0);
        commands = tooltips = 0;
        vm.ExitCompareCommand.Execute(null);
        Assert.Same(source, vm.SelectedImage);
        Assert.Equal(2, vm.SelectedCount);
        Assert.True(vm.SyncSettingsCommand.CanExecute(null));
        Assert.True(commands > 0 && tooltips > 0);
        commands = tooltips = 0;
        vm.SelectedImage = target;
        Assert.Equal(2, vm.SelectedCount);
        Assert.Contains("target.jpg", vm.SyncSettingsToolTip);
        Assert.True(commands > 0 && tooltips > 0);
    }

    [AvaloniaFact]
    public async Task BoundCommandAndTooltipShareOneScanPerSelectionChange()
    {
        await using var fixture = new SyncTransferParityVm();
        await fixture.InitializeAsync();
        var photos = new List<ImageFile>();

        for (var index = 0; index < 4; index++)
        {
            photos.Add(await fixture.ImageAsync($"photo{index}", new()));
        }

        var vm = fixture.Vm;
        SelectForSync(vm, [.. photos]);
        // Bound the way WP2's button will be: both readers react to each notification.
        vm.SyncSettingsCommand.CanExecuteChanged += (_, _) => vm.SyncSettingsCommand.CanExecute(null);
        vm.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(vm.SyncSettingsToolTip)) _ = vm.SyncSettingsToolTip;
        };
        var scans = vm.SyncSelectionScanCount;

        vm.ToggleImageSelection(photos[3]);

        Assert.Equal(scans + 1, vm.SyncSelectionScanCount);
        Assert.Contains("to 2 photos", vm.SyncSettingsToolTip);
    }

    [AvaloniaFact]
    public async Task TwoHundredArrowStepsWithOneSelectionNeverScanTenThousandPhotos()
    {
        using var fixture = new CatalogVmFixture("sync-navigation");
        using var catalog = await fixture.CreateCatalogAsync();
        await using var vm = fixture.CreateViewModel(catalog, new NullBaseLoader(), _ => Task.CompletedTask,
            new TestSourceAvailabilityService(SourceAvailability.AvailableLocally), timeProvider: new TestTimeProvider());
        var photos = Enumerable.Range(0, 10000)
            .Select(index => new ImageFile(fixture.Path($"photo-{index:D5}.dng")) { MetadataLoaded = true }).ToArray();
        vm.Browse.SetImages(photos);
        vm.Browse.SelectOnly(photos[0]);
        vm.SelectedImage = photos[0];
        var scans = vm.SyncSelectionScanCount;

        for (var step = 1; step <= 200; step++)
        {
            vm.SelectNextImageCommand.Execute(null);
            Assert.Same(photos[step], vm.SelectedImage);
            Assert.Equal(1, vm.SelectedCount);
            Assert.False(vm.SyncSettingsCommand.CanExecute(null));
        }

        Assert.Equal(scans, vm.SyncSelectionScanCount);
    }

    [AvaloniaTheory]
    [InlineData("active")]
    [InlineData("workspace")]
    [InlineData("selection")]
    [InlineData("version")]
    [InlineData("compare")]
    [InlineData("fullscreen")]
    [InlineData("hydration")]
    public async Task ChangedDialogContextWritesNothing(string change)
    {
        await using var fixture = new SyncTransferParityVm();
        var availability = new TestSourceAvailabilityService(SourceAvailability.AvailableLocally);
        await fixture.InitializeAsync(availability: availability);
        var source = await fixture.ImageAsync("source", new() { Exposure = 1 });
        var target = await fixture.ImageAsync("target", new());
        var sibling = new ImageFile(target.FilePath) { Version = 2 };
        var vm = fixture.Vm;
        SelectForSync(vm, source, target);
        var shown = false;
        vm.ShowPasteSettingsAsync = _ =>
        {
            shown = true;

            switch (change)
            {
                case "active":
                    vm.SelectedImage = target;
                    break;

                case "workspace":
                    vm.WorkspaceMode = WorkspaceMode.Export;
                    break;

                case "selection":
                    vm.ToggleImageSelection(target);
                    break;

                case "version":
                    vm.Browse.SetImages([source, sibling]);
                    vm.SelectAllCommand.Execute(null);
                    Assert.Equal(2, vm.SelectedCount);
                    Assert.Same(source, vm.SelectedImage);
                    break;

                case "compare":
                    vm.EnterCompareCommand.Execute(null);
                    break;

                case "fullscreen":
                    vm.IsFullScreenMode = true;
                    break;

                case "hydration":
                    availability.Resolver = _ => SourceAvailability.RequiresHydration;
                    break;
            }

            return Task.FromResult(true);
        };
        await vm.SyncSettingsCommand.ExecuteAsync(null);
        Assert.True(shown);
        Assert.Equal(0, target.EditSettings.Exposure);
        Assert.Equal(0, sibling.EditSettings.Exposure);
        Assert.Empty((await fixture.Catalog.LoadEditHistoryAsync(target.CatalogId)).Entries);
        Assert.Empty((await fixture.Catalog.LoadEditHistoryAsync(source.CatalogId)).Entries);
    }

    private static void SelectForSync(MainWindowViewModel vm, params ImageFile[] photos)
    {
        vm.IsDevelopMode = false;
        vm.Browse.SetImages(photos);
        vm.SelectedImage = photos[0];
        vm.SelectAllCommand.Execute(null);
        Assert.Equal(photos.Length, vm.SelectedCount);
        Assert.True(vm.SyncSettingsCommand.CanExecute(null));
    }

    private static void Claim(MainWindowViewModel vm, ImageFile image) =>
        typeof(MainWindowViewModel).GetMethod("SetDeleteTargetsClaimed", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(vm, [new[] { image.FilePath }, true]);
}
