using System.Diagnostics;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using Xunit;

namespace HappyPhoton.Tests;

public sealed partial class FileOperationsTests : IDisposable
{
    private readonly CatalogVmFixture _fx = new("file-operations");

    [Fact]
    public async Task DeleteSelection_MovesSidecarAndRemovesCatalogCachesAndGridRows()
    {
        using var catalog = await _fx.CreateCatalogAsync("catalog");
        var operations = new TestFileOperationService();
        await using var vm = _fx.CreateViewModel(
            catalog,
            loadMetadataAsync: _ => Task.CompletedTask,
            fileOperationService: operations);
        var first = await CreateCatalogImageAsync(catalog, "first.jpg");
        var second = await CreateCatalogImageAsync(catalog, "second.jpg");
        var sidecar = first.FilePath + ".xmp";
        var shadowedSidecar = Path.ChangeExtension(first.FilePath, ".xmp");
        await File.WriteAllTextAsync(sidecar, "sidecar");
        await File.WriteAllTextAsync(shadowedSidecar, "shadowed");
        File.SetLastWriteTimeUtc(sidecar, DateTime.UtcNow.AddMinutes(1));
        var preview = catalog.GetPreviewPath(first.CatalogId);
        var thumbnail = catalog.GetThumbnailPath(first.CatalogId);
        Directory.CreateDirectory(Path.GetDirectoryName(preview)!);
        Directory.CreateDirectory(Path.GetDirectoryName(thumbnail)!);
        await File.WriteAllBytesAsync(preview, [1]);
        await File.WriteAllBytesAsync(thumbnail, [1]);
        vm.Browse.SetImages([first, second]);
        vm.SelectedImage = first;
        vm.Browse.SelectAllVisible();
        (int count, string? name)? prompt = null;
        vm.ConfirmMoveToTrashAsync = (count, name) =>
        {
            prompt = (count, name);
            return Task.FromResult(true);
        };

        await vm.DeleteImageCommand.ExecuteAsync(null);

        Assert.Equal((2, null), prompt);
        Assert.Equal(
            [first.FilePath, sidecar, shadowedSidecar, second.FilePath],
            operations.MovedPaths);
        Assert.Empty(vm.Browse.AllImages);
        Assert.Null(vm.SelectedImage);
        Assert.False(File.Exists(first.FilePath));
        Assert.False(File.Exists(sidecar));
        Assert.False(File.Exists(shadowedSidecar));
        Assert.False(File.Exists(second.FilePath));
        Assert.False(File.Exists(preview));
        Assert.False(File.Exists(thumbnail));
        Assert.Empty(await catalog.LoadImageStatesAsync(
            [first.FilePath, second.FilePath]));
    }

    [Fact]
    public async Task Delete_RequiresConfirmationAndNamesTheSingleFile()
    {
        using var catalog = await _fx.CreateCatalogAsync("confirm-catalog");
        var operations = new TestFileOperationService();
        await using var vm = _fx.CreateViewModel(
            catalog,
            loadMetadataAsync: _ => Task.CompletedTask,
            fileOperationService: operations);
        var image = await CreateCatalogImageAsync(catalog, "only.jpg");
        vm.Browse.SetImages([image]);
        vm.SelectedImage = image;

        await vm.DeleteImageCommand.ExecuteAsync(null);
        Assert.Empty(operations.MovedPaths);

        (int count, string? name)? prompt = null;
        vm.ConfirmMoveToTrashAsync = (count, name) =>
        {
            prompt = (count, name);
            return Task.FromResult(false);
        };
        await vm.DeleteImageCommand.ExecuteAsync(null);

        Assert.Equal((1, image.FileName), prompt);
        Assert.Empty(operations.MovedPaths);
        Assert.True(File.Exists(image.FilePath));
        Assert.Contains(image, vm.Browse.AllImages);
    }

    [Fact]
    public async Task DeleteVersionTile_DeletesTheFileAndEverySibling()
    {
        using var catalog = await _fx.CreateCatalogAsync("version-file-catalog");
        var operations = new TestFileOperationService();
        await using var vm = _fx.CreateViewModel(
            catalog,
            loadMetadataAsync: _ => Task.CompletedTask,
            fileOperationService: operations);
        var primary = await CreateCatalogImageAsync(catalog, "versioned.jpg");
        var secondState = (await catalog.CreateVersionAsync(primary.CatalogId))!;
        var second = new ImageFile(primary.FilePath)
        {
            CatalogId = secondState.CatalogId,
            Version = secondState.Version,
            VersionCount = 2,
            IsSelected = true
        };
        primary.VersionCount = 2;
        vm.Browse.SetImages([primary, second]);
        vm.SelectedImage = second;
        vm.ConfirmMoveToTrashAsync = (_, _) => Task.FromResult(true);

        await vm.DeleteImageCommand.ExecuteAsync(null);

        Assert.Equal([primary.FilePath], operations.MovedPaths);
        Assert.Empty(vm.Browse.AllImages);
        Assert.Empty(await catalog.LoadImageStatesAsync([primary.FilePath]));
    }

    [Fact]
    public async Task DeleteVersionedFile_SelectsNextNonSiblingTile()
    {
        using var catalog = await _fx.CreateCatalogAsync("selection-catalog");
        var operations = new TestFileOperationService();
        await using var vm = _fx.CreateViewModel(
            catalog,
            loadMetadataAsync: _ => Task.CompletedTask,
            fileOperationService: operations);
        var before = await CreateCatalogImageAsync(catalog, "before.jpg");
        var primary = await CreateCatalogImageAsync(catalog, "versioned-selection.jpg");
        var secondState = (await catalog.CreateVersionAsync(primary.CatalogId))!;
        var second = new ImageFile(primary.FilePath)
        {
            CatalogId = secondState.CatalogId,
            Version = secondState.Version,
            VersionCount = 2,
            IsSelected = true
        };
        var after = await CreateCatalogImageAsync(catalog, "after.jpg");
        primary.VersionCount = 2;
        vm.Browse.SetImages([before, primary, second, after]);
        vm.SelectedImage = second;
        vm.ConfirmMoveToTrashAsync = (_, _) => Task.FromResult(true);

        await vm.DeleteImageCommand.ExecuteAsync(null);

        Assert.Equal([before, after], vm.Browse.AllImages);
        Assert.Same(after, vm.SelectedImage);
    }

    [Fact]
    public async Task DeleteBatch_ContinuesAfterLockedFileAndNamesFailure()
    {
        using var catalog = await _fx.CreateCatalogAsync("failure-catalog");
        var locked = await CreateCatalogImageAsync(catalog, "locked.jpg");
        var moved = await CreateCatalogImageAsync(catalog, "moved.jpg");
        var operations = new TestFileOperationService
        {
            MoveResult = path => path != locked.FilePath
        };
        await using var vm = _fx.CreateViewModel(
            catalog,
            loadMetadataAsync: _ => Task.CompletedTask,
            fileOperationService: operations);
        vm.Browse.SetImages([locked, moved]);
        vm.Browse.SelectAllVisible();
        vm.SelectedImage = locked;
        vm.ConfirmMoveToTrashAsync = (_, _) => Task.FromResult(true);
        IReadOnlyList<FileOperationFailure>? summary = null;
        vm.ShowFileOperationFailuresAsync = failures =>
        {
            summary = failures;
            return Task.CompletedTask;
        };

        await vm.DeleteImageCommand.ExecuteAsync(null);

        Assert.Equal([locked.FilePath, moved.FilePath], operations.MovedPaths);
        Assert.Equal([locked], vm.Browse.AllImages);
        Assert.True(File.Exists(locked.FilePath));
        Assert.False(File.Exists(moved.FilePath));
        var failure = Assert.Single(summary!);
        Assert.Equal(locked.FilePath, failure.Path);
        Assert.Contains("could not be moved", failure.Reason);
    }

    [Fact]
    public async Task Delete_ReportsCatalogCleanupFailureAfterFileWasTrashed()
    {
        using var catalog = await _fx.CreateCatalogAsync("catalog-failure");
        var operations = new TestFileOperationService();
        await using var vm = _fx.CreateViewModel(
            catalog,
            loadMetadataAsync: _ => Task.CompletedTask,
            fileOperationService: operations);
        var image = await CreateCatalogImageAsync(catalog, "catalog-failure.jpg");
        vm.Browse.SetImages([image]);
        vm.SelectedImage = image;
        vm.ConfirmMoveToTrashAsync = (_, _) => Task.FromResult(true);
        IReadOnlyList<FileOperationFailure>? summary = null;
        vm.ShowFileOperationFailuresAsync = failures =>
        {
            summary = failures;
            return Task.CompletedTask;
        };
        catalog.Dispose();

        await vm.DeleteImageCommand.ExecuteAsync(null);

        Assert.Equal([image.FilePath], operations.MovedPaths);
        Assert.Empty(vm.Browse.AllImages);
        var failure = Assert.Single(summary!);
        Assert.Equal(image.FilePath, failure.Path);
        Assert.Contains("moved to Trash", failure.Reason);
        Assert.Contains("catalog entry", failure.Reason);
        Assert.NotEqual(0, image.CatalogId);
    }

    [Fact]
    public async Task DeleteBatch_SkipsOnlineOnlyImageAndSidecarWithoutHydration()
    {
        using var catalog = await _fx.CreateCatalogAsync("cloud-catalog");
        var online = await CreateCatalogImageAsync(catalog, "online.jpg");
        var local = await CreateCatalogImageAsync(catalog, "local.jpg");
        var sidecar = local.FilePath + ".xmp";
        await File.WriteAllTextAsync(sidecar, "sidecar");
        var availability = new TestSourceAvailabilityService(
            SourceAvailability.AvailableLocally)
        {
            Resolver = path => path == online.FilePath || path == sidecar
                ? SourceAvailability.RequiresHydration
                : SourceAvailability.AvailableLocally
        };
        var operations = new TestFileOperationService();
        await using var vm = _fx.CreateViewModel(
            catalog,
            loadMetadataAsync: _ => Task.CompletedTask,
            availabilityService: availability,
            fileOperationService: operations);
        vm.Browse.SetImages([online, local]);
        vm.Browse.SelectAllVisible();
        vm.SelectedImage = online;
        vm.ConfirmMoveToTrashAsync = (_, _) => Task.FromResult(true);
        IReadOnlyList<FileOperationFailure>? summary = null;
        vm.ShowFileOperationFailuresAsync = failures =>
        {
            summary = failures;
            return Task.CompletedTask;
        };

        await vm.DeleteImageCommand.ExecuteAsync(null);

        Assert.Equal([local.FilePath], operations.MovedPaths);
        Assert.Equal([online], vm.Browse.AllImages);
        Assert.True(File.Exists(online.FilePath));
        Assert.True(File.Exists(sidecar));
        Assert.Equal(2, summary!.Count);
        Assert.Contains(summary, failure => failure.Path == online.FilePath &&
            failure.Reason.Contains("online-only"));
        Assert.Contains(summary, failure => failure.Path == sidecar &&
            failure.Reason.Contains("online-only"));
    }

    [Fact]
    public async Task DeleteBatch_RefusesUnsafeVolumesAndNamesEachFile()
    {
        using var catalog = await _fx.CreateCatalogAsync("volume-catalog");
        var network = await CreateCatalogImageAsync(catalog, "network.jpg");
        var removable = await CreateCatalogImageAsync(catalog, "removable.jpg");
        var operations = new TestFileOperationService
        {
            Assessment = path => new TrashPathAssessment(
                false,
                path == network.FilePath
                    ? "Network files cannot be moved to Trash safely."
                    : "Files on removable media cannot be moved to Trash safely.")
        };
        await using var vm = _fx.CreateViewModel(
            catalog,
            loadMetadataAsync: _ => Task.CompletedTask,
            fileOperationService: operations);
        vm.Browse.SetImages([network, removable]);
        vm.Browse.SelectAllVisible();
        vm.ConfirmMoveToTrashAsync = (_, _) => Task.FromResult(true);
        IReadOnlyList<FileOperationFailure>? summary = null;
        vm.ShowFileOperationFailuresAsync = failures =>
        {
            summary = failures;
            return Task.CompletedTask;
        };

        await vm.DeleteImageCommand.ExecuteAsync(null);

        Assert.Empty(operations.MovedPaths);
        Assert.Equal([network, removable], vm.Browse.AllImages);
        Assert.Equal(
            [network.FilePath, removable.FilePath],
            summary!.Select(failure => failure.Path));
    }

    [Fact]
    public async Task CopyPaths_UsesSelectionInGridOrderAndVerbatimPaths()
    {
        using var catalog = await _fx.CreateCatalogAsync("copy-catalog");
        await using var vm = _fx.CreateViewModel(
            catalog,
            loadMetadataAsync: _ => Task.CompletedTask);
        var first = new ImageFile(_fx.Path("first image.jpg"));
        var second = new ImageFile(_fx.Path("second.jpg"));
        var third = new ImageFile(_fx.Path("third.jpg"));
        vm.Browse.SetImages([first, second, third]);
        vm.SelectedImage = second;
        vm.Browse.ToggleSelection(first);
        vm.Browse.ToggleSelection(third);
        string? copied = null;
        vm.CopyToClipboardAsync = text =>
        {
            copied = text;
            return Task.CompletedTask;
        };

        await vm.CopyImagePathsCommand.ExecuteAsync(null);

        Assert.Equal(
            $"{first.FilePath}{Environment.NewLine}{third.FilePath}", copied);

        vm.Browse.DeselectAllVisible();
        await vm.CopyImagePathsCommand.ExecuteAsync(null);
        Assert.Equal(second.FilePath, copied);
    }

    [Fact]
    public async Task DeleteClaim_ExcludesTrashedRowsFromAssessmentTargets()
    {
        using var catalog = await _fx.CreateCatalogAsync("xmp-catalog");
        await catalog.SetAppSettingAsync(
            MainWindowViewModel.XmpSidecarModeKey,
            XmpSidecarMode.ReadWrite.ToString());
        var secondMoveStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var allowSecondMove = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        string? secondPath = null;
        var operations = new TestFileOperationService
        {
            BeforeMoveAsync = async path =>
            {
                if (path != secondPath) return;
                secondMoveStarted.TrySetResult();
                await allowSecondMove.Task;
            }
        };
        var availability = new TestSourceAvailabilityService(
            SourceAvailability.AvailableLocally);
        await using var vm = _fx.CreateViewModel(
            catalog,
            loadMetadataAsync: _ => Task.CompletedTask,
            availabilityService: availability,
            fileOperationService: operations);
        await vm.RestoreXmpSettingsAsync();
        var first = await CreateCatalogImageAsync(catalog, "claimed-first.jpg");
        var second = await CreateCatalogImageAsync(catalog, "claimed-second.jpg");
        var unclaimed = await CreateCatalogImageAsync(catalog, "unclaimed.jpg");
        secondPath = second.FilePath;
        vm.Browse.SetImages([first, second, unclaimed]);
        vm.Browse.ToggleSelection(first);
        vm.Browse.ToggleSelection(second);
        vm.SelectedImage = unclaimed;
        vm.ConfirmMoveToTrashAsync = (_, _) => Task.FromResult(true);

        var deleting = vm.DeleteImageCommand.ExecuteAsync(null);
        await secondMoveStarted.Task.WaitAsync(TestWaits.Condition);
        await vm.SetRatingCommand.ExecuteAsync(4);

        var statesWhileClaimed = await catalog.LoadImageStatesAsync([first.FilePath]);
        var ratingWhileClaimed = first.Rating;
        var pendingWhileClaimed = first.PendingAssessmentAxes;
        var unclaimedRating = unclaimed.Rating;

        allowSecondMove.TrySetResult();
        await deleting;

        Assert.Empty(statesWhileClaimed);
        Assert.Equal(0, ratingWhileClaimed);
        Assert.Equal(AssessmentAxes.None, pendingWhileClaimed);
        Assert.Equal(0, unclaimedRating);
        Assert.False(File.Exists(first.FilePath + ".xmp"));
        Assert.Empty(await catalog.LoadImageStatesAsync(
            [first.FilePath, second.FilePath]));
        Assert.Equal([unclaimed], vm.Browse.AllImages);
    }

    [Fact]
    public async Task Delete_DrainsQueuedXmpWriteBeforeMovingResolvedSidecar()
    {
        using var catalog = await _fx.CreateCatalogAsync("xmp-drain-catalog");
        await catalog.SetAppSettingAsync(
            MainWindowViewModel.XmpSidecarModeKey,
            XmpSidecarMode.ReadWrite.ToString());
        var operations = new TestFileOperationService();
        var availability = new TestSourceAvailabilityService(
            SourceAvailability.AvailableLocally);
        await using var vm = _fx.CreateViewModel(
            catalog,
            loadMetadataAsync: _ => Task.CompletedTask,
            availabilityService: availability,
            fileOperationService: operations);
        await vm.RestoreXmpSettingsAsync();
        var image = await CreateCatalogImageAsync(catalog, "queued.jpg");
        vm.Browse.SetImages([image]);
        vm.SelectedImage = image;
        await vm.SetRatingCommand.ExecuteAsync(3);
        vm.ConfirmMoveToTrashAsync = (_, _) => Task.FromResult(true);

        await vm.DeleteImageCommand.ExecuteAsync(null);

        Assert.Equal(
            [image.FilePath, image.FilePath + ".xmp"],
            operations.MovedPaths);
        Assert.False(File.Exists(image.FilePath + ".xmp"));
    }

    private async Task<ImageFile> CreateCatalogImageAsync(
        CatalogService catalog,
        string name)
    {
        var path = _fx.Path(name);
        await File.WriteAllBytesAsync(path, [1]);
        return new ImageFile(path)
        {
            CatalogId = await catalog.GetOrCreateImageAsync(path)
        };
    }

    public void Dispose() => _fx.Dispose();

    private sealed class TestFileOperationService : IFileOperationService
    {
        internal List<string> MovedPaths { get; } = [];
        internal Func<string, bool> MoveResult { get; init; } = _ => true;
        internal Func<string, TrashPathAssessment> Assessment { get; init; } =
            _ => new TrashPathAssessment(true, null);
        internal Func<string, Task>? BeforeMoveAsync { get; init; }

        public TrashPathAssessment AssessTrashPath(string path) => Assessment(path);

        public async Task<bool> MoveToTrashAsync(string filePath)
        {
            MovedPaths.Add(filePath);
            if (BeforeMoveAsync != null) await BeforeMoveAsync(filePath);
            if (!MoveResult(filePath)) return false;
            File.Delete(filePath);
            return true;
        }

        public Task<bool> RevealFileAsync(string filePath) => Task.FromResult(true);

        public Task<bool> OpenFolderAsync(string folderPath) => Task.FromResult(true);
    }
}
