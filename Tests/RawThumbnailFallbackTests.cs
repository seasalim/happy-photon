using ImageMagick;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using Xunit;

namespace HappyPhoton.Tests;

[Collection(AvaloniaTestCollection.Name)]
public sealed class RawThumbnailFallbackTests : IDisposable
{
    private readonly AvaloniaTestFixture _fixture;
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        $"HappyPhotonRawThumbFallback_{Guid.NewGuid():N}");

    public RawThumbnailFallbackTests(AvaloniaTestFixture fixture) =>
        _fixture = fixture;

    [WindowsFact]
    public async Task TonalOnlyFallbackMatchesUneditedSourceWithoutExtraction()
    {
        _fixture.RequireWindows();
        var (catalog, file) = await CreateCachedRawAsync(150, 90);
        using (catalog)
        {
            var raw = new CountingRawService();
            await using var renderedCache =
                new RenderedThumbnailCacheService(catalog);
            await using var service = new ThumbnailService(
                catalog,
                raw,
                new RenderPipeline(),
                renderedCache);
            file.EditSettings = new EditSettings { Exposure = 3, Saturation = 50 };

            using var unedited = await service.LoadUneditedThumbnailAsync(file);
            using var fallback = await service.LoadThumbnailAsync(file);

            Assert.NotNull(unedited);
            Assert.NotNull(fallback);
            Assert.Equal(
                BitmapConversionService.CopyBgraPixels(unedited!),
                BitmapConversionService.CopyBgraPixels(fallback!));
            Assert.Equal(0, raw.ExtractCount);
        }
    }

    [WindowsFact]
    public async Task GeometryFallbackRotatesWithoutChangingSourceCache()
    {
        _fixture.RequireWindows();
        var (catalog, file) = await CreateCachedRawAsync(150, 90);
        using (catalog)
        {
            var cachePath = catalog.GetThumbnailPath(file.CatalogId);
            var before = await File.ReadAllBytesAsync(cachePath);
            await using var renderedCache =
                new RenderedThumbnailCacheService(catalog);
            await using var service = new ThumbnailService(
                catalog,
                new CountingRawService(),
                new RenderPipeline(),
                renderedCache);
            file.EditSettings = new EditSettings { Rotation = 90 };

            using var fallback = await service.LoadThumbnailAsync(file);

            Assert.NotNull(fallback);
            Assert.Equal(90, fallback!.PixelSize.Width);
            Assert.Equal(150, fallback.PixelSize.Height);
            Assert.Equal(before, await File.ReadAllBytesAsync(cachePath));
        }
    }

    [WindowsFact]
    public async Task GeometryFallbackDoesNotUpscaleCrop()
    {
        _fixture.RequireWindows();
        var (catalog, file) = await CreateCachedRawAsync(150, 100);
        using (catalog)
        {
            await using var renderedCache =
                new RenderedThumbnailCacheService(catalog);
            await using var service = new ThumbnailService(
                catalog,
                new CountingRawService(),
                new RenderPipeline(),
                renderedCache);
            file.EditSettings = new EditSettings
            {
                Crop = new CropRegion
                {
                    Left = 0.25,
                    Top = 0.25,
                    Right = 0.75,
                    Bottom = 0.75
                }
            };

            using var unedited = await service.LoadUneditedThumbnailAsync(file);
            using var fallback = await service.LoadThumbnailAsync(file);

            Assert.NotNull(unedited);
            Assert.NotNull(fallback);
            var expected = file.EditSettings.Crop!.ToPixels(
                unedited!.PixelSize.Width,
                unedited.PixelSize.Height);
            Assert.Equal(expected.Width, fallback!.PixelSize.Width);
            Assert.Equal(expected.Height, fallback.PixelSize.Height);
        }
    }

    [WindowsFact]
    public async Task AgentTonalBatchInLibraryDoesNotDecodeRawBase()
    {
        _fixture.RequireWindows();
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, $"{Guid.NewGuid():N}.dng");
        await File.WriteAllBytesAsync(path, [1]);
        using var catalog = new CatalogService(Path.Combine(
            _root,
            Guid.NewGuid().ToString("N")));
        await catalog.InitializeAsync();
        var loader = new CountingBaseLoader();
        await using var viewModel = new MainWindowViewModel(catalog, loader);
        var file = new ImageFile(path);
        viewModel.Library.SetImages([file]);
        viewModel.SelectedImage = file;
        var patch = new AgentEditSettingsPatch(
            new EditSettings { Exposure = 1 },
            ApplyWb: false,
            ApplyBaseLook: false,
            ApplyHighlightReconstruction: false);

        var failures = await viewModel.ApplyAgentEditSettingsToImagesAsync(
            [file],
            patch);

        Assert.Empty(failures);
        Assert.Equal(0, loader.LoadCount);
    }

    private async Task<(CatalogService Catalog, ImageFile File)> CreateCachedRawAsync(
        int width,
        int height)
    {
        Directory.CreateDirectory(_root);
        var sourcePath = Path.Combine(_root, $"{Guid.NewGuid():N}.dng");
        await File.WriteAllBytesAsync(sourcePath, [1]);
        File.SetLastWriteTimeUtc(sourcePath, DateTime.UtcNow.AddMinutes(-2));
        var catalog = new CatalogService(Path.Combine(_root, Guid.NewGuid().ToString("N")));
        await catalog.InitializeAsync();
        var file = new ImageFile(sourcePath);
        await file.EnsureCatalogIdAsync(catalog);
        var cachePath = catalog.GetThumbnailPath(file.CatalogId);
        Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
        using var image = new MagickImage(MagickColors.Gray, (uint)width, (uint)height);
        image.Write(cachePath, MagickFormat.Jpeg);
        return (catalog, file);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private sealed class CountingRawService : IRawProcessingService
    {
        public int ExtractCount { get; private set; }
        public bool IsAvailable => true;
        public RawMetadata? ExtractMetadata(string filePath) => null;
        public byte[]? ExtractThumbnail(string filePath)
        {
            ExtractCount++;
            return null;
        }
    }

    private sealed class CountingBaseLoader : IBaseImageLoader
    {
        public int LoadCount { get; private set; }
        public bool CanLoad(ImageFile file) => true;
        public BaseImage? LoadPreviewBase(
            ImageFile file,
            BaseDecodeSettings decode,
            CancellationToken cancellationToken)
        {
            LoadCount++;
            throw new InvalidOperationException("Library batch must not decode RAW.");
        }

        public BaseImage? LoadFullBase(
            ImageFile file,
            BaseDecodeSettings decode,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
