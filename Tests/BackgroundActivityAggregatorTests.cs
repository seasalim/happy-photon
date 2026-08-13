using HappyPhoton.ViewModels;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class BackgroundActivityAggregatorTests
{
    [Fact]
    public void Aggregate_UsesPriorityOverflowAndMetadataSuppression()
    {
        var aggregator = new BackgroundActivityAggregator();
        var now = DateTimeOffset.UtcNow;
        var snapshot = new BackgroundActivitySnapshot(
            ThumbnailCount: 3,
            PreviewCount: 1,
            CacheWriteCount: 2,
            MetadataCount: 4,
            CaptureTimes: new BackgroundProgress(12, 40),
            Export: new ExportActivitySnapshot(1, 0, 5));

        aggregator.Aggregate(snapshot, now, 1);
        var display = aggregator.Aggregate(
            snapshot,
            now + BackgroundActivityAggregator.ShowDelay,
            1);

        Assert.True(display.IsVisible);
        Assert.Equal("Exporting — preparing 5 photos +4", display.Label);
        Assert.DoesNotContain("metadata", display.Tooltip, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(5, display.ActiveKindCount);
        Assert.True(display.ShowProgress);
        Assert.Equal(0, display.ProgressValue);
        Assert.Equal(5, display.ProgressMaximum);
    }

    [Fact]
    public void Aggregate_FormatsBurstProgressAndNonTotalWork()
    {
        var now = DateTimeOffset.UtcNow;
        var burst = Visible(new BackgroundActivitySnapshot(
            0, 0, 0, 8, new BackgroundProgress(240, 1200), null), now);

        Assert.Equal("Capture times — 240 / 1,200", burst.Label);
        Assert.True(burst.ShowProgress);
        Assert.Equal(240, burst.ProgressValue);
        Assert.Equal(1200, burst.ProgressMaximum);

        var thumbnails = Visible(new BackgroundActivitySnapshot(
            2, 0, 0, 0, null, null), now);
        Assert.Equal("Loading thumbnails", thumbnails.Label);
        Assert.False(thumbnails.ShowProgress);
    }

    [Fact]
    public void Aggregate_AppliesShowHideHysteresisAndEpochGuard()
    {
        var aggregator = new BackgroundActivityAggregator();
        var now = DateTimeOffset.UtcNow;
        var busy = BackgroundActivitySnapshot.Empty with { PreviewCount = 1 };

        Assert.False(aggregator.Aggregate(busy, now, 1).IsVisible);
        Assert.False(aggregator.Aggregate(
            busy,
            now + TimeSpan.FromMilliseconds(399),
            1).IsVisible);
        Assert.True(aggregator.Aggregate(
            busy,
            now + TimeSpan.FromMilliseconds(400),
            1).IsVisible);

        var quiet = now + TimeSpan.FromSeconds(1);
        Assert.True(aggregator.Aggregate(
            BackgroundActivitySnapshot.Empty,
            quiet,
            1).IsVisible);
        Assert.True(aggregator.Aggregate(
            BackgroundActivitySnapshot.Empty,
            quiet + TimeSpan.FromMilliseconds(599),
            1).IsVisible);
        Assert.False(aggregator.Aggregate(
            BackgroundActivitySnapshot.Empty,
            quiet + TimeSpan.FromMilliseconds(600),
            1).IsVisible);
        Assert.True(aggregator.CanStop(
            BackgroundActivitySnapshot.Empty,
            quiet + TimeSpan.FromMilliseconds(600),
            1));

        aggregator.Aggregate(
            BackgroundActivitySnapshot.Empty,
            quiet + TimeSpan.FromMilliseconds(700),
            2);
        Assert.False(aggregator.CanStop(
            BackgroundActivitySnapshot.Empty,
            quiet + TimeSpan.FromMilliseconds(700),
            2));
    }

    private static BackgroundActivityDisplay Visible(
        BackgroundActivitySnapshot snapshot,
        DateTimeOffset now)
    {
        var aggregator = new BackgroundActivityAggregator();
        aggregator.Aggregate(snapshot, now, 1);
        return aggregator.Aggregate(
            snapshot,
            now + BackgroundActivityAggregator.ShowDelay,
            1);
    }
}
