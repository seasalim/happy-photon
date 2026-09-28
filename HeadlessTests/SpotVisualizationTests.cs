using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using HappyPhoton.Services;
using HappyPhoton.Views;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class SpotVisualizationTests
{
    [AvaloniaFact]
    public void HighPassShowsSpecksAndOnlyCopiesTheVisiblePixels()
    {
        using var image = new MagickImage(MagickColors.Gray, 100, 80);
        using (var pixels = image.GetPixels()) pixels.SetPixel(50, 40, [0, 0, 0]);
        using var displayed = BitmapConversionService.ConvertToBitmap(image)!;
        var owned = ZoomPanControl.CopySpotViewport(displayed, new Rect(.25, .25, .5, .5), out var rect);
        Assert.Equal(new PixelRect(25, 20, 50, 40), rect);
        Assert.Equal(50 * 40 * 4, owned.Length);
        using var result = SpotVisualizationRenderer.Render(owned, rect.Width, rect.Height, 50);
        var codes = BitmapConversionService.CopyBgraPixels(result);
        Assert.Equal(0, codes[0]);
        Assert.Equal(255, codes[(20 * 50 + 25) * 4]);
        Assert.Equal(255, codes[3]);
        Assert.Equal(new PixelSize(50, 40), result.PixelSize);
    }

    [AvaloniaTheory]
    [InlineData("bitmap")]
    [InlineData("navigation")]
    [InlineData("zoom")]
    [InlineData("threshold")]
    [InlineData("detach")]
    public async Task PendingWorkCannotPublishAfterInvalidation(string change)
    {
        using var image = new MagickImage(MagickColors.Gray, 320, 240);
        using var first = BitmapConversionService.ConvertToBitmap(image)!;
        using var second = BitmapConversionService.ConvertToBitmap(image)!;
        var viewer = new ZoomPanControl { Source = first, IsSpotsMode = true, VisualizeSpots = true };
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        var calls = 0;
        viewer.VisualizeRenderer = (pixels, width, height, threshold) =>
        {
            if (Interlocked.Increment(ref calls) == 1)
            {
                entered.TrySetResult();
                Assert.True(release.Wait(TestWaits.Condition));
            }

            return SpotVisualizationRenderer.Render(pixels, width, height, threshold);
        };
        using var scope = new TestUiScope(new Window { Width = 220, Height = 180, Content = viewer });

        try
        {
            await entered.Task.WaitAsync(TestWaits.Condition);
            var pending = viewer.VisualizeWork;

            switch (change)
            {
                case "bitmap":
                    viewer.Source = second;
                    break;

                case "navigation":
                    viewer.SourceIdentity = new object();
                    viewer.Source = null;
                    break;

                case "zoom":
                    viewer.ZoomLevel = 2;
                    break;

                case "threshold":
                    viewer.SpotVisualizeThreshold = 90;
                    break;

                case "detach":
                    scope.Window!.Content = null;
                    break;
            }

            Assert.Null(viewer.VisualizeBitmap);
            viewer.Source = change is "navigation" or "detach" ? null : second;
            first.Dispose();
            release.Set();
            await pending.WaitAsync(TestWaits.Condition);
            Assert.Null(viewer.VisualizeBitmap);

            if (change is "navigation" or "detach")
            {
                Dispatcher.UIThread.RunJobs();
                await viewer.VisualizeWork;
                Assert.Null(viewer.VisualizeBitmap);
            }
            else
            {
                await TestWaits.UntilAsync(() => viewer.VisualizeBitmap != null);
                Assert.True(calls >= 2);
            }
        }
        finally
        {
            release.Set();
            await viewer.VisualizeWork.WaitAsync(TestWaits.Condition);
        }
    }
}
