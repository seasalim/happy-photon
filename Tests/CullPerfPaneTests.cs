using System.Diagnostics;
using System.Reflection;
using Avalonia;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

[Collection(AvaloniaTestCollection.Name)]
public sealed class CullPerfPaneTests
{
    [WindowsTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MatchedCacheCompletionOnlySamplesAcceptedPane(bool superseded)
    {
        using var directory = new TemporaryDirectory();
        using var catalog = new CatalogService(directory.Path);
        await catalog.InitializeAsync();
        var image = new ImageFile(Path.Combine(directory.Path, "first.jpg"));
        TestImages.WriteJpeg(image.FilePath);
        await image.EnsureCatalogIdAsync(catalog);
        await using (var cache = new PreviewCacheService(catalog))
        {
            using var pixels = new MagickImage(MagickColors.Gray, 48, 32);
            cache.QueueSaveToCache(image, pixels, RenderSettingsHash.Compute(image.EditSettings),
                new PreviewCacheIdentity(new PixelSize(48, 32), new PixelSize(48, 32)));
        }
        await using var vm = new MainWindowViewModel(catalog, baseLoader: null,
            loadMetadataAsync: _ => Task.CompletedTask,
            availabilityService: new TestSourceAvailabilityService(SourceAvailability.AvailableLocally),
            timeProvider: new TestTimeProvider());
        vm.SelectedImage = image;
        var recorder = new CullPerfRecorder();
        vm.ImageService.Previews.CullPerf = recorder;
        var input = new CullPerfSubmission(1, Stopwatch.GetTimestamp(), image.CatalogId, false);
        var operation = recorder.Record("Receipt", operation: -1);
        var generation = vm.LatestPreviewOutcomeGeneration;
        recorder.Record("Selection", image.CatalogId, generation, operation);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        vm.ImageService.Previews.CachedPreviewGateAsync = () =>
        {
            entered.TrySetResult();
            return release.Task;
        };
        var pane = new ComparePaneViewModel(image);
        // Let the cache request finish even after its pane becomes inactive,
        // as a decode already past its cancellation check can do.
        var load = (Task)typeof(MainWindowViewModel).GetMethod("LoadPreviewPaneAsync",
            BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(vm,
                [pane, (Func<bool>)(() => ReferenceEquals(vm.SelectedImage, image)), CancellationToken.None])!;
        try
        {
            await entered.Task.WaitAsync(TestWaits.Condition);
            if (superseded) vm.SelectedImage = null;
            release.TrySetResult();
            await load.WaitAsync(TestWaits.Condition);
            var events = recorder.Snapshot();
            var accounting = CullPerfLedger.Reconcile([input], events, matchedCache: true);
            Assert.Empty(accounting.Failures);
            Assert.Equal(superseded ? 1 : 0, accounting.Superseded);
            Assert.Equal(superseded ? 0 : 1, accounting.Completed);
            if (superseded)
            {
                Assert.Null(pane.Preview);
                Assert.Equal(generation, Assert.Single(events, item => item.Kind == "PaneSuperseded").Generation);
                Assert.DoesNotContain(events, item => item.Kind == "MatchedCacheReady");
                Assert.False(accounting.Samples.ContainsKey("accurate-ready-ms"));
                Assert.False(accounting.Samples.ContainsKey("matched-preview-ms"));
            }
            else
            {
                Assert.NotNull(pane.Preview);
                Assert.Single(accounting.Samples["accurate-ready-ms"]);
                Assert.Single(accounting.Samples["matched-preview-ms"]);
            }
        }
        finally
        {
            release.TrySetResult();
            await load.WaitAsync(TestWaits.Condition);
            pane.Preview?.Dispose();
        }
    }
}