using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class ManualFolderRefreshTests
{
    [Fact]
    public void NoCurrentFolderOrDummyFolder_IsNoOp()
    {
        using var context = RefreshTestContext.Create();

        Assert.Equal(0, context.Refresh());

        context.ViewModel.SelectedFolder = FolderNode.CreateDummy();

        Assert.Equal(0, context.Refresh());
        Assert.Empty(context.ViewModel.Library.AllImages);
    }

    [Fact]
    public void Refresh_ReconcilesSupportedFilesAndRemovedSelection()
    {
        using var context = RefreshTestContext.Create();
        context.AddFile("alpha.jpg");
        var removed = context.AddFile("removed.jpg");
        context.LoadFolder();
        context.ViewModel.SelectedImage = context.ViewModel.Library.AllImages
            .Single(image => image.FilePath == removed);

        File.Delete(removed);
        var added = context.AddFile("added.jpeg");
        context.AddFile("notes.txt");

        var generation = context.Refresh();

        Assert.True(generation > 0);
        Assert.Equal(2, context.ViewModel.Library.TotalCount);
        Assert.Contains(
            context.ViewModel.Library.AllImages,
            image => image.FilePath == added);
        Assert.DoesNotContain(
            context.ViewModel.Library.AllImages,
            image => image.FilePath == removed);
        Assert.Equal(added, context.ViewModel.SelectedImage?.FilePath);
        Assert.Equal("Refreshed — 2 photos.", context.ViewModel.TransientStatus);
    }

    [Fact]
    public void Refresh_AddsNewSubfolderToSelectedTreeNode()
    {
        using var context = RefreshTestContext.Create();
        context.LoadFolder();
        var selectedFolder = Assert.IsType<FolderNode>(
            context.ViewModel.SelectedFolder);
        Assert.Empty(selectedFolder.Children);

        var exportPath = Directory.CreateDirectory(
            Path.Combine(context.PhotoDirectory, "export")).FullName;

        context.Refresh();

        var exportNode = Assert.Single(selectedFolder.Children);
        Assert.Equal(exportPath, exportNode.Path);
        Assert.Equal("export", exportNode.Name);
        Assert.True(context.ViewModel.CurrentFolderHasSubfolders);
        Assert.Same(selectedFolder, context.ViewModel.SelectedFolder);
    }

    [Fact]
    public void Refresh_ReselectsUsingPlatformPathComparison()
    {
        using var context = RefreshTestContext.Create();
        var first = context.AddFile("alpha.jpg");
        var second = context.AddFile("beta.jpg");
        context.LoadFolder();
        context.ViewModel.SelectedImage = new ImageFile(second.ToUpperInvariant());

        context.Refresh();

        Assert.Equal(
            OperatingSystem.IsWindows() ? second : first,
            context.ViewModel.SelectedImage?.FilePath);
    }

    [Fact]
    public void MissingFolder_PreservesLibraryAndReportsSkippedStatus()
    {
        using var context = RefreshTestContext.Create();
        var imagePath = context.AddFile("still-listed.jpg");
        context.LoadFolder();
        Directory.Delete(context.PhotoDirectory, recursive: true);

        var generation = context.Refresh();

        Assert.Equal(0, generation);
        Assert.Single(context.ViewModel.Library.AllImages);
        Assert.Equal(
            imagePath,
            context.ViewModel.Library.AllImages[0].FilePath);
        Assert.Equal(
            "Refresh skipped — the folder is no longer available.",
            context.ViewModel.TransientStatus);
    }

    [Fact]
    public void Refresh_PreservesCatalogStateFiltersAndFilteredCount()
    {
        using var context = RefreshTestContext.Create();
        context.AddFile("capture.nef");
        var keeperPath = context.AddFile("keeper.jpg");
        context.AddFile("ignored.txt");
        context.LoadFolder();
        var keeper = context.ViewModel.Library.AllImages.Single(
            image => image.FilePath == keeperPath);
        var edits = new EditSettings { Exposure = 1.25, Contrast = 18 };
        Complete(context.Catalog.SaveEditSettingsAsync(keeper.CatalogId, edits));
        Complete(context.Catalog.SaveFlagStateAsync(
            keeper.CatalogId,
            ImageFlag.Picked));
        Complete(context.Catalog.SaveRatingAsync(keeper.CatalogId, 4));
        keeper.EditSettings = edits;
        keeper.HasEdits = true;
        keeper.Flag = ImageFlag.Picked;
        keeper.Rating = 4;
        context.ViewModel.Library.FileTypeFilter = ImageFileTypeFilter.Jpeg;
        context.ViewModel.Library.FlagFilter = FlagFilter.Picked;
        context.ViewModel.Library.MinimumRating = 4;
        context.ViewModel.SelectedImage = keeper;

        context.Refresh();

        var refreshed = Assert.Single(context.ViewModel.Library.VisibleImages);
        Assert.Equal(keeperPath, refreshed.FilePath);
        Assert.Equal(1.25, refreshed.EditSettings.Exposure);
        Assert.Equal(18, refreshed.EditSettings.Contrast);
        Assert.True(refreshed.HasEdits);
        Assert.Equal(ImageFlag.Picked, refreshed.Flag);
        Assert.Equal(4, refreshed.Rating);
        Assert.Same(refreshed, context.ViewModel.SelectedImage);
        Assert.Equal(ImageFileTypeFilter.Jpeg, context.ViewModel.Library.FileTypeFilter);
        Assert.Equal(FlagFilter.Picked, context.ViewModel.Library.FlagFilter);
        Assert.Equal(4, context.ViewModel.Library.MinimumRating);
        Assert.Equal(
            "Refreshed — 1 of 2 photos.",
            context.ViewModel.TransientStatus);
    }

    [Fact]
    public void LoadFolder_ReturnsPublishedGenerationAndZeroOnFailure()
    {
        using var context = RefreshTestContext.Create();
        context.AddFile("image.jpg");

        var generation = context.LoadFolder();

        Assert.True(generation > 0);
        Assert.True(context.ViewModel.IsLibraryGenerationCurrent(generation));

        context.Catalog.Dispose();
        var failedGeneration = Complete(context.ViewModel.LoadFolderAsync(
            context.PhotoDirectory));

        Assert.Equal(0, failedGeneration);
        Assert.StartsWith(
            "Unable to load folder:",
            context.ViewModel.TransientStatus);
    }

    [Fact]
    public void SupersededRefresh_DoesNotReselectOrReportSuccess()
    {
        using var context = RefreshTestContext.Create();
        context.AddFile("alpha.jpg");
        context.LoadFolder();
        context.ViewModel.TransientStatus = "newer status";

        var result = Complete(context.ViewModel.RefreshCurrentFolderAsync(
            _ => Task.FromResult(true),
            async path =>
            {
                var staleGeneration = await context.ViewModel.LoadFolderAsync(path);
                await context.ViewModel.LoadFolderAsync(path);
                return staleGeneration;
            }));

        Assert.Equal(0, result);
        Assert.Null(context.ViewModel.SelectedImage);
        Assert.Equal("newer status", context.ViewModel.TransientStatus);
    }

    private static void Complete(Task task) => task.GetAwaiter().GetResult();

    private static T Complete<T>(Task<T> task) =>
        task.GetAwaiter().GetResult();

    private sealed class RefreshTestContext : IDisposable
    {
        private readonly string _rootDirectory;

        private RefreshTestContext(
            string rootDirectory,
            string photoDirectory,
            CatalogService catalog,
            MainWindowViewModel viewModel)
        {
            _rootDirectory = rootDirectory;
            PhotoDirectory = photoDirectory;
            Catalog = catalog;
            ViewModel = viewModel;
        }

        public string PhotoDirectory { get; }
        public CatalogService Catalog { get; }
        public MainWindowViewModel ViewModel { get; }

        public static RefreshTestContext Create()
        {
            var root = Path.Combine(
                Path.GetTempPath(),
                $"happy-photon-refresh-{Guid.NewGuid():N}");
            var photos = Path.Combine(root, "photos");
            Directory.CreateDirectory(photos);
            var catalog = new CatalogService(Path.Combine(root, "catalog"));
            catalog.InitializeAsync().GetAwaiter().GetResult();
            return new RefreshTestContext(
                root,
                photos,
                catalog,
                new MainWindowViewModel(
                    catalog,
                    new NullBaseLoader(),
                    loadMetadataAsync: _ => Task.CompletedTask,
                    availabilityService: new TestSourceAvailabilityService(
                        SourceAvailability.RequiresHydration)));
        }

        public string AddFile(string fileName)
        {
            var path = Path.Combine(PhotoDirectory, fileName);
            if (Path.GetExtension(fileName) is ".jpg" or ".jpeg")
            {
                using var image = new MagickImage(MagickColors.Gray, 16, 16);
                image.Write(path, MagickFormat.Jpeg);
            }
            else
            {
                File.WriteAllBytes(path, [1, 2, 3, 4]);
            }
            return path;
        }

        public int LoadFolder()
        {
            ViewModel.SetRootFolder(PhotoDirectory);
            return Refresh();
        }

        public int Refresh() => ViewModel.RefreshCurrentFolderAsync()
            .GetAwaiter().GetResult();

        public void Dispose()
        {
            ViewModel.DisposeAsync().AsTask().GetAwaiter().GetResult();
            Catalog.Dispose();
            if (Directory.Exists(_rootDirectory))
            {
                Directory.Delete(_rootDirectory, recursive: true);
            }
        }

        private sealed class NullBaseLoader : IBaseImageLoader
        {
            public bool CanLoad(ImageFile file) => true;

            public BaseImage? LoadPreviewBase(
                ImageFile file,
                BaseDecodeSettings decode,
                CancellationToken cancellationToken) => null;

            public BaseImage? LoadFullBase(
                ImageFile file,
                BaseDecodeSettings decode,
                CancellationToken cancellationToken) => null;
        }
    }
}
