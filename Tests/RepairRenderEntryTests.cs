using Avalonia.Media.Imaging;
using HappyPhoton.Models;
using HappyPhoton.Services;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

[Collection(AvaloniaTestCollection.Name)]
public sealed class RepairRenderEntryTests
{
    internal static EditSettings Settings() => new()
    {
        Detail = new() { CaptureSharpen = 0 },
        Crop = new() { Left = .1, Top = .1, Right = .9, Bottom = .9 },
        Repairs = [new() { Type = "clone", U = .6, V = .5, Su = .2, Sv = .3, Radius = .1, Feather = 0 }]
    };

    [Theory]
    [InlineData("tick")] [InlineData("compare")] [InlineData("loupe")]
    [InlineData("side")] [InlineData("proof")] [InlineData("resting")] [InlineData("warm")]
    public async Task EntryRendersRepairedPixels(string entry)
    {
        using var folder = new TemporaryDirectory();
        using var catalog = new CatalogService(folder.Path);
        await catalog.InitializeAsync();
        var settings = Settings();
        var file = new ImageFile(Path.Combine(folder.Path, "source.jpg")) { EditSettings = settings };
        File.WriteAllBytes(file.FilePath, [0]);
        file.CatalogId = await catalog.GetOrCreateImageAsync(file.FilePath);
        await using var service = new PreviewService(catalog, new Loader(), new RenderPipeline(),
            createRenderedThumbnail: false);
        using var basis = RenderRepairsTests.Fixture();
        var before = RenderPipelineTestSupport.ReadPixels(basis.Pixels);
        var spot = new HealSpot(.6, .5, .2, .3, .1, true, 0);
        using var repaired = RenderPipelineTestSupport.CreateBase(HealOracle.Apply(before, 160, 120, [spot], default), height: 120);
        var neutral = settings.Clone(); neutral.Repairs = null;
        using var expected = new RenderPipeline().Render(new(repaired, neutral, RenderIntent.Preview, null, new(false, false)));
        using var off = new RenderPipeline().Render(new(basis, neutral, RenderIntent.Preview, null, new(false, false)));
        using var expectedBitmap = BitmapConversionService.ConvertToBitmap(expected.Image)!;
        var expectedCodes = BitmapConversionService.CopyBgraPixels(expectedBitmap);
        Bitmap? actual = null;
        if (entry == "warm")
        {
            Assert.True(service.TryStartAdjacentWarm(file));
            await TestWaits.UntilAsync(() => service.PreviewActivityCount == 0 && service.PendingCacheWrites == 0);
            using var cached = await service.LoadCachedPreviewAsync(file, settings);
            Assert.NotNull(cached);
            expected.Image.Quality = 90;
            using var jpeg = new MagickImage(expected.Image.ToByteArray(MagickFormat.Jpeg));
            using var jpegBitmap = BitmapConversionService.ConvertToBitmap(jpeg)!;
            Assert.Equal(BitmapConversionService.CopyBgraPixels(jpegBitmap), BitmapConversionService.CopyBgraPixels(cached.Bitmap));
            return;
        }
        try
        {
            if (entry is "compare" or "loupe")
            {
                using var compared = await service.LoadComparePreviewAsync(file, settings, entry == "loupe" ? 4000 : 1600);
                Assert.NotNull(compared);
                Assert.Equal(expectedCodes, BitmapConversionService.CopyBgraPixels(compared.Bitmap));
                return;
            }
            if (entry == "proof") actual = await service.RenderProofAsync(file, settings, null, OutputColorSpace.Srgb, OutputSharpeningMode.Off);
            else
            {
                var (tick, _) = await service.ApplyEditsToPreviewAsync(file, settings, skipHistogram: true);
                using var ownedTick = tick;
                Assert.NotNull(tick);
                if (entry == "tick")
                {
                    Assert.Equal(expectedCodes, BitmapConversionService.CopyBgraPixels(tick));
                    return;
                }
                if (entry == "resting")
                {
                    var stages = new List<string>();
                    service.RestingStageStarted = stages.Add;
                    using var resting = await service.RenderRestingPreviewAsync(file, settings, 128,
                        service.TryGetPreviewRenderIdentity(tick)!, CancellationToken.None);
                    Assert.NotNull(resting);
                    Assert.Equal(expectedCodes, BitmapConversionService.CopyBgraPixels(resting.Bitmap));
                    Assert.Equal(1, stages.Count(s => s == "repairs"));
                    // A second request uses the same parent without repeating or losing repairs.
                    using var again = await service.RenderRestingPreviewAsync(file, settings, 128,
                        service.TryGetPreviewRenderIdentity(tick)!, CancellationToken.None);
                    Assert.NotNull(again);
                    Assert.Equal(expectedCodes, BitmapConversionService.CopyBgraPixels(again.Bitmap));
                    return;
                }
                actual = (await service.RenderCurrentBaseSideSurfaceAsync(file, settings, 1600)).Bitmap;
            }
            Assert.NotNull(actual);
            Assert.Equal(expectedCodes, BitmapConversionService.CopyBgraPixels(actual));
        }
        finally { actual?.Dispose(); }
        Assert.NotEqual(RenderPipelineTestSupport.ReadPixels(off.Image), RenderPipelineTestSupport.ReadPixels(expected.Image));
        Assert.Equal(before, RenderPipelineTestSupport.ReadPixels(basis.Pixels));
    }

    [Fact]
    public async Task ExportUsesRepairsBeforeGeometry()
    {
        using var folder = new TemporaryDirectory();
        var file = new ImageFile(Path.Combine(folder.Path, "source.jpg")) { EditSettings = Settings() };
        File.WriteAllBytes(file.FilePath, [0]);
        var pipeline = new RenderPipeline(); var called = false;
        var service = new ImageExportService(pipeline, new Loader(), new ExportMetadataService(),
            new DcpProfileService(new SourceAvailabilityService()), request =>
            {
                called = true;
                var actual = pipeline.RenderDisplayRec2020(request);
                using var reference = new HealProductionStage().Repair(request.Base, [new(.6, .5, .2, .3, .1, true, 0)], default);
                var neutral = request.Settings.Clone(); neutral.Repairs = null;
                using var expected = pipeline.RenderDisplayRec2020(request with { Base = reference, Settings = neutral });
                Assert.Equal(RenderPipelineTestSupport.ReadPixels(expected), RenderPipelineTestSupport.ReadPixels(actual));
                return actual;
            });
        var result = await service.ExportBatchAsync([file], new ExportSettings
        {
            OutputFolder = Path.Combine(folder.Path, "export"), Format = ExportFormat.Jpeg,
            ExportHiRes = true, ExportWeb = false, ExportSmall = false, OutputSharpening = OutputSharpeningMode.Off
        });
        Assert.True(called); Assert.Equal(1, result.ExportedCount);
    }

    [Fact]
    public void StandardThumbnailRendersRepairsButRawFallbackIgnoresThem()
    {
        using var basis = RenderRepairsTests.Fixture();
        using var encoded = new RenderPipeline().Render(new(basis, new(), RenderIntent.Preview, null, new(false, false)));
        using var source = BitmapConversionService.ConvertToBitmap(encoded.Image)!;
        var settings = Settings(); var neutral = settings.Clone(); neutral.Repairs = null;
        var renderer = new ThumbnailRenderer(new RenderPipeline());
        using var standard = renderer.RenderStandardEdits(source, settings, 160);
        using var standardOff = renderer.RenderStandardEdits(source, neutral, 160);
        Assert.NotEqual(BitmapConversionService.CopyBgraPixels(standardOff), BitmapConversionService.CopyBgraPixels(standard));
        using var raw = renderer.RenderRawGeometry(source, settings, 160);
        using var rawOff = renderer.RenderRawGeometry(source, neutral, 160);
        Assert.Equal(BitmapConversionService.CopyBgraPixels(rawOff), BitmapConversionService.CopyBgraPixels(raw));
        // Geometry-only ignores even an invalid repair, proving the stage is never invoked.
        settings.Repairs![0].Radius = double.NaN;
        using var ignored = renderer.RenderRawGeometry(source, settings, 160);
        Assert.Equal(BitmapConversionService.CopyBgraPixels(rawOff), BitmapConversionService.CopyBgraPixels(ignored));
    }

    private sealed class Loader : IBaseImageLoader
    {
        public bool CanLoad(ImageFile file) => true;
        public BaseImageLoadOutcome LoadPreviewBaseWithOutcome(ImageFile file, BaseDecodeSettings decode, CancellationToken token) =>
            BaseImageLoadOutcome.Loaded(new PreviewBasePair(RenderRepairsTests.Fixture(), RenderRepairsTests.Fixture()));
        public BaseImage LoadFullBase(ImageFile file, BaseDecodeSettings decode, CancellationToken token) => RenderRepairsTests.Fixture();
    }
}
