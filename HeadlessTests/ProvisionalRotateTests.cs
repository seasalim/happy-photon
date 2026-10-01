using System.Reflection;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class ProvisionalRotateTests
{
    [AvaloniaTheory]
    [InlineData(1, new[] { 0, 1, 2, 3, 4, 5 })]
    [InlineData(2, new[] { 2, 1, 0, 5, 4, 3 })]
    [InlineData(3, new[] { 5, 4, 3, 2, 1, 0 })]
    [InlineData(4, new[] { 3, 4, 5, 0, 1, 2 })]
    [InlineData(5, new[] { 0, 3, 1, 4, 2, 5 })]
    [InlineData(6, new[] { 3, 0, 4, 1, 5, 2 })]
    [InlineData(7, new[] { 5, 2, 4, 1, 3, 0 })]
    [InlineData(8, new[] { 2, 5, 1, 4, 0, 3 })]
    public void SharedRemapPreservesAllExifOrientationsAndAlpha(int orientation, int[] indexes)
    {
        var bytes = Enumerable.Range(0, 6).SelectMany(i => new byte[]
            { (byte)(20 + i * 20), (byte)(10 + i * 10), (byte)(5 + i * 5), 128 }).ToArray();
        using var source = BitmapConversionService.ConvertToBitmap(bytes, 3, 2);
        var before = Pixels(source);
        using var result = BgraBitmapOrientation.ApplyExifOrientation(source, orientation);
        Assert.Equal(source.AlphaFormat, result.AlphaFormat);
        Assert.Equal(orientation >= 5 ? new PixelSize(2, 3) : new PixelSize(3, 2), result.PixelSize);
        Assert.Equal(indexes.Select(index => before[index]), Pixels(result));
        Assert.Equal(before, Pixels(source));
    }

    [AvaloniaFact]
    public async Task RapidPressesRemapRetainedPixelsAndRealPaintWins()
    {
        await using var rig = await Rig.CreateAsync();
        var vm = rig.Vm;
        var retained = vm.PreviewImage!;
        var source = Pixels(retained);
        var histogram = vm.Histogram;
        var original = vm.OriginalViewPixelSize;
        var traces = new List<string>();
        using var trace = ImageServiceHelpers.OverrideDisplayTraceForTesting(true, traces.Add);
        var renders = 0;
        vm.ImageService.Previews.RenderStarted += () => renders++;
        var provisional = new List<Bitmap>();

        for (var press = 1; press <= 3; press++)
        {
            vm.RotateLeftCommand.Execute(null);
            provisional.Add(vm.PreviewImage!);
            AssertTurn(source, retained.PixelSize, vm.PreviewImage!, press);
            Assert.Equal(press % 2 == 1 ? new PixelSize(original.Height, original.Width) : original,
                vm.OriginalViewPixelSize);
            Assert.Null(vm.PreviewClippingMask);
            Assert.Same(histogram, vm.Histogram);
            Assert.Null(vm.Histogram!.Waveform);
            Assert.Null(vm.ImageService.Previews.TryGetPreviewRenderIdentity(vm.PreviewImage!));
            Assert.Null(vm.SpotDisplayMap);
            Assert.False(vm.HasArmedRestingRender);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(64, retained.PixelSize.Width);
        }

        Assert.Equal(3, traces.Count(line => line.Contains("paint source=provisional-rotate ")));
        Assert.Throws<ObjectDisposedException>(() => _ = provisional[0].PixelSize);
        Assert.Throws<ObjectDisposedException>(() => _ = provisional[1].PixelSize);
        Assert.Equal(0, renders);
        rig.Release.TrySetResult();
        await TestWaits.UntilAsync(() => vm.PreviewImage != provisional[2] &&
            vm.ImageService.Previews.TryGetPreviewRenderIdentity(vm.PreviewImage!) != null);
        Assert.Equal(1, renders);
        Dispatcher.UIThread.RunJobs();
        Assert.Throws<ObjectDisposedException>(() => _ = retained.PixelSize);
        Assert.Throws<ObjectDisposedException>(() => _ = provisional[2].PixelSize);
        Assert.DoesNotContain(provisional, bitmap => ReferenceEquals(bitmap, rig.Image.Thumbnail));
    }

    [AvaloniaFact]
    public async Task FailedRotationRestoresRetainedObjectAndSize()
    {
        await using var rig = await Rig.CreateAsync(fail: true);
        var retained = rig.Vm.PreviewImage;
        var size = rig.Vm.OriginalViewPixelSize;
        rig.Vm.RotateRightCommand.Execute(null);
        Assert.NotSame(retained, rig.Vm.PreviewImage);
        rig.Release.TrySetResult();
        await TestWaits.UntilAsync(() => rig.Vm.Rotation == 0);
        Assert.Same(retained, rig.Vm.PreviewImage);
        Assert.Equal(size, rig.Vm.OriginalViewPixelSize);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(64, retained!.PixelSize.Width);
    }

    [AvaloniaFact]
    public async Task RawCachedPaintUsesRequestedSettingsProvenance()
    {
        await using var rig = await Rig.CreateAsync(fail: true, raw: true);
        var painted = rig.Vm.PreviewImage!;
        var pixels = Pixels(painted);
        rig.Vm.RotateLeftCommand.Execute(null);
        AssertTurn(pixels, painted.PixelSize, rig.Vm.PreviewImage!, 1);
    }

    [AvaloniaFact]
    public async Task RollbackToAnotherRotationRemapsRecordedPaint()
    {
        await using var rig = await Rig.CreateAsync(new EditSettings { Rotation = 90 });
        var vm = rig.Vm;
        var painted = vm.PreviewImage!;
        var pixels = Pixels(painted);
        vm.RotateLeftCommand.Execute(null);
        var restored = (bool)typeof(MainWindowViewModel).GetMethod("RollbackEditReservation",
            BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(vm,
                [rig.Image, new EditSettings { Rotation = 180 }, vm.LatestPreviewOutcomeGeneration,
                    PreviewSurfaceIntent.Edited])!;
        Assert.True(restored);
        AssertTurn(pixels, painted.PixelSize, vm.PreviewImage!, 3);
    }

    [AvaloniaFact]
    public async Task SettingsMismatchedCachedPaintWaits()
    {
        await using var rig = await Rig.CreateAsync(mismatch: true);
        var painted = rig.Vm.PreviewImage;
        rig.Vm.RotateLeftCommand.Execute(null);
        Assert.Same(painted, rig.Vm.PreviewImage);
    }

    [AvaloniaFact]
    public async Task RotationDuringPendingGeometryResetWaits()
    {
        await using var rig = await Rig.CreateAsync(new EditSettings
        {
            Geometry = new GeometrySettings { Vertical = 20 }
        });
        var painted = rig.Vm.PreviewImage;
        rig.Vm.GeometryVertical = 0;
        rig.Vm.RotateLeftCommand.Execute(null);
        Assert.Same(painted, rig.Vm.PreviewImage);
    }

    [AvaloniaTheory]
    [InlineData("crop")]
    [InlineData("geometry")]
    [InlineData("horizon")]
    [InlineData("split")]
    [InlineData("original")]
    [InlineData("proof")]
    public async Task IneligiblePaintWaits(string reason)
    {
        var settings = new EditSettings();

        if (reason == "crop") settings.Crop = new CropRegion { Right = .5 };
        if (reason == "geometry") settings.Geometry = new GeometrySettings { Vertical = 20 };
        if (reason == "horizon") settings.HorizonRotation = 2;

        await using var rig = await Rig.CreateAsync(settings);
        var vm = rig.Vm;
        if (reason == "split") vm.IsBeforeAfterSplit = true;
        if (reason == "original") vm.IsShowingOriginal = true;
        if (reason == "proof") SetField(vm, "_proofIsDisplayed", true);
        var painted = vm.PreviewImage;
        vm.RotateLeftCommand.Execute(null);
        Assert.Same(painted, vm.PreviewImage);
    }

    [AvaloniaFact]
    public async Task OneToOneUsesRestoredRestingSurface()
    {
        await using var rig = await Rig.CreateAsync();
        var vm = rig.Vm;
        var resting = vm.PreviewImage!;
        var pixels = Pixels(resting);
        using var source = new MagickImage(MagickColors.Blue, 128, 96);
        var full = BitmapConversionService.ConvertToBitmap(source)!;
        SetField(vm, "_fullRestingBitmap", resting);
        SetField(vm, "_fullBitmap", full);
        vm.PreviewImage = full;
        vm.RotateLeftCommand.Execute(null);
        AssertTurn(pixels, resting.PixelSize, vm.PreviewImage!, 1);
        Dispatcher.UIThread.RunJobs();
        Assert.Throws<ObjectDisposedException>(() => _ = full.PixelSize);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SelectionAndDisposalReleaseRetainedAndProvisional(bool dispose)
    {
        await using var rig = await Rig.CreateAsync();
        var retained = rig.Vm.PreviewImage!;
        rig.Vm.RotateLeftCommand.Execute(null);
        var provisional = rig.Vm.PreviewImage!;

        if (dispose)
        {
            rig.Release.TrySetResult();
            await rig.DisposeViewModelAsync();
        }
        else
        {
            rig.Vm.SelectedImage = null;
            Dispatcher.UIThread.RunJobs();
        }

        Assert.Throws<ObjectDisposedException>(() => _ = retained.PixelSize);
        Assert.Throws<ObjectDisposedException>(() => _ = provisional.PixelSize);
    }

    [AvaloniaFact]
    public async Task BothCullPerfMappingsMarkProvisionalWithoutCountingRealPaint()
    {
        await using var rig = await Rig.CreateAsync();
        var recorder = new CullPerfRecorder();
        rig.Vm.ImageService.Previews.CullPerf = recorder;
        rig.Vm.RotateLeftCommand.Execute(null);
        using var source = new MagickImage(MagickColors.Red, 4, 3);
        var bitmap = BitmapConversionService.ConvertToBitmap(source)!;
        using var pane = new ComparePaneViewModel(rig.Image);
        typeof(MainWindowViewModel).GetMethod("ApplyPreviewPaneBitmap", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(rig.Vm, [pane, bitmap, (Func<bool>)(() => true), default(PixelSize), false,
                PreviewPaintSource.ProvisionalRotate, 0L, false]);
        var paints = recorder.Snapshot().Where(item => item.Kind == "ProvisionalRotate").ToArray();
        Assert.Equal(2, paints.Length);
        Assert.DoesNotContain(recorder.Snapshot(), item => item.Kind is "FreshRender" or "CachedJpeg" or "RestingRender");
        bitmap.Dispose();
    }

    private static void SetField(MainWindowViewModel vm, string name, object value) =>
        typeof(MainWindowViewModel).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(vm, value);

    private static uint[] Pixels(Bitmap bitmap)
    {
        using var readable = new WriteableBitmap(bitmap.PixelSize, bitmap.Dpi, PixelFormat.Bgra8888, AlphaFormat.Premul);
        using var buffer = readable.Lock();
        bitmap.CopyPixels(buffer);
        var values = new int[bitmap.PixelSize.Width * bitmap.PixelSize.Height];

        for (var y = 0; y < bitmap.PixelSize.Height; y++)
        {
            Marshal.Copy(buffer.Address + y * buffer.RowBytes, values, y * bitmap.PixelSize.Width, bitmap.PixelSize.Width);
        }

        return values.Select(value => unchecked((uint)value)).ToArray();
    }

    private static void AssertTurn(uint[] source, PixelSize size, Bitmap turned, int presses)
    {
        var expectedSize = presses % 2 == 1 ? new PixelSize(size.Height, size.Width) : size;
        Assert.Equal(expectedSize, turned.PixelSize);
        var actual = Pixels(turned);

        for (var y = 0; y < size.Height; y++)
        {
            for (var x = 0; x < size.Width; x++)
            {
                var (dx, dy) = presses switch
                {
                    1 => (y, size.Width - 1 - x),
                    2 => (size.Width - 1 - x, size.Height - 1 - y),
                    _ => (size.Height - 1 - y, x)
                };
                Assert.Equal(source[y * size.Width + x], actual[dy * expectedSize.Width + dx]);
            }
        }
    }

    private sealed class Rig : IAsyncDisposable
    {
        private readonly CatalogVmFixture _fixture = new("rotate-correctness");

        private CatalogService _catalog = null!;

        public MainWindowViewModel Vm { get; private set; } = null!;

        public ImageFile Image { get; private set; } = null!;

        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public static async Task<Rig> CreateAsync(EditSettings? settings = null, bool mismatch = false, bool fail = false, bool raw = false)
        {
            var rig = new Rig();

            try
            {
                rig._catalog = await rig._fixture.CreateCatalogAsync();
                using var source = new MagickImage("gradient:red-blue", 64, 48);
                var path = rig._fixture.Path(raw ? "source.CR3" : "source.jpg");
                source.Write(path, MagickFormat.Jpeg);
                rig.Image = new ImageFile(path) { EditSettings = settings ?? new EditSettings() };
                await rig.Image.EnsureCatalogIdAsync(rig._catalog);
                await rig._catalog.SaveEditSettingsAsync(rig.Image.CatalogId, rig.Image.EditSettings);
                await using var cache = new PreviewCacheService(rig._catalog);
                Assert.True(await cache.QueueSaveToCache(rig.Image, source,
                    mismatch ? "unknown-settings" : RenderSettingsHash.Compute(rig.Image.EditSettings),
                    new PreviewCacheIdentity(new PixelSize(6000, 4000), new PixelSize(6000, 4000))));
                rig.Vm = new MainWindowViewModel(rig._catalog, fail ? new NullBaseLoader() : null,
                    loadMetadataAsync: _ => Task.CompletedTask,
                    availabilityService: new TestSourceAvailabilityService(SourceAvailability.AvailableLocally))
                {
                    IsDevelopMode = true
                };
                rig.Vm.ImageService.Previews.SourceWorkGateAsync = () => rig.Release.Task;
                rig.Vm.SelectedImage = rig.Image;
                await TestWaits.UntilAsync(() => rig.Vm.PreviewImage != null && rig.Vm.IsHistoryLoaded);

                return rig;
            }
            catch
            {
                await rig.DisposeAsync();
                throw;
            }
        }

        private bool _disposedViewModel;

        public async Task DisposeViewModelAsync()
        {
            if (_disposedViewModel || Vm == null) return;

            _disposedViewModel = true;
            await Vm.DisposeAsync();
        }

        public async ValueTask DisposeAsync()
        {
            Release.TrySetResult();
            await DisposeViewModelAsync();
            _catalog?.Dispose();
            _fixture.Dispose();
        }
    }
}
