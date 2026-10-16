using System.Text.Json.Nodes;
using HappyPhoton.Models;
using HappyPhoton.Services;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class PreviewCacheGenerationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ChangedGenerationRejectsReadEvenWithIdenticalSettings(bool removeSidecar)
    {
        using var directory = new TemporaryDirectory();
        var source = Path.Combine(directory.Path, "source.jpg");
        await File.WriteAllBytesAsync(source, []);
        File.SetLastWriteTimeUtc(source, DateTime.UtcNow.AddMinutes(-1));
        using var catalog = new CatalogService(Path.Combine(directory.Path, "catalog"));
        await using var cache = new PreviewCacheService(catalog);
        var file = new ImageFile(source) { CatalogId = 1 };
        using var before = new MagickImage(MagickColors.Gray, 8, 12);
        using var after = new MagickImage(MagickColors.White, 8, 12);
        Assert.True(await cache.QueueSaveToCache(file, before, "same-settings", default)
            .WaitAsync(TestWaits.Condition));
        var changed = false;
        cache.AfterMetadataReadForTesting = () =>
        {
            if (removeSidecar)
            {
                File.Delete(cache.GetMetadataPath(file));
                changed = true;
            }
            else
            {
                changed = cache.QueueSaveToCache(file, after, "same-settings", default)
                    .WaitAsync(TestWaits.Condition).GetAwaiter().GetResult();
            }
        };

        using var loaded = await Task.Run(() => cache.LoadRenderedPreview(file))
            .WaitAsync(TestWaits.Condition);
        Assert.True(changed);
        Assert.Null(loaded);
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(2, true)]
    public async Task UnverifiableSidecarsStayStaleUntilRewritten(int version, bool emptyGeneration)
    {
        using var directory = new TemporaryDirectory();
        var source = Path.Combine(directory.Path, "source.jpg");
        await File.WriteAllBytesAsync(source, []);
        File.SetLastWriteTimeUtc(source, DateTime.UtcNow.AddMinutes(-1));
        using var catalog = new CatalogService(Path.Combine(directory.Path, "catalog"));
        await using var cache = new PreviewCacheService(catalog);
        var file = new ImageFile(source) { CatalogId = 1 };
        using var pixels = new MagickImage(MagickColors.Gray, 8, 12);
        Assert.True(await cache.QueueSaveToCache(file, pixels, "settings", default)
            .WaitAsync(TestWaits.Condition));
        var metadataPath = cache.GetMetadataPath(file);
        var metadata = JsonNode.Parse(await File.ReadAllTextAsync(metadataPath))!.AsObject();
        metadata["Version"] = version;

        if (emptyGeneration)
        {
            metadata["Generation"] = Guid.Empty;
        }
        else
        {
            metadata.Remove("Generation");
        }

        await File.WriteAllTextAsync(metadataPath, metadata.ToJsonString());
        Assert.False(cache.HasSettingsMatchedEntry(file, "settings"));
        using var stale = cache.LoadRenderedPreview(file);
        Assert.NotNull(stale);
        Assert.Null(stale.SettingsHash);
        Assert.Null(stale.OriginalViewPixelSize);
        Assert.Null(stale.OriginalImagePixelSize);
        Assert.True(await cache.QueueSaveToCache(file, pixels, "settings", default)
            .WaitAsync(TestWaits.Condition));
        using var current = cache.LoadRenderedPreview(file);
        Assert.NotNull(current);
        Assert.Equal("settings", current.SettingsHash);
        Assert.True(cache.HasSettingsMatchedEntry(file, "settings"));
    }
}
