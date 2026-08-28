using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

public sealed partial class FileOperationsTests
{
    [Fact]
    public async Task DeleteVersionSelection_LeavesOriginalAndSiblingVersions()
    {
        using var catalog = await _fx.CreateCatalogAsync("version-row-catalog");
        var operations = new TestFileOperationService();
        await using var vm = _fx.CreateViewModel(
            catalog,
            loadMetadataAsync: _ => Task.CompletedTask,
            fileOperationService: operations);
        var primary = await CreateCatalogImageAsync(catalog, "versioned.jpg");
        var second = VersionImage(
            primary,
            (await catalog.CreateVersionAsync(primary.CatalogId))!,
            selected: true);
        var third = VersionImage(
            primary,
            (await catalog.CreateVersionAsync(primary.CatalogId))!);
        primary.VersionCount = second.VersionCount = third.VersionCount = 3;
        vm.Browse.SetImages([primary, second, third]);
        vm.SelectedImage = second;
        DeleteConfirmationRequest? prompt = null;
        vm.ConfirmDeleteAsync = request =>
        {
            prompt = request;
            return Task.FromResult(true);
        };

        await vm.DeleteImageCommand.ExecuteAsync(null);

        Assert.Same(second, Assert.Single(prompt!.Versions));
        Assert.Empty(prompt.Primaries);
        Assert.Empty(operations.MovedPaths);
        Assert.True(File.Exists(primary.FilePath));
        Assert.Equal([primary, third], vm.Browse.AllImages);
        Assert.Same(third, vm.SelectedImage);
        Assert.All(vm.Browse.AllImages, image => Assert.Equal(2, image.VersionCount));
        var states = await catalog.LoadImageStatesAsync([primary.FilePath]);
        Assert.Equal(
            new[] { 1, 3 },
            states[primary.FilePath].Select(state => state.Version).ToArray());
    }

    [Fact]
    public async Task DeleteRejectedVersion_LeavesOriginalAndPrimaryVersion()
    {
        using var catalog = await _fx.CreateCatalogAsync("rejected-version-catalog");
        var operations = new TestFileOperationService();
        await using var vm = _fx.CreateViewModel(
            catalog,
            loadMetadataAsync: _ => Task.CompletedTask,
            fileOperationService: operations);
        var primary = await CreateCatalogImageAsync(catalog, "rejected-version.jpg");
        var version = VersionImage(
            primary,
            (await catalog.CreateVersionAsync(primary.CatalogId))!);
        version.Flag = ImageFlag.Rejected;
        primary.VersionCount = version.VersionCount = 2;
        vm.Browse.SetImages([primary, version]);
        vm.SelectedImage = version;
        (int Versions, int Primaries)? prompt = null;
        vm.ConfirmDeleteRejectedAsync = (versions, primaries, _) =>
        {
            prompt = (versions, primaries);
            return Task.FromResult(true);
        };

        await vm.DeleteRejectedImagesCommand.ExecuteAsync(null);

        Assert.Equal((1, 0), prompt);
        Assert.Empty(operations.MovedPaths);
        Assert.True(File.Exists(primary.FilePath));
        Assert.Equal([primary], vm.Browse.AllImages);
        var states = await catalog.LoadImageStatesAsync([primary.FilePath]);
        Assert.Equal([1], states[primary.FilePath].Select(state => state.Version));
    }

    [Fact]
    public async Task DeletePrimarySelection_TrashesFileAndAllVersionRows()
    {
        using var catalog = await _fx.CreateCatalogAsync("primary-file-catalog");
        var operations = new TestFileOperationService();
        await using var vm = _fx.CreateViewModel(
            catalog,
            loadMetadataAsync: _ => Task.CompletedTask,
            fileOperationService: operations);
        var before = await CreateCatalogImageAsync(catalog, "before.jpg");
        var primary = await CreateCatalogImageAsync(catalog, "versioned-primary.jpg");
        var second = VersionImage(
            primary,
            (await catalog.CreateVersionAsync(primary.CatalogId))!);
        var after = await CreateCatalogImageAsync(catalog, "after.jpg");
        primary.VersionCount = second.VersionCount = 2;
        primary.IsSelected = true;
        vm.Browse.SetImages([before, primary, second, after]);
        vm.SelectedImage = primary;
        vm.ConfirmDeleteAsync = _ => Task.FromResult(true);

        await vm.DeleteImageCommand.ExecuteAsync(null);

        Assert.Equal([primary.FilePath], operations.MovedPaths);
        Assert.Equal([before, after], vm.Browse.AllImages);
        Assert.Contains(vm.SelectedImage!, vm.Browse.AllImages);
        Assert.Empty(await catalog.LoadImageStatesAsync([primary.FilePath]));
    }

    [Fact]
    public async Task DeletePrimaryAndItsVersion_ConfirmsOneImageOnly()
    {
        using var catalog = await _fx.CreateCatalogAsync("same-file-selection-catalog");
        var operations = new TestFileOperationService();
        await using var vm = _fx.CreateViewModel(
            catalog,
            loadMetadataAsync: _ => Task.CompletedTask,
            fileOperationService: operations);
        var primary = await CreateCatalogImageAsync(catalog, "same-file.jpg");
        var version = VersionImage(
            primary,
            (await catalog.CreateVersionAsync(primary.CatalogId))!,
            selected: true);
        primary.IsSelected = true;
        primary.VersionCount = version.VersionCount = 2;
        vm.Browse.SetImages([primary, version]);
        vm.SelectedImage = version;
        DeleteConfirmationRequest? prompt = null;
        vm.ConfirmDeleteAsync = request =>
        {
            prompt = request;
            return Task.FromResult(true);
        };

        await vm.DeleteImageCommand.ExecuteAsync(null);

        Assert.Empty(prompt!.Versions);
        Assert.Same(primary, Assert.Single(prompt.Primaries));
        Assert.DoesNotContain(
            "not affected",
            MainWindow.DeleteConfirmationContent(prompt).Message,
            StringComparison.OrdinalIgnoreCase);
        Assert.Equal([primary.FilePath], operations.MovedPaths);
        Assert.Empty(vm.Browse.AllImages);
        Assert.Empty(await catalog.LoadImageStatesAsync([primary.FilePath]));
    }

    [Fact]
    public async Task DeleteRejectedPrimaryAndItsVersion_ConfirmsOneImageOnly()
    {
        using var catalog = await _fx.CreateCatalogAsync("same-file-rejected-catalog");
        var operations = new TestFileOperationService();
        await using var vm = _fx.CreateViewModel(
            catalog,
            loadMetadataAsync: _ => Task.CompletedTask,
            fileOperationService: operations);
        var primary = await CreateCatalogImageAsync(catalog, "same-rejected-file.jpg");
        var version = VersionImage(
            primary,
            (await catalog.CreateVersionAsync(primary.CatalogId))!);
        primary.Flag = version.Flag = ImageFlag.Rejected;
        primary.VersionCount = version.VersionCount = 2;
        vm.Browse.SetImages([primary, version]);
        vm.SelectedImage = version;
        (int Versions, int Primaries)? prompt = null;
        vm.ConfirmDeleteRejectedAsync = (versions, primaries, _) =>
        {
            prompt = (versions, primaries);
            return Task.FromResult(true);
        };

        await vm.DeleteRejectedImagesCommand.ExecuteAsync(null);

        Assert.Equal((0, 1), prompt);
        Assert.NotNull(prompt);
        var content = MainWindow.DeleteRejectedConfirmationContent(
            prompt.Value.Versions,
            prompt.Value.Primaries,
            vm.CurrentFolderPath);
        Assert.DoesNotContain(
            "not affected", content.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal([primary.FilePath], operations.MovedPaths);
        Assert.Empty(vm.Browse.AllImages);
        Assert.Empty(await catalog.LoadImageStatesAsync([primary.FilePath]));
    }

    [Fact]
    public async Task DeleteMixedSelection_ConfirmsOnceAndNamesBothOperations()
    {
        using var catalog = await _fx.CreateCatalogAsync("mixed-delete-catalog");
        var operations = new TestFileOperationService();
        await using var vm = _fx.CreateViewModel(
            catalog,
            loadMetadataAsync: _ => Task.CompletedTask,
            fileOperationService: operations);
        var versionPrimary = await CreateCatalogImageAsync(catalog, "keep-original.jpg");
        var version = VersionImage(
            versionPrimary,
            (await catalog.CreateVersionAsync(versionPrimary.CatalogId))!,
            selected: true);
        versionPrimary.VersionCount = version.VersionCount = 2;
        var trashPrimary = await CreateCatalogImageAsync(catalog, "trash-original.jpg");
        trashPrimary.IsSelected = true;
        vm.Browse.SetImages([versionPrimary, version, trashPrimary]);
        vm.SelectedImage = version;
        var confirmations = 0;
        DeleteConfirmationRequest? prompt = null;
        vm.ConfirmDeleteAsync = request =>
        {
            confirmations++;
            prompt = request;
            return Task.FromResult(true);
        };

        await vm.DeleteImageCommand.ExecuteAsync(null);

        Assert.Equal(1, confirmations);
        Assert.Single(prompt!.Versions);
        Assert.Single(prompt.Primaries);
        Assert.Equal([trashPrimary.FilePath], operations.MovedPaths);
        Assert.True(File.Exists(versionPrimary.FilePath));
        Assert.False(File.Exists(trashPrimary.FilePath));
        Assert.Equal([versionPrimary], vm.Browse.AllImages);
        var content = MainWindow.DeleteConfirmationContent(prompt);
        Assert.Contains("1 version", content.Message);
        Assert.Contains("1 image", content.Message);
        Assert.Contains("Trash", content.Message);
    }

    [Fact]
    public async Task DeleteThreeVersions_RecomputesCapturePairsOnce()
    {
        using var catalog = await _fx.CreateCatalogAsync("version-batch-catalog");
        await using var vm = _fx.CreateViewModel(
            catalog,
            loadMetadataAsync: _ => Task.CompletedTask);
        var primary = await CreateCatalogImageAsync(catalog, "many-versions.jpg");
        var versions = new List<ImageFile>();
        for (var index = 0; index < 3; index++)
        {
            versions.Add(VersionImage(
                primary,
                (await catalog.CreateVersionAsync(primary.CatalogId))!,
                selected: true));
        }
        primary.VersionCount = 4;
        foreach (var version in versions) version.VersionCount = 4;
        vm.Browse.SetImages([primary, .. versions]);
        vm.SelectedImage = versions[0];
        vm.ConfirmDeleteAsync = _ => Task.FromResult(true);
        var recomputes = 0;
        vm.Browse.FilterChanged += (_, _) => recomputes++;

        await vm.DeleteImageCommand.ExecuteAsync(null);

        Assert.Equal(1, recomputes);
        Assert.Equal([primary], vm.Browse.AllImages);
        Assert.Equal(1, primary.VersionCount);
    }

    [Fact]
    public async Task DeleteVersionBatch_ContinuesAndReconcilesAfterFailure()
    {
        using var catalog = await _fx.CreateCatalogAsync("version-failure-catalog");
        var primary = await CreateCatalogImageAsync(catalog, "version-failure.jpg");
        var versions = new List<ImageFile>();
        for (var index = 0; index < 3; index++)
        {
            versions.Add(VersionImage(
                primary,
                (await catalog.CreateVersionAsync(primary.CatalogId))!,
                selected: true));
        }
        primary.VersionCount = 4;
        foreach (var version in versions) version.VersionCount = 4;
        var attempted = new List<long>();
        async Task<bool> DeleteCatalogVersionAsync(long catalogId)
        {
            attempted.Add(catalogId);
            if (catalogId == versions[1].CatalogId)
                throw new IOException("The cache asset is locked.");
            return await catalog.DeleteVersionAsync(catalogId);
        }

        await using var vm = _fx.CreateViewModel(
            catalog,
            loadMetadataAsync: _ => Task.CompletedTask,
            deleteCatalogVersionAsync: DeleteCatalogVersionAsync);
        vm.Browse.SetImages([primary, .. versions]);
        vm.SelectedImage = versions[0];
        vm.ConfirmDeleteAsync = _ => Task.FromResult(true);
        IReadOnlyList<FileOperationFailure>? summary = null;
        vm.ShowFileOperationFailuresAsync = failures =>
        {
            summary = failures;
            return Task.CompletedTask;
        };
        var recomputes = 0;
        vm.Browse.FilterChanged += (_, _) => recomputes++;

        await vm.DeleteImageCommand.ExecuteAsync(null);

        Assert.Equal(versions.Select(image => image.CatalogId), attempted);
        Assert.Equal([primary, versions[1]], vm.Browse.AllImages);
        Assert.Equal(1, recomputes);
        Assert.All(vm.Browse.AllImages, image => Assert.Equal(2, image.VersionCount));
        var failure = Assert.Single(summary!);
        Assert.Equal(primary.FilePath, failure.Path);
        Assert.Contains("V3", failure.Reason);
        Assert.Contains("locked", failure.Reason);
        var states = await catalog.LoadImageStatesAsync([primary.FilePath]);
        Assert.Equal(
            new[] { 1, 3 },
            states[primary.FilePath].Select(state => state.Version).ToArray());
    }

    [Fact]
    public void DeleteConfirmationContent_CoversVersionPrimaryAndMixedModes()
    {
        var primary = new ImageFile("photo.jpg");
        var version = new ImageFile("photo.jpg")
        {
            Version = 2,
            VersionLabel = "Warm"
        };

        var versionOnly = MainWindow.DeleteConfirmationContent(
            new DeleteConfirmationRequest([version], []));
        Assert.Equal("Delete Version", versionOnly.Title);
        Assert.Equal(
            "Delete version \"V2 · Warm\" of photo.jpg? " +
            "The original file is not affected.",
            versionOnly.Message);

        var primaryOnly = MainWindow.DeleteConfirmationContent(
            new DeleteConfirmationRequest([], [primary]));
        Assert.Equal("Move to Trash", primaryOnly.Title);
        Assert.Equal("Move \"photo.jpg\" to Trash?", primaryOnly.Message);

        var mixed = MainWindow.DeleteConfirmationContent(
            new DeleteConfirmationRequest([version, version], [primary]));
        Assert.Contains("Delete 2 versions", mixed.Message);
        Assert.Contains("move 1 image to Trash", mixed.Message);
    }

    [Fact]
    public void DeleteRejectedConfirmationContent_NamesVersionAndOriginalOutcomes()
    {
        var versionOnly = MainWindow.DeleteRejectedConfirmationContent(
            1, 0, "C:\\Photos");
        Assert.Contains("1 rejected version", versionOnly.Message);
        Assert.Contains("C:\\Photos", versionOnly.Message);
        Assert.Contains("original file is not affected", versionOnly.Message);

        var primaryOnly = MainWindow.DeleteRejectedConfirmationContent(
            0, 1, "C:\\Photos");
        Assert.Equal(
            "Move 1 rejected image from \"C:\\Photos\" to Trash?",
            primaryOnly.Message);

        var mixed = MainWindow.DeleteRejectedConfirmationContent(
            2, 1, "C:\\Photos");
        Assert.Contains("Delete 2 rejected versions", mixed.Message);
        Assert.Contains("move 1 rejected image", mixed.Message);
        Assert.Contains("original files for the deleted versions are not affected",
            mixed.Message);
    }

    private static ImageFile VersionImage(
        ImageFile primary,
        CatalogImageState state,
        bool selected = false) => new(primary.FilePath)
        {
            CatalogId = state.CatalogId,
            Version = state.Version,
            IsSelected = selected
        };
}
