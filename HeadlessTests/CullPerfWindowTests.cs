using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class CullPerfWindowTests
{
    [AvaloniaFact]
    public async Task QualifiedLoupeFrames()
    {
        Assert.SkipWhen(Environment.GetEnvironmentVariable("CULL_PERF_RUN") == null,
            "Use scripts/cull-perf.ps1 for isolated qualification.");
        using var directory = new TemporaryDirectory();
        using var catalog = new CatalogService(directory.Path);
        await catalog.InitializeAsync();
        var images = new[] { "first.jpg", "second.jpg" }.Select(name =>
        {
            var path = Path.Combine(directory.Path, name);
            TestImages.WriteJpeg(path);
            return new ImageFile(path) { Thumbnail = new Bitmap(path) };
        }).ToArray();
        foreach (var image in images) await image.EnsureCatalogIdAsync(catalog);
        var loader = new GatedPairLoader();
        var recorder = new CullPerfRecorder();
        await using var vm = new MainWindowViewModel(catalog, loader,
            loadMetadataAsync: _ => Task.CompletedTask,
            availabilityService: new TestSourceAvailabilityService(SourceAvailability.AvailableLocally),
            timeProvider: new TestTimeProvider());
        vm.ShowWorkspaceReady(MainWindowViewModel.CurrentFirstRunExperienceVersion);
        vm.Browse.SetImages(images);
        vm.SelectedImage = images[0];
        vm.ImageService.Previews.CullPerf = recorder;
        var window = new MainWindow { Width = 1024, Height = 768 };
        using var scope = TestUiScope.ForMainWindow(window, vm);
        var samples = new Dictionary<string, CullPerfSample[]>();
        var frames = new List<object>();
        try
        {
            vm.EnterLoupeCommand.Execute(null);
            loader.Release.Set();
            await vm.LoupeLoadingTask.WaitAsync(TestWaits.Condition);
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            using (var initial = window.CaptureRenderedFrame()) Assert.NotNull(initial);
            loader.Release.Reset();
            loader.DecodeStarted.Reset();
            var submitted = Stopwatch.GetTimestamp();
            vm.SelectNextImageCommand.Execute(null);
            // Wait on the loader's signal off the UI thread: polling would add a timer tick
            // to the measured span, and blocking here could stall the decode's scheduling.
            Assert.True(await Task.Run(() => loader.DecodeStarted.Wait(TestWaits.Condition)),
                "The gated decode did not start.");
            var loupe = Assert.Single(window.GetVisualDescendants().OfType<LoupeView>());
            var placeholder = Assert.Single(loupe.GetVisualDescendants().OfType<Image>(),
                image => ReferenceEquals(image.Source, images[1].Thumbnail));
            Dispatcher.UIThread.RunJobs();
            Assert.True(placeholder.IsEffectivelyVisible);
            Assert.Null(vm.LoupePane!.Preview);
            using var placeholderFrame = Capture("placeholder", submitted);
            Assert.Same(images[1].Thumbnail, placeholder.Source);
            Assert.True(placeholder.Bounds.Width > 0 && placeholder.Bounds.Height > 0);
            var point = placeholder.TranslatePoint(new Point(
                placeholder.Bounds.Width / 2, placeholder.Bounds.Height / 3), window)!.Value;
            var probeStart = Stopwatch.GetTimestamp();
            var probe = new TaskCompletionSource<long>(TaskCreationOptions.RunContinuationsAsynchronously);
            Dispatcher.UIThread.Post(() => probe.SetResult(Stopwatch.GetTimestamp()), DispatcherPriority.Background);
            var probeEnd = await probe.Task.WaitAsync(TestWaits.Condition);
            samples["dispatcher-ms"] = [new(1, Stopwatch.GetElapsedTime(probeStart, probeEnd).TotalMilliseconds)];
            loader.Release.Set();
            await vm.LoupeLoadingTask.WaitAsync(TestWaits.Condition);
            Dispatcher.UIThread.RunJobs();
            Assert.NotNull(vm.LoupePane?.Preview);
            Assert.False(placeholder.IsVisible);
            using var previewFrame = Capture("preview", submitted);
            // Pixel inspection and PNG writes stay outside both measured frame spans.
            VerifyThumbnail(placeholderFrame, point);
            var shots = Path.Combine(CullPerfFiles.Root, "artifacts", "shots");
            Directory.CreateDirectory(shots);
            placeholderFrame.Save(Path.Combine(shots, "cullperf-loupe-placeholder.png"));
            previewFrame.Save(Path.Combine(shots, "cullperf-loupe-preview.png"));
            var destination = Path.Combine(CullPerfFiles.Run, "window-loupe");
            Directory.CreateDirectory(destination);
            var fixtureManifest = CullPerfFiles.Read<FixtureManifest>(Path.Combine(CullPerfFiles.Run, "fixtures.json"));
            CullPerfFiles.WriteNew(Path.Combine(destination, "frames.json"), new { frames, events = recorder.Snapshot() });
            CullPerfFiles.WriteNew(Path.Combine(destination, "fragment.json"), new CullPerfFragment(
                "window-loupe", CullPerfFiles.Hash(CullPerfFiles.GatePath),
                Environment.GetEnvironmentVariable("CULL_PERF_MACHINE") ?? "", fixtureManifest.Hashes,
                "blocked-source", true, false, recorder.LostEvents, 1, 1, 0, 0, 0, [], samples, []));
        }
        finally
        {
            loader.Release.Set();
            scope.Dispose();
        }

        Bitmap Capture(string kind, long start)
        {
            // Read the first frame after the change. CaptureRenderedFrame keeps running jobs
            // and rendering until the dispatcher is idle, so it would also wait out unrelated
            // transitions, such as the loupe-hidden browse tiles restyled by each selection.
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            var frame = window.GetLastRenderedFrame();
            Assert.NotNull(frame);
            Assert.True(frame.PixelSize.Width > 0 && frame.PixelSize.Height > 0);
            var timestamp = Stopwatch.GetTimestamp();
            frames.Add(new { kind, timestamp, width = frame.PixelSize.Width, height = frame.PixelSize.Height });
            samples[kind + "-frame-ms"] = [new(1, Stopwatch.GetElapsedTime(start, timestamp).TotalMilliseconds)];
            return frame;
        }

        void VerifyThumbnail(Bitmap frame, Point point)
        {
            using var encoded = new MemoryStream();
            frame.Save(encoded);
            using var pixels = new ImageMagick.MagickImage(encoded.ToArray());
            using var expected = new ImageMagick.MagickImage(images[1].FilePath);
            using var actualPixels = pixels.GetPixels();
            using var expectedPixels = expected.GetPixels();
            var actual = actualPixels.GetPixel((int)(point.X * window.RenderScaling),
                (int)(point.Y * window.RenderScaling)).ToColor()!;
            var resident = expectedPixels.GetPixel(0, 0).ToColor()!;
            Assert.InRange(Math.Abs((int)actual.R - resident.R), 0, 512);
            Assert.InRange(Math.Abs((int)actual.G - resident.G), 0, 512);
            Assert.InRange(Math.Abs((int)actual.B - resident.B), 0, 512);
        }
    }

    private sealed record FixtureManifest(Dictionary<string, string> Hashes);
}
