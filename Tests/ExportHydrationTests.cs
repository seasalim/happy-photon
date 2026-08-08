using HappyPhoton.Models;
using HappyPhoton.Services;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class ExportHydrationTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        $"happy-photon-export-hydration-{Guid.NewGuid():N}");

    public ExportHydrationTests() => Directory.CreateDirectory(_root);

    [Fact]
    public async Task Scope_CountsOnlyCloudSourcesAndTheirLogicalBytes()
    {
        var cloudA = WriteFile("cloud-a.jpg", 17);
        var local = WriteFile("local.jpg", 23);
        var cloudB = WriteFile("cloud-b.jpg", 31);
        var availability = new TestSourceAvailabilityService(
            SourceAvailability.AvailableLocally)
        {
            Resolver = path => path == local
                ? SourceAvailability.AvailableLocally
                : SourceAvailability.RequiresHydration
        };

        using var catalog = new CatalogService(Path.Combine(_root, "catalog"));
        await catalog.InitializeAsync();
        await using var imageService = new ImageService(
            catalog,
            new CountingBaseLoader(),
            availability);

        var scope = imageService.GetExportHydrationScope(
            [new ImageFile(cloudA), new ImageFile(local), new ImageFile(cloudB)]);

        Assert.Equal(2, scope.FileCount);
        Assert.Equal(48, scope.LogicalBytes);
    }

    [Fact]
    public async Task BackgroundExport_CloudSourceMakesZeroSourceCalls()
    {
        var loader = new CountingBaseLoader();
        var profileReads = 0;
        var service = CreateExportService(loader, () => profileReads++);
        var settings = new ExportSettings
        {
            OutputFolder = Path.Combine(_root, "canceled")
        };

        var count = await service.ExportBatchAsync(
            [new ImageFile(Path.Combine(_root, "cloud.jpg"))],
            settings);

        Assert.Equal(0, count);
        Assert.Empty(loader.FullLoads);
        Assert.Equal(0, profileReads);
    }

    [Fact]
    public void ExportMetadata_ReadsCloudSourceOnlyWithApproval()
    {
        var profileReads = 0;
        var availability = new TestSourceAvailabilityService(
            SourceAvailability.RequiresHydration);
        var metadata = new ExportMetadataService(
            "Happy Photon test",
            availability,
            _ =>
            {
                profileReads++;
                return null;
            });
        var source = new ImageFile(Path.Combine(_root, "cloud.jpg"));
        using var backgroundDestination = new MagickImage(
            MagickColors.Blue,
            8,
            8);
        using var approvedDestination = new MagickImage(
            MagickColors.Blue,
            8,
            8);

        metadata.Apply(source, backgroundDestination, stripLocationData: false);
        metadata.Apply(
            source,
            approvedDestination,
            stripLocationData: false,
            SourceReadIntent.UserApprovedHydration);

        Assert.Equal(1, profileReads);
    }

    [Fact]
    public async Task ApprovedExport_ReadsExactlyConfirmedImages()
    {
        var loader = new CountingBaseLoader();
        var profileReads = new List<string>();
        var availability = new TestSourceAvailabilityService(
            SourceAvailability.RequiresHydration);
        var metadata = new ExportMetadataService(
            "Happy Photon test",
            availability,
            path =>
            {
                profileReads.Add(path);
                return null;
            });
        var service = new ImageExportService(
            new RenderPipeline(),
            new GatedBaseImageLoader(loader, availability),
            metadata);
        var first = new ImageFile(Path.Combine(_root, "first.jpg"));
        var excluded = new ImageFile(Path.Combine(_root, "excluded.jpg"));
        var third = new ImageFile(Path.Combine(_root, "third.jpg"));
        var settings = new ExportSettings
        {
            OutputFolder = Path.Combine(_root, "approved")
        };

        var count = await service.ExportBatchApprovedAsync(
            [first, third],
            settings,
            progress: null,
            CancellationToken.None);

        Assert.Equal(2, count);
        Assert.Equal([first.FilePath, third.FilePath], loader.FullLoads);
        Assert.Equal([first.FilePath, third.FilePath], profileReads);
        Assert.DoesNotContain(excluded.FilePath, loader.FullLoads);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private ImageExportService CreateExportService(
        CountingBaseLoader loader,
        Action onProfileRead)
    {
        var availability = new TestSourceAvailabilityService(
            SourceAvailability.RequiresHydration);
        return new ImageExportService(
            new RenderPipeline(),
            new GatedBaseImageLoader(loader, availability),
            new ExportMetadataService(
                "Happy Photon test",
                availability,
                _ =>
                {
                    onProfileRead();
                    return null;
                }));
    }

    private string WriteFile(string name, int length)
    {
        var path = Path.Combine(_root, name);
        File.WriteAllBytes(path, new byte[length]);
        return path;
    }

    private sealed class CountingBaseLoader : IBaseImageLoader
    {
        internal List<string> FullLoads { get; } = [];

        public bool CanLoad(ImageFile file) => true;

        public BaseImage? LoadPreviewBase(
            ImageFile file,
            BaseDecodeSettings decode,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public BaseImage? LoadFullBase(
            ImageFile file,
            BaseDecodeSettings decode,
            CancellationToken cancellationToken)
        {
            FullLoads.Add(file.FilePath);
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
}
