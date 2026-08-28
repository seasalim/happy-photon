using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class CapturePairingBrowseTests : IDisposable
{
    private readonly CatalogVmFixture _fixture = new("capture-pairing");

    [Fact]
    public async Task ToggleFiltersAndVersions_FollowCaptureVisibility()
    {
        var (catalog, viewModel, _) = await CreateContextAsync();
        using (catalog)
        await using (viewModel)
        {
            Assert.False(viewModel.ShowCapturePairs);
            AssertPairState(viewModel, visible: 8, pairChips: 0);
            viewModel.ShowCapturePairs = true;
            AssertPairState(viewModel, visible: 5, pairChips: 3);
            Assert.Equal("5 photos", viewModel.Browse.PhotoCountText);
            AssertFilterCount(viewModel, ImageFileTypeFilter.Raw, 4);
            AssertFilterCount(viewModel, ImageFileTypeFilter.Jpeg, 4);

            viewModel.Browse.FileTypeFilter = ImageFileTypeFilter.All;
            viewModel.ShowBurstGroups = true;
            await viewModel.WaitForBurstAnalysisAsync();
            var bursts = viewModel.Browse.AllImages.ToDictionary(
                image => image.FilePath,
                image => (image.BurstIndex, image.BurstSize));

            viewModel.ShowCapturePairs = false;
            AssertPairState(viewModel, visible: 8, pairChips: 0);
            AssertFilterCount(viewModel, ImageFileTypeFilter.Raw, 4);
            AssertFilterCount(viewModel, ImageFileTypeFilter.Jpeg, 4);
            Assert.All(viewModel.Browse.AllImages, image =>
                Assert.Equal(bursts[image.FilePath],
                    (image.BurstIndex, image.BurstSize)));

            viewModel.Browse.FileTypeFilter = ImageFileTypeFilter.All;
            viewModel.ShowCapturePairs = true;
            var jpeg = Find(viewModel, "pair-a.jpg");
            viewModel.Browse.SelectOnly(jpeg);
            viewModel.SelectedImage = jpeg;
            await viewModel.NewVersionFromCurrentCommand.ExecuteAsync(null);
            AssertPairState(viewModel, visible: 6, pairChips: 4);

            viewModel.ShowCapturePairs = false;
            var raw = Find(viewModel, "pair-b.dng");
            viewModel.Browse.SelectOnly(raw);
            viewModel.SelectedImage = raw;
            await viewModel.NewVersionFromCurrentCommand.ExecuteAsync(null);
            AssertPairState(viewModel, visible: 10, pairChips: 0);
            AssertFilterCount(viewModel, ImageFileTypeFilter.Raw, 5);
            AssertFilterCount(viewModel, ImageFileTypeFilter.Jpeg, 5);

            viewModel.Browse.FileTypeFilter = ImageFileTypeFilter.All;
            viewModel.ShowCapturePairs = true;
            AssertPairState(viewModel, visible: 6, pairChips: 4);
            AssertFilterCount(viewModel, ImageFileTypeFilter.Raw, 5);
            AssertFilterCount(viewModel, ImageFileTypeFilter.Jpeg, 5);
            Assert.DoesNotContain(viewModel.Browse.VisibleImages,
                image => image.FileName == "pair-b.dng" && image.Version == 2);
        }
    }

    [Fact]
    public async Task EnablingPairs_MapsActiveAndSelectedRawsToJpegs()
    {
        var (catalog, viewModel, _) = await CreateContextAsync();
        using (catalog)
        await using (viewModel)
        {
            viewModel.ShowCapturePairs = false;
            var firstRaw = Find(viewModel, "pair-a.dng");
            var secondRaw = Find(viewModel, "pair-b.dng");
            firstRaw.IsSelected = true;
            secondRaw.IsSelected = true;
            viewModel.SelectedImage = secondRaw;
            viewModel.RefreshSelectedCount();

            viewModel.ShowCapturePairs = true;

            Assert.Equal("pair-b.jpg", viewModel.SelectedImage?.FileName);
            Assert.Equal(
                ["pair-a.jpg", "pair-b.jpg"],
                viewModel.GetSelectedImages().Select(image => image.FileName)
                    .Order(StringComparer.Ordinal).ToArray());
            Assert.DoesNotContain(viewModel.Browse.VisibleImages, image => image.IsRaw &&
                image.FileName.StartsWith("pair-", StringComparison.Ordinal));

            viewModel.SelectedImage = viewModel.Browse.FirstVisible();
            viewModel.SelectNextImageCommand.Execute(null);
            Assert.Contains(viewModel.SelectedImage!, viewModel.Browse.VisibleImages);
            Assert.False(viewModel.SelectedImage!.IsRaw &&
                viewModel.SelectedImage.FileName.StartsWith("pair-", StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task DeletingEitherMember_RecomputesPairingImmediately()
    {
        var operations = new SuccessfulTrashService();
        var (catalog, viewModel, _) = await CreateContextAsync(operations);
        using (catalog)
        await using (viewModel)
        {
            viewModel.ConfirmDeleteAsync = _ => Task.FromResult(true);
            viewModel.ShowCapturePairs = true;
            var jpeg = Find(viewModel, "pair-a.jpg");
            Assert.True(jpeg.IsRawJpegPair);
            viewModel.Browse.SelectOnly(jpeg);
            viewModel.SelectedImage = jpeg;

            await viewModel.DeleteImageCommand.ExecuteAsync(null);

            var exposedRaw = Find(viewModel, "pair-a.dng");
            Assert.Contains(jpeg.FilePath, operations.MovedPaths);
            Assert.Contains(exposedRaw, viewModel.Browse.VisibleImages);
            Assert.False(exposedRaw.IsRawJpegPair);

            viewModel.ShowCapturePairs = false;
            var raw = Find(viewModel, "pair-b.dng");
            viewModel.Browse.SelectOnly(raw);
            viewModel.SelectedImage = raw;
            await viewModel.DeleteImageCommand.ExecuteAsync(null);
            viewModel.ShowCapturePairs = true;

            var plainJpeg = Find(viewModel, "pair-b.jpg");
            Assert.Contains(raw.FilePath, operations.MovedPaths);
            Assert.Contains(plainJpeg, viewModel.Browse.VisibleImages);
            Assert.False(plainJpeg.IsRawJpegPair);
        }
    }

    [Fact]
    public async Task PairPreference_RoundTripsAndAppliesBeforeFolderLoad()
    {
        var folder = _fixture.Path($"persisted-pairs-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        foreach (var name in new[] { "pair.jpg", "pair.dng", "single.jpg" })
        {
            TestImages.WriteJpeg(Path.Combine(folder, name));
        }

        using var catalog = await _fixture.CreateUniqueCatalogAsync();
        var settingsService = new AppSettingsService(catalog);
        await using (var savingViewModel = _fixture.CreateViewModel(
                         catalog,
                         loadMetadataAsync: _ => Task.CompletedTask,
                         postSelection: action => action()))
        {
            var restoreWrites = 0;
            savingViewModel.PersistAppSettingsAsync = () =>
            {
                restoreWrites++;
                return Task.CompletedTask;
            };
            savingViewModel.RestoreShowCapturePairs(true);
            savingViewModel.RestoreShowCapturePairs(false);
            Assert.Equal(0, restoreWrites);

            var saved = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            savingViewModel.PersistAppSettingsAsync = async () =>
            {
                await settingsService.SavePreferencesAsync(new AppSettings
                {
                    ShowCapturePairs = savingViewModel.ShowCapturePairs
                });
                saved.TrySetResult();
            };
            savingViewModel.ShowCapturePairs = true;
            await saved.Task.WaitAsync(TestWaits.Condition);
        }

        var restoredOn = await settingsService.LoadAsync();
        Assert.True(restoredOn.ShowCapturePairs);
        await using (var restoredOnViewModel = _fixture.CreateViewModel(
                         catalog,
                         loadMetadataAsync: _ => Task.CompletedTask,
                         postSelection: action => action()))
        {
            restoredOnViewModel.RestoreShowCapturePairs(restoredOn.ShowCapturePairs);
            await restoredOnViewModel.LoadFolderAsync(folder);
            AssertPairState(restoredOnViewModel, visible: 2, pairChips: 1);

            var saved = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            restoredOnViewModel.PersistAppSettingsAsync = async () =>
            {
                await settingsService.SavePreferencesAsync(new AppSettings
                {
                    ShowCapturePairs = restoredOnViewModel.ShowCapturePairs
                });
                saved.TrySetResult();
            };
            restoredOnViewModel.ShowCapturePairs = false;
            await saved.Task.WaitAsync(TestWaits.Condition);
        }

        var restoredOff = await settingsService.LoadAsync();
        Assert.False(restoredOff.ShowCapturePairs);
        await using var restoredOffViewModel = _fixture.CreateViewModel(
            catalog,
            loadMetadataAsync: _ => Task.CompletedTask,
            postSelection: action => action());
        restoredOffViewModel.RestoreShowCapturePairs(restoredOff.ShowCapturePairs);
        await restoredOffViewModel.LoadFolderAsync(folder);
        AssertPairState(restoredOffViewModel, visible: 3, pairChips: 0);
    }

    private async Task<(CatalogService Catalog, MainWindowViewModel ViewModel, string Folder)>
        CreateContextAsync(IFileOperationService? fileOperations = null)
    {
        var folder = _fixture.Path(Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        foreach (var name in new[]
                 {
                     "pair-a.jpg", "pair-a.dng",
                     "pair-b.jpg", "pair-b.dng",
                     "pair-c.jpg", "pair-c.dng",
                     "raw-only.dng", "jpeg-only.jpg"
                 })
            TestImages.WriteJpeg(Path.Combine(folder, name));

        var catalog = await _fixture.CreateUniqueCatalogAsync();
        var viewModel = _fixture.CreateViewModel(
            catalog,
            loadMetadataAsync: image =>
            {
                image.ApplyMetadata(new ImageMetadata
                {
                    DateTaken = new DateTime(2026, 8, 27, 12, 0, 0)
                });
                return Task.CompletedTask;
            },
            availabilityService: new TestSourceAvailabilityService(
                SourceAvailability.AvailableLocally),
            postSelection: action => action(),
            fileOperationService: fileOperations);
        await viewModel.LoadFolderAsync(folder);
        return (catalog, viewModel, folder);
    }

    private static ImageFile Find(MainWindowViewModel viewModel, string name) =>
        viewModel.Browse.AllImages.First(image => image.FileName == name);

    private static void AssertPairState(
        MainWindowViewModel viewModel,
        int visible,
        int pairChips)
    {
        viewModel.Browse.FileTypeFilter = ImageFileTypeFilter.All;
        Assert.Equal(visible, viewModel.Browse.VisibleCount);
        Assert.Equal(visible, viewModel.Browse.TotalCount);
        Assert.Equal(pairChips,
            viewModel.Browse.VisibleImages.Count(image => image.IsRawJpegPair));
    }

    private static void AssertFilterCount(
        MainWindowViewModel viewModel,
        ImageFileTypeFilter filter,
        int expected)
    {
        viewModel.Browse.FileTypeFilter = filter;
        Assert.Equal(expected, viewModel.Browse.VisibleCount);
    }

    public void Dispose() => _fixture.Dispose();

    private sealed class SuccessfulTrashService : IFileOperationService
    {
        internal List<string> MovedPaths { get; } = [];

        public TrashPathAssessment AssessTrashPath(string path) => new(true, null);

        public Task<bool> MoveToTrashAsync(string filePath)
        {
            // Physical deletion races the thumbnail reader under assembly load,
            // turning this successful fake into a failed file operation.
            MovedPaths.Add(filePath);
            return Task.FromResult(true);
        }

        public Task<bool> RevealFileAsync(string filePath) => Task.FromResult(true);
        public Task<bool> OpenFolderAsync(string folderPath) => Task.FromResult(true);
    }
}
