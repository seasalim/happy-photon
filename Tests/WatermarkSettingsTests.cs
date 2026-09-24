using HappyPhoton.Models;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class WatermarkSettingsTests
{
    [Fact]
    public void SettingsAggregateChangesStripLineBreaksAndFreezeSnapshots()
    {
        var settings = new ExportSettings { OutputFolder = "finished" };
        Assert.Null(settings.SnapshotOutput().Watermark);
        var changes = new List<string?>();
        settings.PropertyChanged += (_, args) => changes.Add(args.PropertyName);
        settings.Watermark.Text = "Jane\r\nDoe";
        Assert.Equal("JaneDoe", settings.Watermark.Text);
        Assert.Equal(1, changes.Count(name => name == nameof(ExportSettings.Watermark)));
        settings.Watermark.Enabled = true;
        var frozen = settings.SnapshotOutput();
        settings.Watermark.Text = "Changed";
        Assert.Equal("JaneDoe", frozen.Watermark!.Text);
        Assert.Equal(new WatermarkSpec("JaneDoe"), frozen.Watermark);
        settings.Watermark.Text = " ";
        Assert.Equal("Enter watermark text.", settings.ValidationReason);
        Assert.Throws<InvalidOperationException>(() => settings.CreateJob([]));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FlatSettingsRoundTripIncludingDisabledValues(bool preferencesOnly)
    {
        using var root = new TemporaryDirectory();
        using var catalog = new CatalogService(root.Path);
        await catalog.InitializeAsync();
        var service = new AppSettingsService(catalog);
        var spec = new WatermarkSpec("© Jane Doe", "Saved missing family", true, true, 2.5,
            WatermarkColor.Black, 55, WatermarkEdge.Left, WatermarkAlignment.Middle, false, 4.5);
        foreach (var enabled in new[] { true, false })
        {
            var settings = new AppSettings { Watermark = spec, WatermarkEnabled = enabled };
            if (preferencesOnly) await service.SavePreferencesAsync(settings);
            else await service.SaveAsync(settings);
            var loaded = await service.LoadAsync();
            Assert.Equal(enabled, loaded.WatermarkEnabled);
            Assert.Equal(spec, loaded.Watermark);
            Assert.Equal("2.5", await catalog.GetAppSettingAsync("WatermarkSize"));
        }
    }

    [Theory]
    [InlineData("garbage")]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("-1")]
    [InlineData("101")]
    public async Task InvalidPersistedValuesUseDefaults(string invalid)
    {
        using var root = new TemporaryDirectory();
        using var catalog = new CatalogService(root.Path);
        await catalog.InitializeAsync();
        foreach (var name in new[] { "Enabled", "Bold", "Italic", "RotateAlongEdge", "Size",
                     "Color", "Opacity", "Edge", "Alignment", "Margin" })
            await catalog.SetAppSettingAsync("Watermark" + name, invalid);
        var loaded = await new AppSettingsService(catalog).LoadAsync();
        Assert.False(loaded.WatermarkEnabled);
        Assert.Equal(new WatermarkSpec(""), loaded.Watermark);
    }

    [Fact]
    public async Task BoundsAreInclusiveAndMissingKeysUseDefaults()
    {
        using var root = new TemporaryDirectory();
        using var catalog = new CatalogService(root.Path);
        await catalog.InitializeAsync();
        var service = new AppSettingsService(catalog);
        Assert.Equal(new WatermarkSpec(""), (await service.LoadAsync()).Watermark);
        foreach (var spec in new[] { new WatermarkSpec("", Size: 1, Opacity: 5, Margin: 0),
                     new WatermarkSpec("", Size: 20, Opacity: 100, Margin: 20) })
        {
            await service.SaveAsync(new AppSettings { Watermark = spec });
            Assert.Equal(spec, (await service.LoadAsync()).Watermark);
        }
        await catalog.SetAppSettingAsync("WatermarkText", "a\r\nb");
        await catalog.SetAppSettingAsync("WatermarkEdge", "left");
        Assert.Equal("ab", (await service.LoadAsync()).Watermark.Text);
        Assert.Equal(WatermarkEdge.Left, (await service.LoadAsync()).Watermark.Edge);
    }
}
