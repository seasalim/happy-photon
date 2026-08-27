using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class BurstAnalysisLifecycleTests : IDisposable
{
    private readonly CatalogVmFixture _fx = new("bursts");

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
                TestImages.WriteJpeg(Path.Combine(photos, "one.jpg"));

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
                TestImages.WriteJpeg(Path.Combine(photos, "one.jpg"));
                TestImages.WriteJpeg(Path.Combine(photos, "two.jpg"));
                Complete(viewModel.LoadFolderAsync(photos));

                viewModel.ShowBurstGroups = true;
                Complete(viewModel.WaitForBurstAnalysisAsync());

                Assert.Equal(2, metadataLoads);
                Assert.True(viewModel.BurstsComputed);
                Assert.False(viewModel.IsBurstAnalysisActive);
                Assert.Equal(2, viewModel.BurstAnalysisProcessed);
                Assert.Equal(2, viewModel.BurstAnalysisTotal);
                Assert.All(
                    viewModel.Browse.AllImages,
                    image => Assert.Equal(2, image.BurstSize));
            }
            finally
            {
                Complete(viewModel.DisposeAsync().AsTask());
            }
        }
    }

    [Fact]
    public void Bursts_AssignCaptureLevelMembershipToPairsAndSingles()
    {
        var (catalog, viewModel, photos) = CreateContext(image =>
        {
            ApplyCaptureTime(image);
            return Task.CompletedTask;
        });
        using (catalog)
        {
            try
            {
                TestImages.WriteJpeg(Path.Combine(photos, "one.dng"));
                TestImages.WriteJpeg(Path.Combine(photos, "one.jpg"));
                TestImages.WriteJpeg(Path.Combine(photos, "two.jpg"));
                Complete(viewModel.LoadFolderAsync(photos));

                viewModel.ShowBurstGroups = true;
                Complete(viewModel.WaitForBurstAnalysisAsync());

                var raw = viewModel.Browse.AllImages.Single(
                    image => image.FileName == "one.dng");
                var jpeg = viewModel.Browse.AllImages.Single(
                    image => image.FileName == "one.jpg");
                var single = viewModel.Browse.AllImages.Single(
                    image => image.FileName == "two.jpg");
                Assert.Equal(1, raw.BurstIndex);
                Assert.Equal(raw.BurstIndex, jpeg.BurstIndex);
                Assert.Equal(2, single.BurstIndex);
                Assert.All(
                    viewModel.Browse.AllImages,
                    image => Assert.Equal(2, image.BurstSize));
                Assert.Equal(
                    viewModel.GetBurstMembership(raw.FilePath)?.BurstId,
                    viewModel.GetBurstMembership(single.FilePath)?.BurstId);
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

                TestImages.WriteJpeg(Path.Combine(photos, "one.jpg"));
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
                TestImages.WriteJpeg(Path.Combine(photos, "one.jpg"));
                TestImages.WriteJpeg(Path.Combine(photos, "two.jpg"));
                Complete(viewModel.LoadFolderAsync(photos));

                viewModel.ShowBurstGroups = true;
                Complete(firstLoadStarted.Task);

                Assert.Null(viewModel.PinnedStatus);
                Assert.True(viewModel.IsBurstAnalysisActive);
                Assert.Equal(0, viewModel.BurstAnalysisProcessed);
                Assert.Equal(2, viewModel.BurstAnalysisTotal);
                Assert.NotNull(
                    viewModel.CaptureBackgroundActivitySnapshot().CaptureTimes);

                viewModel.ShowBurstGroups = false;
                Assert.Null(viewModel.PinnedStatus);
                Assert.Null(viewModel.StatusMessage);
                // Disabling removes the published activity immediately, even while
                // the sweep is still blocked in the gated metadata load.
                Assert.False(viewModel.IsBurstAnalysisActive);
                Assert.Null(
                    viewModel.CaptureBackgroundActivitySnapshot().CaptureTimes);
                releaseFirstLoad.TrySetResult();
                Complete(viewModel.WaitForBurstAnalysisAsync());

                Assert.Equal(1, metadataLoads);
                Assert.False(viewModel.BurstsComputed);
                Assert.False(viewModel.IsBurstAnalysisActive);
                Assert.Equal(1, viewModel.BurstAnalysisProcessed);
                Assert.Equal(2, viewModel.BurstAnalysisTotal);
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
                TestImages.WriteJpeg(Path.Combine(firstFolder, "old.jpg"));
                Complete(viewModel.LoadFolderAsync(firstFolder));
                viewModel.ShowBurstGroups = true;
                Complete(firstLoadStarted.Task);

                var secondFolder = Path.Combine(
                    Path.GetDirectoryName(firstFolder)!,
                    "second-photos");
                Directory.CreateDirectory(secondFolder);
                TestImages.WriteJpeg(Path.Combine(secondFolder, "new.jpg"));
                Complete(viewModel.LoadFolderAsync(secondFolder));
                releaseFirstLoad.TrySetResult();
                Complete(viewModel.WaitForBurstAnalysisAsync());

                Assert.Equal(["old.jpg", "new.jpg"], loadedNames);
                Assert.True(viewModel.BurstsComputed);
                Assert.Equal(1, viewModel.BurstAnalysisProcessed);
                Assert.Equal(1, viewModel.BurstAnalysisTotal);
                Assert.Equal(
                    "new.jpg",
                    Assert.Single(viewModel.Browse.AllImages).FileName);
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
                TestImages.WriteJpeg(Path.Combine(photos, "local.jpg"));
                var cloudPath = Path.Combine(photos, "cloud.jpg");
                TestImages.WriteJpeg(cloudPath);
                var cloudPrimary = Complete(catalog.GetOrCreateImageAsync(cloudPath));
                Assert.NotNull(Complete(catalog.CreateVersionAsync(cloudPrimary)));
                Complete(viewModel.LoadFolderAsync(photos));

                viewModel.ShowBurstGroups = true;
                Complete(viewModel.WaitForBurstAnalysisAsync());

                var cloud = viewModel.Browse.AllImages.Where(
                    image => image.FileName == "cloud.jpg").ToArray();
                Assert.Equal(2, cloud.Length);
                Assert.Equal(1, metadataLoads);
                Assert.Equal(2, viewModel.BurstAnalysisProcessed);
                Assert.Equal(2, viewModel.BurstAnalysisTotal);
                Assert.All(cloud, image => Assert.False(image.MetadataLoaded));
                Assert.All(cloud, image => Assert.False(image.HasBurstGroup));
                Assert.All(cloud,
                    image => Assert.True(image.SourceRequiresHydration));
                Assert.Equal(2, viewModel.OnlineOnlyPhotoCount);
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
                var path = Path.Combine(photos, "local.jpg");
                TestImages.WriteJpeg(path);
                var primary = Complete(catalog.GetOrCreateImageAsync(path));
                Assert.NotNull(Complete(catalog.CreateVersionAsync(primary)));
                Complete(viewModel.LoadFolderAsync(photos));
                WaitForThumbnailAttempt(viewModel);
                var images = viewModel.Browse.AllImages.ToArray();
                Assert.Equal(2, images.Length);
                foreach (var image in images)
                    image.SourceRequiresHydration = true;
                viewModel.RefreshOnlineOnlyPhotoCount();

                viewModel.ShowBurstGroups = true;
                Complete(viewModel.WaitForBurstAnalysisAsync());

                Assert.All(images, image => Assert.True(image.MetadataLoaded));
                Assert.All(images,
                    image => Assert.False(image.SourceRequiresHydration));
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
        var runFolder = Guid.NewGuid().ToString("N");
        var photos = Path.Combine(_fx.Path(runFolder), "photos");
        Directory.CreateDirectory(photos);
        var catalog = _fx.CreateCatalog(Path.Combine(runFolder, "catalog"));
        catalog.InitializeAsync().GetAwaiter().GetResult();
        var viewModel = _fx.CreateViewModel(
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


    // Bounds a genuine hang rather than asserting decode latency. A real JPEG
    // decode is milliseconds locally but can stall for seconds on a shared CI
    // runner, which is what made the old 5s budget flake on macOS.
    private static readonly TimeSpan ThumbnailAttemptTimeout =
        TestWaits.Condition;

    private static void WaitForThumbnailAttempt(
        MainWindowViewModel viewModel)
    {
        var deadline = DateTime.UtcNow + ThumbnailAttemptTimeout;
        while (DateTime.UtcNow < deadline)
        {
            var images = viewModel.Browse.AllImages;
            if (images.Count > 0 && images.All(image =>
                    image.Thumbnail != null ||
                    image.ThumbnailLoadFailed ||
                    image.ThumbnailDeferredForHydration))
            {
                return;
            }
            Thread.Sleep(10);
        }

        var observed = viewModel.Browse.AllImages.FirstOrDefault();
        throw new TimeoutException(
            $"Thumbnail attempt did not finish within " +
            $"{ThumbnailAttemptTimeout.TotalSeconds:0}s. " +
            $"images={viewModel.Browse.AllImages.Count}, " +
            $"thumbnail={observed?.Thumbnail != null}, " +
            $"failed={observed?.ThumbnailLoadFailed}, " +
            $"deferred={observed?.ThumbnailDeferredForHydration}");
    }

    private static void Complete(Task task) => task.GetAwaiter().GetResult();

    private static T Complete<T>(Task<T> task) => task.GetAwaiter().GetResult();

    public void Dispose() => _fx.Dispose();
}


