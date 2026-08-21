using System.Diagnostics;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using Xunit;

namespace HappyPhoton.Tests;

internal static class DevelopEntryPerformanceMeasurement
{
    public static async Task<(TimeSpan Latency, int RenderCount)> MeasureAsync(
        CatalogService catalog,
        ImageFile file)
    {
        var viewModel = new MainWindowViewModel(
            catalog,
            new BaseLoaderRouter(new RawBaseLoader(), new StandardBaseLoader()),
            loadMetadataAsync: _ => Task.CompletedTask)
        {
            IsDevelopMode = true
        };
        var renderCount = 0;
        viewModel.ImageService.Previews.RenderStarted += () =>
            Interlocked.Increment(ref renderCount);
        var stopwatch = Stopwatch.StartNew();
        viewModel.SelectedImage = file;
        await TestWaits.UntilAsync(() =>
            viewModel.PreviewImage != null && viewModel.Histogram != null);
        stopwatch.Stop();
        Assert.Equal(1, Volatile.Read(ref renderCount));
        await viewModel.DisposeAsync();
        return (stopwatch.Elapsed, renderCount);
    }
}
