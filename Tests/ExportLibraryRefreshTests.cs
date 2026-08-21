using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

[Collection(AvaloniaTestCollection.Name)]
public sealed class ExportLibraryRefreshTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        $"happy-photon-export-library-{Guid.NewGuid():N}");

    [WindowsFact]
    public void ApprovedExport_RefreshesThumbnailAfterHydration()
    {
        Directory.CreateDirectory(_root);
        var sourcePath = WriteJpeg("cloud.jpg");
        using var catalog = new CatalogService(Path.Combine(_root, "catalog"));
        Complete(catalog.InitializeAsync());
        var availability = new TestSourceAvailabilityService(
            SourceAvailability.RequiresHydration);
        var viewModel = new MainWindowViewModel(
            catalog,
            new HydratingBaseLoader(availability),
            _ => throw new InvalidOperationException(
                "Post-export metadata loading is not expected."),
            availability);
        try
        {
            var image = new ImageFile(
                sourcePath,
                SourceAvailability.RequiresHydration)
            {
                ThumbnailDeferredForHydration = true
            };
            viewModel.Library.SetImages([image]);
            viewModel.ExportSettings.OutputFolder = Path.Combine(_root, "export");

            var exported = Complete(
                viewModel.ExportBatchApprovedAsync([image]));
            TestWaits.Until(() => image.Thumbnail != null);

            Assert.Equal(1, exported.ExportedCount);
            Assert.Empty(exported.FailedImages);
            Assert.False(image.SourceRequiresHydration);
            Assert.False(image.ThumbnailDeferredForHydration);
            Assert.NotNull(image.Thumbnail);
            Assert.False(image.MetadataLoaded);
            Assert.Equal(0, viewModel.OnlineOnlyPhotoCount);
        }
        finally
        {
            Complete(viewModel.DisposeAsync().AsTask());
        }
    }

    [WindowsFact]
    public void ExportFailure_IsNotReplacedByLibraryRefreshFailure()
    {
        Directory.CreateDirectory(_root);
        var sourcePath = WriteJpeg("failure.jpg");
        using var catalog = new CatalogService(Path.Combine(_root, "failure-catalog"));
        Complete(catalog.InitializeAsync());
        var availability = new TestSourceAvailabilityService(
            SourceAvailability.RequiresHydration);
        var metadataLoads = 0;
        var viewModel = new MainWindowViewModel(
            catalog,
            new FailingHydratingBaseLoader(availability),
            _ =>
            {
                metadataLoads++;
                throw new InvalidOperationException("metadata failed");
            },
            availability);
        try
        {
            var image = new ImageFile(
                sourcePath,
                SourceAvailability.RequiresHydration)
            {
                ThumbnailDeferredForHydration = true
            };
            viewModel.Library.SetImages([image]);
            viewModel.ExportSettings.OutputFolder = Path.Combine(_root, "failure-export");

            var exception = Assert.Throws<InvalidOperationException>(() =>
                Complete(viewModel.ExportBatchApprovedAsync([image])));

            Assert.Equal("export failed", exception.Message);
            Assert.Equal(0, metadataLoads);
            Assert.True(image.SourceRequiresHydration);
            Assert.True(image.ThumbnailDeferredForHydration);
        }
        finally
        {
            Complete(viewModel.DisposeAsync().AsTask());
        }
    }

    [WindowsFact]
    public void CanceledExport_DoesNotRunLibraryRefreshTail()
    {
        Directory.CreateDirectory(_root);
        var sourcePath = WriteJpeg("canceled.jpg");
        using var catalog = new CatalogService(Path.Combine(_root, "canceled-catalog"));
        Complete(catalog.InitializeAsync());
        var availability = new TestSourceAvailabilityService(
            SourceAvailability.AvailableLocally);
        var metadataLoads = 0;
        var viewModel = new MainWindowViewModel(
            catalog,
            new HydratingBaseLoader(availability),
            _ =>
            {
                metadataLoads++;
                return Task.CompletedTask;
            },
            availability);
        try
        {
            var image = new ImageFile(
                sourcePath,
                SourceAvailability.RequiresHydration)
            {
                ThumbnailDeferredForHydration = true
            };
            viewModel.Library.SetImages([image]);
            viewModel.ExportSettings.OutputFolder = Path.Combine(_root, "canceled-export");
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            Assert.ThrowsAny<OperationCanceledException>(() => Complete(
                viewModel.ExportBatchApprovedAsync(
                    [image],
                    cancellationToken: cancellation.Token)));

            Assert.Equal(0, metadataLoads);
            Assert.True(image.SourceRequiresHydration);
            Assert.True(image.ThumbnailDeferredForHydration);
            Assert.Null(image.Thumbnail);
        }
        finally
        {
            Complete(viewModel.DisposeAsync().AsTask());
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private string WriteJpeg(string name)
    {
        var path = Path.Combine(_root, name);
        TestImages.WriteJpeg(path);
        return path;
    }

    private static void Complete(Task task) => task.GetAwaiter().GetResult();

    private static T Complete<T>(Task<T> task) =>
        task.GetAwaiter().GetResult();

    private sealed class HydratingBaseLoader(
        TestSourceAvailabilityService availability) : IBaseImageLoader
    {
        public bool CanLoad(ImageFile file) => true;

        BaseImageLoadOutcome IBaseImageLoader.LoadPreviewBaseWithOutcome(ImageFile file, BaseDecodeSettings decode, CancellationToken cancellationToken) => BaseImageLoadOutcome.FromImage(LoadPreviewBase(file, decode, cancellationToken), BaseImageLoadFailure.DecodeFailed);

        public BaseImage? LoadPreviewBase(
            ImageFile file,
            BaseDecodeSettings decode,
            CancellationToken cancellationToken) => null;

        public BaseImage? LoadFullBase(
            ImageFile file,
            BaseDecodeSettings decode,
            CancellationToken cancellationToken)
        {
            availability.Availability = SourceAvailability.AvailableLocally;
            return new BaseImage(
                new MagickImage(MagickColors.Orange, 16, 16),
                new BaseImageInfo(
                    BaseSourceKind.Standard,
                    false,
                    decode,
                    null,
                    null,
                    6504,
                    0,
                    false,
                    null,
                    1,
                    16,
                    16));
        }
    }

    private sealed class FailingHydratingBaseLoader(
        TestSourceAvailabilityService availability) : IBaseImageLoader
    {
        public bool CanLoad(ImageFile file) => true;

        BaseImageLoadOutcome IBaseImageLoader.LoadPreviewBaseWithOutcome(ImageFile file, BaseDecodeSettings decode, CancellationToken cancellationToken) => BaseImageLoadOutcome.FromImage(LoadPreviewBase(file, decode, cancellationToken), BaseImageLoadFailure.DecodeFailed);

        public BaseImage? LoadPreviewBase(
            ImageFile file,
            BaseDecodeSettings decode,
            CancellationToken cancellationToken) => null;

        public BaseImage? LoadFullBase(
            ImageFile file,
            BaseDecodeSettings decode,
            CancellationToken cancellationToken)
        {
            availability.Availability = SourceAvailability.AvailableLocally;
            throw new InvalidOperationException("export failed");
        }
    }
}
