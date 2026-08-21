using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class PasteTargetingTests : IDisposable
{
    private readonly string _root = Directory.CreateDirectory(Path.Combine(
        Path.GetTempPath(),
        $"happy-photon-paste-targets-{Guid.NewGuid():N}")).FullName;

    [Fact]
    public async Task OnePhotoLibrarySelection_UsesBatchConfirmation()
    {
        using var catalog = await CreateCatalogAsync();
        await using var vm = CreateViewModel(catalog);
        var source = await CreateImageAsync(catalog, "source.jpg", exposure: 2);
        var target = await CreateImageAsync(catalog, "target.jpg");
        vm.Library.SetImages([source, target]);
        vm.SelectedImage = source;
        vm.CopyEditSettingsCommand.Execute(null);
        vm.Library.ToggleSelection(target);
        var confirmedCount = 0;
        vm.ConfirmBatchApplyAsync = count =>
        {
            confirmedCount = count;
            return Task.FromResult(true);
        };

        await vm.PasteEditSettingsCommand.ExecuteAsync(null);

        Assert.Equal(1, confirmedCount);
        Assert.Equal(2, target.EditSettings.Exposure);
        Assert.Equal("Applied to 1 image", vm.TransientStatus);
    }

    [Fact]
    public async Task EmptyLibrarySelection_UsesSinglePhotoPathWithoutConfirmation()
    {
        using var catalog = await CreateCatalogAsync();
        await using var vm = CreateViewModel(catalog);
        var source = await CreateImageAsync(catalog, "source.jpg", exposure: 2);
        var target = await CreateImageAsync(catalog, "target.jpg");
        vm.Library.SetImages([source, target]);
        vm.SelectedImage = source;
        vm.CopyEditSettingsCommand.Execute(null);
        vm.SelectedImage = target;
        var confirmations = 0;
        vm.ConfirmBatchApplyAsync = _ =>
        {
            confirmations++;
            return Task.FromResult(true);
        };

        await vm.PasteEditSettingsCommand.ExecuteAsync(null);

        Assert.Equal(0, confirmations);
        Assert.Equal(2, target.EditSettings.Exposure);
        Assert.Equal("Pasted edit settings", vm.TransientStatus);
    }

    [Fact]
    public async Task Develop_PastesOnlyToActivePhoto()
    {
        using var catalog = await CreateCatalogAsync();
        await using var vm = CreateViewModel(catalog);
        var source = await CreateImageAsync(catalog, "source.jpg", exposure: 2);
        var active = await CreateImageAsync(catalog, "active.jpg");
        var selected = await CreateImageAsync(catalog, "selected.jpg");
        vm.Library.SetImages([source, active, selected]);
        vm.SelectedImage = source;
        vm.CopyEditSettingsCommand.Execute(null);
        vm.Library.ToggleSelection(selected);
        selected.SourceRequiresHydration = true;
        vm.SelectedImage = active;
        Assert.False(vm.PasteEditSettingsCommand.CanExecute(null));
        var availabilityChanges = 0;
        vm.PasteEditSettingsCommand.CanExecuteChanged += (_, _) =>
            availabilityChanges++;
        vm.IsDevelopMode = true;
        var confirmations = 0;
        vm.ConfirmBatchApplyAsync = _ =>
        {
            confirmations++;
            return Task.FromResult(true);
        };

        await vm.PasteEditSettingsCommand.ExecuteAsync(null);

        Assert.Equal(0, confirmations);
        Assert.True(availabilityChanges > 0);
        Assert.True(vm.PasteEditSettingsCommand.CanExecute(null));
        Assert.Equal(2, active.EditSettings.Exposure);
        Assert.Equal(0, selected.EditSettings.Exposure);
    }

    [Fact]
    public async Task LocalLibrarySelection_IsNotVetoedByOutsideCloudActivePhoto()
    {
        using var catalog = await CreateCatalogAsync();
        var availability = new TestSourceAvailabilityService(
            SourceAvailability.AvailableLocally);
        await using var vm = CreateViewModel(catalog, availability);
        var source = await CreateImageAsync(catalog, "source.jpg", exposure: 2);
        var target = await CreateImageAsync(catalog, "target.jpg");
        var cloud = await CreateImageAsync(catalog, "cloud.jpg");
        availability.Resolver = path => path == cloud.FilePath
            ? SourceAvailability.RequiresHydration
            : SourceAvailability.AvailableLocally;
        vm.Library.SetImages([source, target, cloud]);
        vm.SelectedImage = source;
        vm.CopyEditSettingsCommand.Execute(null);
        vm.Library.ToggleSelection(target);
        vm.SelectedImage = cloud;
        var confirmedCount = 0;
        vm.ConfirmBatchApplyAsync = count =>
        {
            confirmedCount = count;
            return Task.FromResult(true);
        };

        Assert.True(cloud.SourceRequiresHydration);
        Assert.True(vm.PasteEditSettingsCommand.CanExecute(null));
        await vm.PasteEditSettingsCommand.ExecuteAsync(null);

        Assert.Equal(1, confirmedCount);
        Assert.Equal(2, target.EditSettings.Exposure);
        Assert.Equal(0, cloud.EditSettings.Exposure);
    }

    private async Task<CatalogService> CreateCatalogAsync()
    {
        var catalog = new CatalogService(Path.Combine(_root, "catalog"));
        await catalog.InitializeAsync();
        return catalog;
    }

    private static MainWindowViewModel CreateViewModel(
        CatalogService catalog,
        ISourceAvailabilityService? availability = null) =>
        new(
            catalog,
            new NullBaseLoader(),
            _ => Task.CompletedTask,
            availability ?? new TestSourceAvailabilityService(
                SourceAvailability.AvailableLocally));

    private async Task<ImageFile> CreateImageAsync(
        CatalogService catalog,
        string name,
        double exposure = 0)
    {
        var image = new ImageFile(Path.Combine(_root, name))
        {
            EditSettings = new EditSettings { Exposure = exposure }
        };
        image.CatalogId = await catalog.GetOrCreateImageAsync(image.FilePath);
        await catalog.SaveEditSettingsAsync(image.CatalogId, image.EditSettings);
        return image;
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.Delete(_root, recursive: true);
    }
}
