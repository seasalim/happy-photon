using HappyPhoton.Models;
using HappyPhoton.Services;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

[Collection(AvaloniaTestCollection.Name)]
public sealed class PreviewServiceIntegrationTests : IDisposable
{
    private readonly AvaloniaTestFixture _fixture;
    private readonly string _tempDirectory = Path.Combine(
        Path.GetTempPath(),
        $"HappyPhotonPreviewIntegration_{Guid.NewGuid():N}");

    public PreviewServiceIntegrationTests(AvaloniaTestFixture fixture)
    {
        _fixture = fixture;
    }

    [WindowsFact]
    public async Task RenderedCache_WritesOnLeaveAndReportsHashMatch()
    {
        _fixture.RequireWindows();
        Directory.CreateDirectory(_tempDirectory);
        var first = WriteSource("first.jpg", MagickColors.Orange);
        var second = WriteSource("second.jpg", MagickColors.Blue);
        using var catalog = new CatalogService(
            Path.Combine(_tempDirectory, "catalog"));
        await catalog.InitializeAsync();
        var firstFile = new ImageFile(first);
        var secondFile = new ImageFile(second);
        var settings = new EditSettings { Exposure = 0.25 };

        await using (var service = CreateService(catalog))
        {
            var (preview, _) = await service.LoadPreviewWithHistogramAsync(
                firstFile,
                settings,
                skipHistogram: true);
            Assert.False(File.Exists(catalog.GetPreviewPath(firstFile.CatalogId)));

            var (nextPreview, _) = await service.LoadPreviewWithHistogramAsync(
                secondFile,
                new EditSettings(),
                skipHistogram: true);
            preview?.Dispose();
            nextPreview?.Dispose();
        }

        await using var reader = CreateService(catalog);
        using var matching = await reader.LoadCachedPreviewAsync(
            firstFile,
            settings);
        using var stale = await reader.LoadCachedPreviewAsync(
            firstFile,
            new EditSettings { Exposure = 1.0 });

        Assert.NotNull(matching);
        Assert.True(matching!.SettingsMatch);
        Assert.NotNull(stale);
        Assert.False(stale!.SettingsMatch);
    }

    [WindowsFact]
    public async Task PreviewPixels_ComeFromSharedRenderPipeline()
    {
        _fixture.RequireWindows();
        Directory.CreateDirectory(_tempDirectory);
        var path = WriteSource("shared-render.jpg", MagickColors.Orange);
        using var catalog = new CatalogService(
            Path.Combine(_tempDirectory, "shared-render-catalog"));
        await catalog.InitializeAsync();
        var file = new ImageFile(path);
        var settings = new EditSettings
        {
            Exposure = 0.5,
            Contrast = 20,
            Saturation = 15
        };

        await using var service = CreateService(catalog);
        var (preview, _) = await service.LoadPreviewWithHistogramAsync(
            file,
            settings,
            skipHistogram: true);
        using var actual = preview;

        var loader = new StandardBaseLoader();
        using var baseImage = loader.LoadPreviewBase(
            file,
            BaseDecodeSettings.Default,
            CancellationToken.None);
        Assert.NotNull(baseImage);
        using var rendered = new RenderPipeline().Render(new RenderRequest(
            baseImage!,
            settings,
            RenderIntent.Preview,
            BaseImage.InteractivePreviewMaxDimension,
            new RenderOptions(false, false)));
        using var expected = BitmapConversionService.ConvertToBitmap(rendered.Image);

        Assert.NotNull(actual);
        Assert.NotNull(expected);
        Assert.Equal(
            BitmapConversionService.CopyBgraPixels(expected!),
            BitmapConversionService.CopyBgraPixels(actual!));
    }

    private string WriteSource(string name, MagickColor color)
    {
        var path = Path.Combine(_tempDirectory, name);
        using var image = new MagickImage(color, 64, 48);
        image.Write(path, MagickFormat.Jpeg);
        return path;
    }

    private static PreviewService CreateService(CatalogService catalog) =>
        new(
            catalog,
            new StandardBaseLoader(),
            new RenderPipeline(),
            new HistogramService());

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }
}
