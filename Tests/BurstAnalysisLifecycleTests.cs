using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

[Collection(AvaloniaTestCollection.Name)]
public sealed class BurstAnalysisLifecycleTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        $"happy-photon-bursts-{Guid.NewGuid():N}");

    [Fact]
    public void FolderOpen_DoesNotAnalyzeCaptureTimes_WhenBurstsAreOff()
    {
        var metadataLoads = 0;
        var (catalog, viewModel, photos) = CreateContext(image =>
        {
            Interlocked.Increment(ref metadataLoads);
            ApplyCaptureTime(image);
            return Task.CompletedTask;
        });
        using (catalog)
        {
            try
            {
                CreateJpeg(Path.Combine(photos, "one.jpg"));

                Complete(viewModel.LoadFolderAsync(photos));
                WaitForThumbnailAttempt(viewModel);
                Complete(viewModel.WaitForBurstAnalysisAsync());

                Assert.False(viewModel.ShowBurstGroups);
                Assert.False(viewModel.BurstsComputed);
                Assert.Equal(0, metadataLoads);
            }
            finally
            {
                Complete(viewModel.DisposeAsync().AsTask());
            }
        }
    }

    [Fact]
    public void EnablingBursts_AnalyzesCurrentFolderOnDemand()
    {
        var metadataLoads = 0;
        var (catalog, viewModel, photos) = CreateContext(image =>
        {
            Interlocked.Increment(ref metadataLoads);
            ApplyCaptureTime(image);
            return Task.CompletedTask;
        });
        using (catalog)
        {
            try
            {
                CreateJpeg(Path.Combine(photos, "one.jpg"));
                CreateJpeg(Path.Combine(photos, "two.jpg"));
                Complete(viewModel.LoadFolderAsync(photos));

                viewModel.ShowBurstGroups = true;
                Complete(viewModel.WaitForBurstAnalysisAsync());

                Assert.Equal(2, metadataLoads);
                Assert.True(viewModel.BurstsComputed);
                Assert.All(
                    viewModel.Library.AllImages,
                    image => Assert.Equal(2, image.BurstSize));
            }
            finally
            {
                Complete(viewModel.DisposeAsync().AsTask());
            }
        }
    }

    [Fact]
    public void EnablingBurstsBeforeFolder_WaitsWithoutFalseProgress()
    {
        var metadataLoads = 0;
        var (catalog, viewModel, photos) = CreateContext(image =>
        {
            Interlocked.Increment(ref metadataLoads);
            ApplyCaptureTime(image);
            return Task.CompletedTask;
        });
        using (catalog)
        {
            try
            {
                viewModel.ShowBurstGroups = true;

                Assert.Null(viewModel.PinnedStatus);
                Assert.Null(viewModel.StatusMessage);

                CreateJpeg(Path.Combine(photos, "one.jpg"));
                Complete(viewModel.LoadFolderAsync(photos));
                Complete(viewModel.WaitForBurstAnalysisAsync());

                Assert.Equal(1, metadataLoads);
                Assert.True(viewModel.BurstsComputed);
                Assert.Null(viewModel.PinnedStatus);
            }
            finally
            {
                Complete(viewModel.DisposeAsync().AsTask());
            }
        }
    }

    [Fact]
    public void DisablingBursts_CancelsTheRemainingSweep()
    {
        var firstLoadStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirstLoad = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var metadataLoads = 0;
        var (catalog, viewModel, photos) = CreateContext(async image =>
        {
            if (Interlocked.Increment(ref metadataLoads) == 1)
            {
                firstLoadStarted.TrySetResult();
                await releaseFirstLoad.Task;
            }
            ApplyCaptureTime(image);
        });
        using (catalog)
        {
            try
            {
                CreateJpeg(Path.Combine(photos, "one.jpg"));
                CreateJpeg(Path.Combine(photos, "two.jpg"));
                Complete(viewModel.LoadFolderAsync(photos));

                viewModel.ShowBurstGroups = true;
                Complete(firstLoadStarted.Task);

                Assert.Equal("Analyzing capture times…", viewModel.PinnedStatus);
                Assert.Equal(viewModel.PinnedStatus, viewModel.StatusMessage);

                viewModel.ShowBurstGroups = false;
                Assert.Null(viewModel.PinnedStatus);
                Assert.Null(viewModel.StatusMessage);
                releaseFirstLoad.TrySetResult();
                Complete(viewModel.WaitForBurstAnalysisAsync());

                Assert.Equal(1, metadataLoads);
                Assert.False(viewModel.BurstsComputed);
                Assert.Null(viewModel.PinnedStatus);
            }
            finally
            {
                releaseFirstLoad.TrySetResult();
                Complete(viewModel.DisposeAsync().AsTask());
            }
        }
    }

    [Fact]
    public void FolderChange_RestartsRequestedAnalysisForTheNewFolder()
    {
        var firstLoadStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirstLoad = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var loadedNames = new List<string>();
        var (catalog, viewModel, firstFolder) = CreateContext(async image =>
        {
            lock (loadedNames) loadedNames.Add(image.FileName);
            if (image.FileName == "old.jpg")
            {
                firstLoadStarted.TrySetResult();
                await releaseFirstLoad.Task;
            }
            ApplyCaptureTime(image);
        });
        using (catalog)
        {
            try
            {
                CreateJpeg(Path.Combine(firstFolder, "old.jpg"));
                Complete(viewModel.LoadFolderAsync(firstFolder));
                viewModel.ShowBurstGroups = true;
                Complete(firstLoadStarted.Task);

                var secondFolder = Path.Combine(
                    Path.GetDirectoryName(firstFolder)!,
                    "second-photos");
                Directory.CreateDirectory(secondFolder);
                CreateJpeg(Path.Combine(secondFolder, "new.jpg"));
                Complete(viewModel.LoadFolderAsync(secondFolder));
                releaseFirstLoad.TrySetResult();
                Complete(viewModel.WaitForBurstAnalysisAsync());

                Assert.Equal(["old.jpg", "new.jpg"], loadedNames);
                Assert.True(viewModel.BurstsComputed);
                Assert.Equal(
                    "new.jpg",
                    Assert.Single(viewModel.Library.AllImages).FileName);
            }
            finally
            {
                releaseFirstLoad.TrySetResult();
                Complete(viewModel.DisposeAsync().AsTask());
            }
        }
    }

    [Fact]
    public void Bursts_AnalyzeLocalImagesAndReportCloudImagesSkipped()
    {
        var metadataLoads = 0;
        var availability = new TestSourceAvailabilityService(
            SourceAvailability.AvailableLocally)
        {
            Resolver = path => Path.GetFileName(path).StartsWith(
                "cloud",
                StringComparison.Ordinal)
                ? SourceAvailability.RequiresHydration
                : SourceAvailability.AvailableLocally
        };
        var (catalog, viewModel, photos) = CreateContext(
            image =>
            {
                Interlocked.Increment(ref metadataLoads);
                ApplyCaptureTime(image);
                return Task.CompletedTask;
            },
            availability);
        using (catalog)
        {
            try
            {
                CreateJpeg(Path.Combine(photos, "local.jpg"));
                CreateJpeg(Path.Combine(photos, "cloud.jpg"));
                Complete(viewModel.LoadFolderAsync(photos));

                viewModel.ShowBurstGroups = true;
                Complete(viewModel.WaitForBurstAnalysisAsync());

                var cloud = viewModel.Library.AllImages.Single(
                    image => image.FileName == "cloud.jpg");
                Assert.Equal(1, metadataLoads);
                Assert.False(cloud.MetadataLoaded);
                Assert.False(cloud.HasBurstGroup);
                Assert.True(cloud.SourceRequiresHydration);
                Assert.Equal(1, viewModel.OnlineOnlyPhotoCount);
                Assert.Equal(
                    "Burst analysis complete — 1 local photo analyzed; " +
                    "1 online-only photo skipped.",
                    viewModel.TransientStatus);
            }
            finally
            {
                Complete(viewModel.DisposeAsync().AsTask());
            }
        }
    }

    [Fact]
    public void Bursts_SuccessClearsStaleCloudState()
    {
        var availability = new TestSourceAvailabilityService(
            SourceAvailability.AvailableLocally);
        var (catalog, viewModel, photos) = CreateContext(
            image =>
            {
                ApplyCaptureTime(image);
                return Task.CompletedTask;
            },
            availability);
        using (catalog)
        {
            try
            {
                CreateJpeg(Path.Combine(photos, "local.jpg"));
                Complete(viewModel.LoadFolderAsync(photos));
                WaitForThumbnailAttempt(viewModel);
                var image = Assert.Single(viewModel.Library.AllImages);
                image.SourceRequiresHydration = true;
                viewModel.InitializeCloudSourceCount([image]);

                viewModel.ShowBurstGroups = true;
                Complete(viewModel.WaitForBurstAnalysisAsync());

                Assert.True(image.MetadataLoaded);
                Assert.False(image.SourceRequiresHydration);
                Assert.Equal(0, viewModel.OnlineOnlyPhotoCount);
            }
            finally
            {
                Complete(viewModel.DisposeAsync().AsTask());
            }
        }
    }

    private (CatalogService Catalog, MainWindowViewModel ViewModel, string Photos)
        CreateContext(
            Func<ImageFile, Task> loadMetadataAsync,
            ISourceAvailabilityService? availabilityService = null)
    {
        var photos = Path.Combine(_root, Guid.NewGuid().ToString("N"), "photos");
        Directory.CreateDirectory(photos);
        var catalog = new CatalogService(Path.Combine(
            Path.GetDirectoryName(photos)!, "catalog"));
        catalog.InitializeAsync().GetAwaiter().GetResult();
        var viewModel = new MainWindowViewModel(
            catalog,
            baseLoader: null,
            loadMetadataAsync,
            availabilityService,
            postSelection: _ => { });
        return (catalog, viewModel, photos);
    }

    private static void ApplyCaptureTime(ImageFile image)
    {
        var offset = image.FileName.StartsWith("two", StringComparison.Ordinal)
            ? 1
            : 0;
        image.ApplyMetadata(new ImageMetadata
        {
            DateTaken = new DateTime(2026, 8, 8, 12, 0, offset)
        });
    }

    private static void CreateJpeg(string path)
    {
        using var image = new MagickImage(MagickColors.Gray, 16, 16);
        image.Write(path, MagickFormat.Jpeg);
    }

    private static void WaitForThumbnailAttempt(
        MainWindowViewModel viewModel)
    {
        var timeout = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (DateTime.UtcNow < timeout)
        {
            var image = viewModel.Library.AllImages.SingleOrDefault();
            if (image?.Thumbnail != null ||
                image?.ThumbnailLoadFailed == true ||
                image?.ThumbnailDeferredForHydration == true)
            {
                return;
            }
            Thread.Sleep(10);
        }

        throw new TimeoutException("Thumbnail attempt did not finish.");
    }

    private static void Complete(Task task) => task.GetAwaiter().GetResult();

    private static T Complete<T>(Task<T> task) => task.GetAwaiter().GetResult();

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
