using System.Text.Json;
using HappyPhoton.Models;
using HappyPhoton.Services;
using ImageMagick;
using Xunit;
using static HappyPhoton.Tests.RawOrientationMeasureSupport;

namespace HappyPhoton.Tests;

[Collection(AvaloniaTestCollection.Name)]
public sealed class RawOrientationStateBaselineTests(ITestOutputHelper output)
{
    [Fact]
    public void O1Thumbnail()
    {
        Require();
        using var directory = new TemporaryDirectory();
        var original = SyncSpotGateSupport.Fixture();
        var one = SyncSpotRotatedFixture.Create(original, directory.Path, 1);
        var three = SyncSpotRotatedFixture.Create(original, directory.Path, 3);
        var extractor = new EmbeddedPreviewExtractor(new LibRawProcessingService());
        using var a = extractor.TryExtract(one, 150, CancellationToken.None);
        using var b = extractor.TryExtract(three, 150, CancellationToken.None);
        Assert.NotNull(a);
        Assert.NotNull(b);
        using var ai = BitmapConversionService.ConvertToMagickImage(a);
        using var bi = BitmapConversionService.ConvertToMagickImage(b);
        using var expected = Transform(ai, 3);
        var same = Difference(ai, bi, true);
        var rotated = Difference(expected, bi, true);
        Record("O1-thumbnail", new { width = bi.Width, height = bi.Height,
            sameMax8 = same.Max, rotatedMax8 = rotated.Max,
            sameMean8 = same.Mean, rotatedMean8 = rotated.Mean,
            headerOrientation = ImageServiceHelpers.GetExifOrientation(three),
            orientation = rotated.Mean < same.Mean ? "closest to 180 degrees" : "closest to identity" }, output);
    }

    [Fact]
    public async Task O4cPersistentCache()
    {
        Require();
        var statePath = Path.Combine(Root, "cache-state.json");
        CacheState state;

        if (Freeze && !File.Exists(statePath))
        {
            var directory = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(),
                "happy-photon-318-cache-" + Guid.NewGuid().ToString("N"))).FullName;
            var path = SyncSpotRotatedFixture.Create(SyncSpotGateSupport.Fixture(), directory, 3);
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(-5));
            state = new(path, 0, BaseImage.Version, RenderSettingsHash.Compute(new EditSettings()));
        }
        else
        {
            state = JsonSerializer.Deserialize<CacheState>(File.ReadAllText(statePath))!;
        }

        SyncProfileGateSupport.RequireLocal(state.SourcePath);
        var catalogPath = Path.Combine(Root, "cache");
        using var catalog = new CatalogService(catalogPath);
        await catalog.InitializeAsync();
        var file = new ImageFile(state.SourcePath) { CatalogId = state.CatalogId };
        var settings = new EditSettings();
        var hash = RenderSettingsHash.Compute(settings);

        if (Freeze)
        {
            file.CatalogId = await catalog.GetOrCreateImageAsync(state.SourcePath);
            state = state with { CatalogId = file.CatalogId };
            using var basis = Load(state.SourcePath, true, BaseDecodeSettings.From(settings));
            using var rendered = Render(basis, RenderIntent.Preview, settings);
            await using (var previewWriter = new PreviewCacheService(catalog))
            {
                Assert.True(await previewWriter.QueueSaveToCache(file, rendered.Image, hash, default)
                    .WaitAsync(TestWaits.Condition));
            }

            using var thumbnail = (MagickImage)rendered.Image.Clone();
            thumbnail.Resize(300, 300);
            using var bitmap = BitmapConversionService.ConvertToBitmap(thumbnail);
            Assert.NotNull(bitmap);

            await using (var thumbnailWriter = new RenderedThumbnailCacheService(catalog))
            {
                thumbnailWriter.QueueSaveToCache(file, bitmap, hash, File.GetLastWriteTimeUtc(file.FilePath));
            }

            File.WriteAllText(statePath, JsonSerializer.Serialize(state,
                new JsonSerializerOptions { WriteIndented = true }));
        }

        await using var preview = new PreviewCacheService(catalog);
        await using var thumb = new RenderedThumbnailCacheService(catalog);
        using var previewHit = preview.LoadRenderedPreview(file);
        using var thumbHit = thumb.LoadMatching(file, hash);
        var previewMatches = preview.HasSettingsMatchedEntry(file, hash);
        Record("O4c", new { frozenBaseVersion = state.BaseVersion, currentBaseVersion = BaseImage.Version,
            decodeKey = BaseDecodeSettings.From(settings).CacheKey, frozenHash = state.SettingsHash, currentHash = hash,
            previewHit = previewHit != null && previewMatches, thumbnailHit = thumbHit != null,
            previewPath = preview.GetCachePath(file), thumbnailPath = catalog.GetRenderedThumbnailPath(file.CatalogId),
            sourcePath = state.SourcePath, state.CatalogId }, output);
    }

    [Fact]
    public async Task O5ExistingDocument()
    {
        Require();
        var documentPath = Path.Combine(Root, "existing-document.json");

        if (Freeze)
        {
            Assert.False(File.Exists(documentPath));
            var initial = new EditSettings
            {
                Crop = new CropRegion { Left = .125, Top = .1875, Right = .875, Bottom = .9375 },
                HorizonRotation = 3.25,
                Geometry = new GeometrySettings { Vertical = 17, Horizontal = -23, Aspect = 11, Distortion = -9 },
                Locals = [new LocalAdjustment { Id = 31801.ToString("x32"), Type = "radial",
                    Cu = .3125, Cv = .625, Rx = .1875, Ry = .25, Exposure = .5 }],
                Repairs = [new Repair { Id = 31802.ToString("x32"), Type = "clone",
                    U = .25, V = .375, Su = .6875, Sv = .625, Radius = .015625 }]
            };
            File.WriteAllText(documentPath, EditSettingsJson.Serialize(initial));
        }

        var stored = File.ReadAllText(documentPath);
        var edits = EditSettingsJson.Deserialize(stored, out var migrated);
        using var fixture = new CatalogVmFixture("318-document");
        var path = SyncSpotRotatedFixture.Create(SyncSpotGateSupport.Fixture(), fixture.Root, 3);
        using var catalog = await fixture.CreateCatalogAsync("catalog");
        var id = await catalog.GetOrCreateImageAsync(path);
        await catalog.SaveEditSettingsAsync(id, edits);
        var loaded = (await catalog.LoadImageStatesAsync([path]))[path].Single().EditSettings;
        using var basis = Load(path, true);
        using var rendered = Render(basis, RenderIntent.Preview, loaded);
        Record("O5", new { loaded = true, rendered = true, migrated,
            unchanged = stored == EditSettingsJson.Serialize(loaded), documentSha256 = Hash(documentPath),
            width = rendered.Image.Width, height = rendered.Image.Height, storedCoordinates = stored }, output);
    }

    private sealed record CacheState(string SourcePath, long CatalogId, int BaseVersion, string SettingsHash);
}
