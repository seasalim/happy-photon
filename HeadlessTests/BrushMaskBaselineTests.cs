using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using HappyPhoton.Views;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

public sealed partial class BrushMaskBaselineTests(ITestOutputHelper output)
{
    [AvaloniaTheory]
    [InlineData("release", false)]
    [InlineData("release", true)]
    [InlineData("exposure", false)]
    [InlineData("exposure", true)]
    [InlineData("resting", false)]
    [InlineData("resting", true)]
    [InlineData("one-to-one", false)]
    [InlineData("one-to-one", true)]
    public async Task Measure(string scenario, bool restricted)
    {
        for (var run = 1; run <= 3; run++)
        {
            await MeasureOnce(scenario, run, restricted);
        }
    }

    private async Task MeasureOnce(string scenario, int run, bool restricted)
    {
        using var fixture = new CatalogVmFixture("brush-mask-baseline");
        using var catalog = await fixture.CreateCatalogAsync();
        var clock = new TestTimeProvider();
        await using var vm = fixture.CreateViewModel(catalog, new BaselineLoader(),
            _ => Task.CompletedTask, new TestSourceAvailabilityService(SourceAvailability.AvailableLocally),
            timeProvider: clock);
        var settings = new EditSettings
        {
            Locals = [new() { Type = "brush",
                Luminance = restricted ? new() { Enabled = true, Lower = .1 } : null, Strokes =
                [new() { Radius = .04, Feather = 0, Points = [new(3277, 3277)] }] }]
        };
        var image = new ImageFile(fixture.Path("synthetic.jpg")) { EditSettings = settings };
        image.CatalogId = await catalog.GetOrCreateImageAsync(image.FilePath);
        await catalog.SaveEditSettingsAsync(image.CatalogId, settings);
        vm.Browse.SetImages([image]);
        vm.IsDevelopMode = true;
        vm.SelectedImage = image;
        await TestWaits.UntilAsync(() => vm.IsHistoryLoaded && vm.PreviewImage != null);
        await vm.ToggleLocalsModeCommand.ExecuteAsync(null);
        vm.SelectedLocal = vm.Locals[0];
        vm.ShowLocalMask = true;
        await vm.PendingLocalMaskTask.WaitAsync(TestWaits.Condition);
        Assert.Equal(1600, vm.PreviewImage!.PixelSize.Width);

        async Task Rest()
        {
            vm.PublishRequiredDeviceLongEdge(2000);
            clock.Advance(TimeSpan.FromMilliseconds(100));
            await TestWaits.UntilAsync(() => vm.PreviewImage!.PixelSize.Width == 2000);
        }

        if (scenario != "resting")
        {
            await Rest();
            await vm.PendingLocalMaskTask.WaitAsync(TestWaits.Condition);
        }

        var overlay = new LocalsOverlayControl { DataContext = vm };
        overlay.Measure(new Size(400, 200));
        overlay.Arrange(new Rect(0, 0, 400, 200));
        var hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var renders = 0;
        vm.LocalMaskRenderGateAsync = () => { renders++; return hold.Task; };
        var frames = 0;
        var blankFrames = 0;
        var intervals = 0;
        var wasBlank = false;
        var coveredFrames = 0;
        byte[]? expected = null;
        var expectedPixels = 0;

        void Frame(string stage)
        {
            Assert.True(vm.IsLocalMaskVisible);
            Assert.True(overlay.IsVisible);
            Assert.True(vm.CanEditLocals);
            var pixels = Draw(overlay);
            var blank = vm.LocalRangeMask == null && vm.LiveBrushStroke == null;
            frames++;

            if (blank)
            {
                blankFrames++;
                if (!wasBlank) intervals++;
            }

            wasBlank = blank;
            var covered = 0;

            if (expected != null)
            {
                for (var i = 3; i < expected.Length; i += 4)
                {
                    if (expected[i] != 0 && pixels[i] != 0) covered++;
                }

                if (covered == expectedPixels) coveredFrames++;
            }

            output.WriteLine($"frame scenario={scenario} restricted={restricted} run={run} stage={stage} visible=True " +
                $"preview={vm.PreviewImage!.PixelSize} blank={blank} covered={covered}/{expectedPixels}");
        }

        try
        {
            if (scenario == "release")
            {
                vm.BrushSize = 50;
                vm.BrushFeather = 0;
                Assert.True(vm.BeginBrushStroke(new(.6, .65)));
                Assert.True(vm.ExtendBrushStroke(new(.8, .65), 2000));
                var stroke = vm.LiveBrushStroke!;
                var onlyNew = vm.SelectedLocal! with { Strokes = [stroke], Luminance = null };
                using var reference = LocalRangeMaskRenderer.Render(null, vm.LocalsFrame!.Value,
                    settings, onlyNew, new PixelSize(400, 200), 0xffffff, CancellationToken.None);
                expected = Bytes(reference);
                expectedPixels = Enumerable.Range(0, 400 * 200).Count(i => expected[i * 4 + 3] != 0);
                Assert.True(expectedPixels > 0);
                Frame("painting");
                await vm.CompleteLocalsGestureAsync();
                Frame("release-held");
                clock.Advance(TimeSpan.FromMilliseconds(1000));

                if (vm.PendingPreviewDebounceTask is { } pending)
                {
                    await pending.WaitAsync(TestWaits.Condition);
                }

                await TestWaits.UntilAsync(() => vm.PreviewImage!.PixelSize.Width == 1600);
                Frame("interactive-held");
                await Rest();
                Frame("resting-held");
            }
            else if (scenario == "exposure")
            {
                Frame("before");

                foreach (var exposure in new[] { 1.0, 0.0 })
                {
                    vm.LocalExposure = exposure;
                    clock.Advance(TimeSpan.FromMilliseconds(1000));
                    await vm.PendingPreviewDebounceTask!.WaitAsync(TestWaits.Condition);
                    Assert.Equal(1600, vm.PreviewImage!.PixelSize.Width);
                    Frame($"exposure-{exposure}-interactive-held");
                    await Rest();
                    Frame($"exposure-{exposure}-resting-held");
                }
            }
            else
            {
                Frame("before");

                if (scenario == "resting")
                {
                    await Rest();
                    Assert.True(vm.RestingPaintCount > 0);
                }
                else
                {
                    vm.PublishRequiredDeviceLongEdge(2400);
                    await vm.FullResolutionWork.WaitAsync(TestWaits.Condition);
                    Assert.Equal(2400, vm.PreviewImage!.PixelSize.Width);
                    Assert.True(vm.HeldFullBaseCount > 0);
                }

                Assert.False(vm.IsLocalRangeMaskUpdating);
                Assert.False(vm.IsLocalRangeMaskUpdating);
                Frame("swap-held");
            }

            hold.TrySetResult();
            await vm.PendingLocalMaskTask.WaitAsync(TestWaits.Condition);
            Assert.NotNull(vm.LocalRangeMask);
            Frame("published");
            Assert.Equal(0, blankFrames);
            Assert.Equal(0, intervals);

            if (scenario == "release") Assert.Equal(frames, coveredFrames);
            if (scenario is "resting" or "one-to-one") Assert.Equal(0, renders);

            output.WriteLine($"gate scenario={scenario} restricted={restricted} run={run} frames={frames} blank_frames={blankFrames} " +
                $"blank_intervals={intervals} mask_requests={renders} covered_frames={coveredFrames}/{frames} " +
                $"stroke_pixels={expectedPixels} resting_edge=2000");
        }
        finally
        {
            hold.TrySetResult();
            overlay.DataContext = null;
        }
    }

    private static byte[] Draw(LocalsOverlayControl overlay)
    {
        using var target = new RenderTargetBitmap(new PixelSize(400, 200), new Vector(96, 96));

        using (var context = target.CreateDrawingContext())
        {
            overlay.Render(context);
        }

        return Bytes(target);
    }

    private static unsafe byte[] Bytes(Bitmap bitmap)
    {
        var pixels = new byte[400 * 200 * 4];

        fixed (byte* p = pixels)
        {
            bitmap.CopyPixels(new PixelRect(0, 0, 400, 200), (nint)p, pixels.Length, 400 * 4);
        }

        return pixels;
    }

    private sealed class BaselineLoader(string? changedOutcome = null) : IBaseImageLoader
    {
        public bool CanLoad(ImageFile file) => true;

        public BaseImageLoadOutcome LoadPreviewBaseWithOutcome(ImageFile file,
            BaseDecodeSettings decode, CancellationToken cancellationToken) =>
            BaseImageLoadOutcome.Loaded(new PreviewBasePair(Create(1600, decode), Create(2000, decode)));

        public BaseImage LoadFullBase(ImageFile file, BaseDecodeSettings decode,
            CancellationToken cancellationToken) => Create(2400, decode);

        private BaseImage Create(int width, BaseDecodeSettings decode) => new(
            new MagickImage(MagickColors.Gray, (uint)width, (uint)(width / 2)) { ColorSpace = ColorSpace.RGB },
            new BaseImageInfo(BaseSourceKind.Standard, false, decode, null, null,
                changedOutcome == "white-balance" && width == 2000 ? 5000 : 6504, 0, false, null, 1, 2400, 1200)
            {
                ProfileToken = changedOutcome == "profile" && width == 2000 ? "resolved-profile" : "",
                ProfileStatus = changedOutcome == "profile" && width == 2000
                    ? DcpProfileErrorCode.Missing : default
            });
    }
}
