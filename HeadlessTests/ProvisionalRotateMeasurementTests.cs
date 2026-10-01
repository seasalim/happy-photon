using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

// Frozen WP2 workload. Select baseline/post with HAPPY_PHOTON_ROTATE_MEASURE.
// Both modes use identical surfaces, command series, trace hooks and timestamps.
public sealed class ProvisionalRotateMeasurementTests(ITestOutputHelper output)
{
    private const int WarmupPresses = 8;
    private const int SampleCount = 50;

    [AvaloniaFact]
    public async Task R1HeldDecodeInstantTurn()
    {
        var post = RequireMode();
        using var fixture = new CatalogVmFixture("rotate-r1");
        using var catalog = await fixture.CreateCatalogAsync();
        var image = await SeedAsync(fixture, catalog, 64, 48);
        await using var vm = CreateViewModel(catalog);
        var release = NewSignal();
        var entered = NewSignal();
        vm.ImageService.Previews.SourceWorkGateAsync = () =>
        {
            entered.TrySetResult();

            return release.Task;
        };

        try
        {
            vm.SelectedImage = image;
            await TestWaits.UntilAsync(() => vm.PreviewImage != null && vm.IsHistoryLoaded);
            await entered.Task.WaitAsync(TestWaits.Condition);
            var retained = vm.PreviewImage!;
            var original = vm.OriginalViewPixelSize;
            Report("before", vm);
            Assert.True(vm.RotateLeftCommand.CanExecute(null));
            vm.RotateLeftCommand.Execute(null);
            Report("same-turn", vm);
            AssertSurface(vm, retained, original, post);
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            Dispatcher.UIThread.RunJobs();
            Report("drained-held", vm);
            AssertSurface(vm, retained, original, post);
            Assert.False(release.Task.IsCompleted);
        }
        finally
        {
            release.TrySetResult();
        }
    }

    [AvaloniaTheory]
    [InlineData(1600, 1067)]
    [InlineData(3200, 2133)]
    public async Task R2FrozenLatency(int width, int height)
    {
        var post = RequireMode();
#if DEBUG
        Assert.Fail("The frozen latency workload requires Release.");
#endif
        using var fixture = new CatalogVmFixture("rotate-r2");
        using var catalog = await fixture.CreateCatalogAsync();
        var image = await SeedAsync(fixture, catalog, width, height);
        await using var vm = CreateViewModel(catalog);
        var release = NewSignal();
        var entered = NewSignal();
        vm.ImageService.Previews.SourceWorkGateAsync = () =>
        {
            entered.TrySetResult();

            return release.Task;
        };
        vm.ImageService.Previews.RenderGateAsync = () => release.Task;
        var renders = 0;
        vm.ImageService.Previews.RenderStarted += () => Interlocked.Increment(ref renders);
        var traces = new List<string>();
        using var trace = ImageServiceHelpers.OverrideDisplayTraceForTesting(true, traces.Add);

        try
        {
            vm.SelectedImage = image;
            await TestWaits.UntilAsync(() => vm.PreviewImage != null && vm.IsHistoryLoaded);
            await entered.Task.WaitAsync(TestWaits.Condition);
            var retained = vm.PreviewImage!;
            Assert.Equal(new PixelSize(width, height), retained.PixelSize);
            var samples = new List<double>();
            var commands = new List<double>();
            long paintedAt = 0;
            void Observe(object? sender, PropertyChangedEventArgs args)
            {
                if (args.PropertyName == nameof(vm.PreviewImage))
                {
                    paintedAt = Stopwatch.GetTimestamp();
                }
            }

            vm.PropertyChanged += Observe;

            try
            {
                for (var press = 0; press < WarmupPresses + SampleCount; press++)
                {
                    // Separate UI turns allow retired provisional surfaces to be disposed.
                    // Real source/render work stays held; draining is outside the timestamp pair.
                    await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
                    Dispatcher.UIThread.RunJobs();
                    Assert.Equal(new PixelSize(width, height), retained.PixelSize);
                    Assert.False(release.Task.IsCompleted);
                    Assert.Equal(0, Volatile.Read(ref renders));
                    Assert.True(vm.RotateLeftCommand.CanExecute(null));
                    traces.Clear();
                    paintedAt = 0;
                    var started = Stopwatch.GetTimestamp();
                    vm.RotateLeftCommand.Execute(null);
                    var returned = Stopwatch.GetTimestamp();
                    var provisional = traces.Count(line => line.Contains("paint source=provisional-rotate "));
                    Assert.Equal(post ? 1 : 0, provisional);

                    if (post)
                    {
                        Assert.True(paintedAt >= started && paintedAt <= returned);
                        var odd = (press + 1) % 2 != 0;
                        Assert.Equal(odd ? new PixelSize(height, width) : new PixelSize(width, height),
                            vm.PreviewImage!.PixelSize);
                    }
                    else
                    {
                        Assert.Equal(0, paintedAt);
                        Assert.Same(retained, vm.PreviewImage);
                    }

                    if (press >= WarmupPresses)
                    {
                        commands.Add(Stopwatch.GetElapsedTime(started, returned).TotalMilliseconds);
                        samples.Add(Stopwatch.GetElapsedTime(started, post ? paintedAt : returned).TotalMilliseconds);
                    }
                }
            }
            finally
            {
                vm.PropertyChanged -= Observe;
            }

            Assert.Equal(SampleCount, samples.Count);
            Assert.Equal(0, Volatile.Read(ref renders));
            output.WriteLine($"R2 mode={(post ? "post" : "baseline")} size={width}x{height} " +
                $"warmup={WarmupPresses} samples={samples.Count} provisional={(post ? SampleCount : 0)} " +
                $"p95_ms={P95(samples):F6} command_p95_ms={P95(commands):F6} " +
                $"min_ms={samples.Min():F6} max_ms={samples.Max():F6}");
            output.WriteLine("R2 samples_ms=" + string.Join(",", samples.Select(value => value.ToString("F6"))));

            if (post)
            {
                Assert.True(P95(samples) <= (width == 1600 ? 30 : 90));
            }
        }
        finally
        {
            release.TrySetResult();
        }
    }

    private static bool RequireMode()
    {
        var mode = Environment.GetEnvironmentVariable("HAPPY_PHOTON_ROTATE_MEASURE");
        Assert.SkipUnless(mode is "baseline" or "post", "Opt-in frozen rotate measurement.");

        return mode == "post";
    }

    private void Report(string stage, MainWindowViewModel vm) => output.WriteLine(
        $"R1 stage={stage} identity={RuntimeHelpers.GetHashCode(vm.PreviewImage!)} " +
        $"bitmap={vm.PreviewImage!.PixelSize} original={vm.OriginalViewPixelSize} rotation={vm.Rotation}");

    private static void AssertSurface(MainWindowViewModel vm, Bitmap retained, PixelSize original, bool post)
    {
        if (post)
        {
            Assert.NotSame(retained, vm.PreviewImage);
            Assert.Equal(new PixelSize(retained.PixelSize.Height, retained.PixelSize.Width), vm.PreviewImage!.PixelSize);
            Assert.Equal(new PixelSize(original.Height, original.Width), vm.OriginalViewPixelSize);
        }
        else
        {
            Assert.Same(retained, vm.PreviewImage);
            Assert.Equal(retained.PixelSize, vm.PreviewImage!.PixelSize);
            Assert.Equal(original, vm.OriginalViewPixelSize);
        }
    }

    private static double P95(List<double> samples) => samples.Order().ElementAt(47);

    private static MainWindowViewModel CreateViewModel(CatalogService catalog) => new(
        catalog, new NullBaseLoader(), loadMetadataAsync: _ => Task.CompletedTask,
        availabilityService: new TestSourceAvailabilityService(SourceAvailability.AvailableLocally))
    {
        IsDevelopMode = true
    };

    private static async Task<ImageFile> SeedAsync(
        CatalogVmFixture fixture, CatalogService catalog, int width, int height)
    {
        var path = fixture.Path("source.jpg");

        using (var source = new MagickImage(MagickColors.Gray, 64, 48))
        {
            source.Write(path);
        }

        var image = new ImageFile(path);
        await image.EnsureCatalogIdAsync(catalog);
        await catalog.SaveEditSettingsAsync(image.CatalogId, image.EditSettings);
        await using var cache = new PreviewCacheService(catalog);
        using var cached = new MagickImage(MagickColors.Red, (uint)width, (uint)height);
        var original = new PixelSize(6000, 4000);
        Assert.True(await cache.QueueSaveToCache(image, cached,
            RenderSettingsHash.Compute(image.EditSettings), new PreviewCacheIdentity(original, original)));

        return image;
    }

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}

