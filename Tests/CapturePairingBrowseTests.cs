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
            Assert.True(viewModel.ShowCapturePairs);
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
            viewModel.ConfirmMoveToTrashAsync = (_, _) => Task.FromResult(true);
            var jpeg = Find(viewModel, "pair-a.jpg");
            viewModel.Browse.SelectOnly(jpeg);
            viewModel.SelectedImage = jpeg;

            await viewModel.DeleteImageCommand.ExecuteAsync(null);

            var exposedRaw = Find(viewModel, "pair-a.dng");
            Assert.Contains(exposedRaw, viewModel.Browse.VisibleImages);
            Assert.False(exposedRaw.IsRawJpegPair);

            viewModel.ShowCapturePairs = false;
            var raw = Find(viewModel, "pair-b.dng");
            viewModel.Browse.SelectOnly(raw);
            viewModel.SelectedImage = raw;
            await viewModel.DeleteImageCommand.ExecuteAsync(null);
            viewModel.ShowCapturePairs = true;

            var plainJpeg = Find(viewModel, "pair-b.jpg");
            Assert.Contains(plainJpeg, viewModel.Browse.VisibleImages);
            Assert.False(plainJpeg.IsRawJpegPair);
        }
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
        public TrashPathAssessment AssessTrashPath(string path) => new(true, null);

        public Task<bool> MoveToTrashAsync(string filePath)
        {
            File.Delete(filePath);
            return Task.FromResult(true);
        }

        public Task<bool> RevealFileAsync(string filePath) => Task.FromResult(true);
        public Task<bool> OpenFolderAsync(string folderPath) => Task.FromResult(true);
    }
}
