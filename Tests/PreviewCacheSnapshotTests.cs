using Avalonia;
using HappyPhoton.Models;
using HappyPhoton.Services;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class PreviewCacheSnapshotTests(ITestOutputHelper output)
{
    public static IEnumerable<object[]> Cases =>
    [
        new object[] { 1, false },
        new object[] { 1, true }
    ];

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task ReadReturnsOneCompleteGeneration(int iteration, bool replaceAfterMetadata)
    {
        using var directory = new TemporaryDirectory();
        var source = Path.Combine(directory.Path, "source.jpg");
        await File.WriteAllBytesAsync(source, []);
        File.SetLastWriteTimeUtc(source, DateTime.UtcNow.AddMinutes(-1));
        using var catalog = new CatalogService(Path.Combine(directory.Path, "catalog"));
        await using var cache = new PreviewCacheService(catalog);
        var file = new ImageFile(source) { CatalogId = 1 };
        using var before = new MagickImage(MagickColors.Gray, 8, 12);
        using var after = new MagickImage(new MagickColor("#b0b0b0"), 16, 12);
        var original = new PreviewCacheIdentity(new PixelSize(8, 12), new PixelSize(16, 12));
        var edited = new PreviewCacheIdentity(new PixelSize(16, 12), new PixelSize(16, 12));
        Assert.True(await cache.QueueSaveToCache(file, before, "original", original)
            .WaitAsync(TestWaits.Condition));
        bool? replacementPersisted = null;

        if (replaceAfterMetadata)
        {
            cache.AfterMetadataReadForTesting = () =>
            {
                replacementPersisted = cache.QueueSaveToCache(file, after, "edited", edited)
                    .WaitAsync(TestWaits.Condition).GetAwaiter().GetResult();
            };
        }

        using var loaded = await Task.Run(() => cache.LoadRenderedPreview(file))
            .WaitAsync(TestWaits.Condition);
        if (replaceAfterMetadata) Assert.True(replacementPersisted);
        if (!replaceAfterMetadata) Assert.NotNull(loaded);
        if (loaded == null) return;

        using var bitmap = BitmapConversionService.ConvertToBitmap(loaded.Image);
        var luma = BitmapConversionService.EstimateMeanLuma(bitmap!);
        output.WriteLine($"iteration={iteration} replace={replaceAfterMetadata} hash={loaded.SettingsHash} " +
            $"identity={loaded.OriginalViewPixelSize} pixels={loaded.Image.Width}x{loaded.Image.Height} luma={luma:R}");
        Assert.Contains(loaded.SettingsHash, new[] { "original", "edited" });
        var expectedWidth = loaded.SettingsHash == "original" ? 8u : 16u;
        var expectedLuma = loaded.SettingsHash == "original" ? 128d / 255 : 176d / 255;
        Assert.Equal(expectedWidth, loaded.Image.Width);
        Assert.Equal((int)expectedWidth, loaded.OriginalViewPixelSize!.Value.Width);
        Assert.Equal(new PixelSize(16, 12), loaded.OriginalImagePixelSize);
        Assert.Equal(expectedLuma, luma, 3);
    }
}
