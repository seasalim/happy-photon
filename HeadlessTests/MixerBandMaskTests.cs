using System.Runtime.InteropServices;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class MixerBandMaskTests : IDisposable
{
    private readonly CatalogVmFixture _fixture = new("mixer-mask");

    [AvaloniaFact]
    public async Task HoverIsTransientAndPreservesEditsAndLocals()
    {
        using var catalog = await _fixture.CreateCatalogAsync();
        await using var vm = CreateVm(catalog);
        var image = await Prepare(vm, catalog);
        await vm.ToggleLocalsModeCommand.ExecuteAsync(null);
        await vm.PlaceLocalAtCenterCommand.ExecuteAsync(null);
        await vm.ToggleLocalHueCommand.ExecuteAsync(null);
        vm.ShowLocalMask = true;
        if (vm.PendingPreviewDebounceTask is { } pending) await pending;

        await vm.PendingLocalMaskTask;
        var localMask = vm.LocalRangeMask;
        Assert.NotNull(localMask);
        vm.ShowLocalMask = false;
        vm.BeginClippingPeek(ClippingOverlaySide.DisplayFloor);
        var clipping = vm.VisibleClippingOverlaySides;
        Assert.NotEqual(ClippingOverlaySide.None, clipping);
        var hash = RenderSettingsHash.Compute(image.EditSettings);
        var history = vm.HistoryEntries.Count;
        var canReset = vm.CanReset;
        var preview = vm.PreviewImage;
        var generation = vm.LatestPreviewOutcomeGeneration;

        vm.BeginMixerBandHover(ColorMixerBand.Red, 0x202020);
        await vm.PendingMixerMaskTask;
        var first = Assert.IsAssignableFrom<Bitmap>(vm.MixerBandMask);
        Assert.Equal(preview!.PixelSize, first.PixelSize);
        Assert.Equal(ClippingOverlaySide.None, vm.VisibleClippingOverlaySides);
        Assert.Equal(hash, RenderSettingsHash.Compute(image.EditSettings));
        Assert.Equal(history, vm.HistoryEntries.Count);
        Assert.Equal(canReset, vm.CanReset);
        Assert.Same(preview, vm.PreviewImage);
        Assert.Equal(generation, vm.LatestPreviewOutcomeGeneration);
        vm.EndMixerBandHover();
        Assert.Null(vm.MixerBandMask);
        Assert.Equal(clipping, vm.VisibleClippingOverlaySides);
        Dispatcher.UIThread.RunJobs();
        Assert.Throws<ObjectDisposedException>(() => _ = first.PixelSize);

        vm.ShowLocalMask = true;
        await vm.PendingLocalMaskTask;
        localMask = vm.LocalRangeMask;
        Assert.NotNull(localMask);
        vm.BeginMixerBandHover(ColorMixerBand.Blue, 0x202020);
        await vm.PendingMixerMaskTask;
        Assert.NotNull(vm.MixerBandMask);
        Assert.Same(localMask, vm.LocalRangeMask);
        vm.EndMixerBandHover();
        Assert.Same(localMask, vm.LocalRangeMask);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PreviewRefinementRebuildsHoveredBandWithoutAnotherEnter(bool holdBuild)
    {
        using var catalog = await _fixture.CreateCatalogAsync();
        await using var vm = _fixture.CreateViewModel(catalog, new CountingPairLoader(),
            _ => Task.CompletedTask, new TestSourceAvailabilityService(SourceAvailability.AvailableLocally));
        await Prepare(vm, catalog);
        var entered = new TaskCompletionSource();
        var release = new TaskCompletionSource();
        vm.MixerMaskRenderGateAsync = () =>
        {
            entered.TrySetResult();

            return holdBuild ? release.Task : Task.CompletedTask;
        };
        var original = vm.PreviewImage!;
        var parent = vm.ImageService.Previews.TryGetPreviewRenderIdentity(original);
        Assert.NotNull(parent);
        using var resting = await vm.ImageService.Previews.RenderRestingPreviewAsync(vm.SelectedImage!,
            new(), 320, parent, CancellationToken.None);
        Assert.NotNull(resting);
        using var refined = resting.DetachBitmap();
        Assert.True(refined.PixelSize.Width > original.PixelSize.Width);
        vm.BeginMixerBandHover(ColorMixerBand.Blue, 0x123456);
        Bitmap? firstMask = null;

        try
        {
            await entered.Task.WaitAsync(TestWaits.Condition);

            if (!holdBuild)
            {
                await vm.PendingMixerMaskTask;
                firstMask = Assert.IsAssignableFrom<Bitmap>(vm.MixerBandMask);
            }

            vm.PreviewImage = refined;

            if (firstMask != null)
            {
                Assert.Same(firstMask, vm.MixerBandMask);
            }

            release.TrySetResult();
            await vm.PendingMixerMaskTask;
            var mask = Assert.IsType<WriteableBitmap>(vm.MixerBandMask);
            Assert.Equal(refined.PixelSize, mask.PixelSize);
            using var lease = vm.ImageService.Previews.AcquireLocalRangeBase(vm.SelectedImage!,
                new EditSettings(), refined.PixelSize.Width, refined);
            Assert.NotNull(lease);
            using var expected = MixerBandMaskRenderer.Render(lease.Base, new(), ColorMixerBand.Blue,
                refined.PixelSize, 0x123456, CancellationToken.None);
            using var actualFrame = mask.Lock();
            using var expectedFrame = expected.Lock();
            Assert.Equal(Marshal.ReadInt32(expectedFrame.Address), Marshal.ReadInt32(actualFrame.Address));
            Dispatcher.UIThread.RunJobs();

            if (firstMask != null)
            {
                Assert.Throws<ObjectDisposedException>(() => _ = firstMask.PixelSize);
            }

            vm.EndMixerBandHover();
            Assert.Null(vm.MixerBandMask);
        }
        finally
        {
            release.TrySetResult();
            vm.EndMixerBandHover();
            await vm.PendingMixerMaskTask;
            vm.PreviewImage = original;
        }
    }

    [AvaloniaTheory]
    [InlineData("leave")]
    [InlineData("replacement")]
    [InlineData("selection")]
    [InlineData("mode")]
    [InlineData("white-balance")]
    public async Task HeldBuildCannotPublishAfterInvalidation(string change)
    {
        using var catalog = await _fixture.CreateCatalogAsync();
        await using var vm = CreateVm(catalog);
        await Prepare(vm, catalog);
        var entered = new TaskCompletionSource();
        var release = new TaskCompletionSource();
        vm.MixerMaskRenderGateAsync = () =>
        {
            entered.TrySetResult();

            return release.Task;
        };
        vm.BeginMixerBandHover(ColorMixerBand.Red, 0x202020);
        var first = vm.PendingMixerMaskTask;

        try
        {
            await entered.Task.WaitAsync(TestWaits.Condition);

            switch (change)
            {
                case "leave": vm.EndMixerBandHover(); break;

                case "replacement": vm.BeginMixerBandHover(ColorMixerBand.Blue, 0x202020); break;

                case "selection": vm.SelectedImage = null; break;

                case "mode": vm.IsDevelopMode = false; break;

                case "white-balance": vm.WhiteBalanceTint += 10; break;
            }

            Assert.Null(vm.MixerBandMask);
        }
        finally
        {
            release.TrySetResult();
            await first;
            await vm.PendingMixerMaskTask;
        }

        if (change is "replacement" or "white-balance") Assert.NotNull(vm.MixerBandMask);
        else Assert.Null(vm.MixerBandMask);
    }

    [AvaloniaTheory]
    [InlineData("preset")]
    [InlineData("history")]
    [InlineData("monochrome")]
    public async Task OtherHoverAndMonochromeSuppressBandMask(string state)
    {
        using var catalog = await _fixture.CreateCatalogAsync();
        var clock = new TestTimeProvider();
        await using var vm = CreateVm(catalog, clock, state == "monochrome");
        await Prepare(vm, catalog);

        if (state == "preset")
        {
            vm.BeginMixerBandHover(ColorMixerBand.Red, 0x202020);
            await vm.PendingMixerMaskTask;
            Assert.NotNull(vm.MixerBandMask);
            await vm.PresetService.UseDirectoryAsync(_fixture.Path("presets"));
            var preset = await vm.PresetService.SaveUserPresetAsync("Hover", new EditSettings { Exposure = 1 });
            await vm.PreviewPresetHoverAsync(preset.Id);
        }

        if (state == "history")
        {
            vm.Exposure = 1;
            vm.OnSliderEditStarted();
            vm.OnSliderEditCompleted(completeImmediately: true);
            if (vm.PendingPreviewDebounceTask is { } pending) await pending;

            vm.BeginMixerBandHover(ColorMixerBand.Red, 0x202020);
            await vm.PendingMixerMaskTask;
            Assert.NotNull(vm.MixerBandMask);
            var task = vm.PreviewHistoryHoverAsync(vm.HistoryEntries.Single(entry => !entry.IsCurrent));
            clock.Advance(TimeSpan.FromMilliseconds(80));
            await task;
        }

        Assert.Null(vm.MixerBandMask);
        vm.BeginMixerBandHover(ColorMixerBand.Blue, 0x202020);
        await vm.PendingMixerMaskTask;
        Assert.Null(vm.MixerBandMask);
    }

    [AvaloniaTheory]
    [InlineData(ColorMixerBand.Red)]
    [InlineData(ColorMixerBand.Blue)]
    public void RendererUsesEffectiveInfluenceAndPremultipliedThemeTint(ColorMixerBand band)
    {
        using var basis = new BaseImage(new MagickImage(MagickColors.Red, 64, 48) { ColorSpace = ColorSpace.RGB },
            new(BaseSourceKind.Standard, false, BaseDecodeSettings.Default, null, null, 6504, 0, false, null, 1, 64, 48));
        using var pixels = basis.Pixels.GetPixels();
        var before = pixels.GetArea(0, 0, 64, 48)!;
        var lab = OklabColor.Classify(before[0] / 65535d, before[1] / 65535d, before[2] / 65535d);
        var weight = OklabColor.MixerBandInfluence((int)band, Math.Atan2(lab.B, lab.A), lab.Chroma);
        using var mask = MixerBandMaskRenderer.Render(basis, new(), band, new(64, 48), 0x123456, CancellationToken.None);
        using var frame = mask.Lock();
        var first = new byte[4];
        Marshal.Copy(frame.Address, first, 0, 4);
        var alpha = (byte)Math.Round((1 - weight) * .9 * 255);
        Assert.Equal(new byte[] { (byte)(0x56 * alpha / 255), (byte)(0x34 * alpha / 255), (byte)(0x12 * alpha / 255), alpha }, first);
        Assert.Equal(before, pixels.GetArea(0, 0, 64, 48));
    }

    private MainWindowViewModel CreateVm(CatalogService catalog, TimeProvider? clock = null, bool mono = false) =>
        _fixture.CreateViewModel(catalog, new LocalTestLoader(raw: mono, mono: mono, saturated: !mono),
            _ => Task.CompletedTask, new TestSourceAvailabilityService(SourceAvailability.AvailableLocally), timeProvider: clock);

    private async Task<ImageFile> Prepare(MainWindowViewModel vm, CatalogService catalog)
    {
        var image = new ImageFile(_fixture.Path("photo.jpg"));
        image.CatalogId = await catalog.GetOrCreateImageAsync(image.FilePath);
        vm.ShowWorkspaceReady(MainWindowViewModel.CurrentFirstRunExperienceVersion);
        vm.IsDevelopMode = true;
        vm.Browse.SetImages([image]);
        vm.SelectedImage = image;
        await TestWaits.UntilAsync(() => vm.IsHistoryLoaded && vm.PreviewImage != null);

        return image;
    }

    public void Dispose() => _fixture.Dispose();
}
