using Avalonia;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

[Collection(AvaloniaTestCollection.Name)]
public sealed class ViewportZoomAnchorTests : IAsyncLifetime
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        $"HappyPhotonZoomAnchor_{Guid.NewGuid():N}");
    private CatalogService _catalog = null!;
    private ImageFile _image = null!;

    public async ValueTask InitializeAsync()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "source.jpg");
        await File.WriteAllBytesAsync(path, [0]);
        _image = new ImageFile(path);
        _catalog = new CatalogService(_directory);
        await _catalog.InitializeAsync();
    }

    public async ValueTask DisposeAsync()
    {
        _catalog.Dispose();
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private MainWindowViewModel CreateViewModel(
        IBaseImageLoader loader,
        TimeProvider clock) =>
        new(
            _catalog,
            loader,
            loadMetadataAsync: _ => Task.CompletedTask,
            availabilityService: new TestSourceAvailabilityService(
                SourceAvailability.AvailableLocally),
            timeProvider: clock);

    [WindowsFact]
    public async Task ProvisionalZoom_RescalesWhenTheTrueOriginalArrives()
    {
        var loader = new GatedPairLoader();
        var viewModel = CreateViewModel(loader, new TestTimeProvider());
        viewModel.IsDevelopMode = true;
        viewModel.SelectedImage = _image;
        Assert.True(loader.DecodeStarted.Wait(TestWaits.Condition));

        // Entry window: an unidentified cached paint is on screen and the
        // user zooms manually. The zoom value carries the provisional
        // bitmap-as-original meaning (the view's fallback).
        using var source = new MagickImage(MagickColors.Gray, 160, 100);
        var cached = BitmapConversionService.ConvertToBitmap(source)!;
        viewModel.ReplacePreviewImage(cached, PreviewPaintSource.CachedJpeg);
        viewModel.ApplyManualZoom(1.6);
        Assert.Equal(default, viewModel.OriginalViewPixelSize);
        var fitRequests = 0;
        viewModel.RequestZoomFit = () =>
        {
            fitRequests++;
            viewModel.ApplyFitZoom(0.36);
        };

        loader.Release.Set();
        await TestWaits.UntilAsync(() =>
            viewModel.OriginalViewPixelSize.Width > 0);

        // The true original (400 long edge) re-anchors the zoom while
        // preserving on-screen geometry: 1.6 × 160/400 = 0.64 — and the
        // entry refit must not fire over the user's zoom (no snap-back).
        Assert.Equal(new PixelSize(400, 200), viewModel.OriginalViewPixelSize);
        Assert.False(viewModel.IsZoomFitMode);
        Assert.Equal(0.64, viewModel.ZoomLevel, 10);
        Assert.Equal(0, fitRequests);
        await viewModel.DisposeAsync();
    }

    [WindowsFact]
    public async Task EntryWithoutUserZoom_StillRequestsTheFit()
    {
        var loader = new GatedPairLoader();
        var viewModel = CreateViewModel(loader, new TestTimeProvider());
        var fitRequests = 0;
        viewModel.RequestZoomFit = () => fitRequests++;
        viewModel.IsDevelopMode = true;
        viewModel.ApplyManualZoom(2.0);
        viewModel.SelectedImage = _image;
        Assert.True(loader.DecodeStarted.Wait(TestWaits.Condition));
        loader.Release.Set();
        await TestWaits.UntilAsync(() => fitRequests > 0);

        // Manual zoom from a PREVIOUS image never suppresses the next entry's
        // fit: the load start re-declares fit intent.
        Assert.Equal(1, fitRequests);
        Assert.True(viewModel.IsZoomFitMode);
        await viewModel.DisposeAsync();
    }

    [WindowsFact]
    public async Task ModeRoundTrip_KeepsRestingArmed()
    {
        var lines = new List<string>();
        using var trace = ImageServiceHelpers.OverrideDisplayTraceForTesting(
            enabled: true,
            line => { lock (lines) lines.Add(line); });
        var clock = new TestTimeProvider();
        var loader = new CountingPairLoader();
        var viewModel = CreateViewModel(loader, clock);
        viewModel.PublishRequiredDeviceLongEdge(200);
        viewModel.IsDevelopMode = true;
        viewModel.SelectedImage = _image;
        await TestWaits.UntilAsync(() => viewModel.PreviewImage != null);
        clock.Advance(TimeSpan.FromMilliseconds(300));
        await TestWaits.UntilAsync(() => viewModel.HasArmedRestingRender);
        clock.Advance(TimeSpan.FromMilliseconds(75));
        await TestWaits.UntilAsync(() => viewModel.RestingPaintCount >= 1);

        // Round-trip the mode on the same image, then grow the viewport:
        // the resting parent must survive (in-flight work cancels, arming
        // does not — the permanently-unarmed round-trip defect).
        viewModel.IsDevelopMode = false;
        viewModel.IsDevelopMode = true;
        var paints = viewModel.RestingPaintCount;
        viewModel.PublishRequiredDeviceLongEdge(280);
        try
        {
            // The re-entry fresh paint re-arms asynchronously; advance the
            // settle clock on every poll so whichever tick armed last elapses.
            await TestWaits.UntilAsync(() =>
            {
                clock.Advance(TimeSpan.FromMilliseconds(75));
                return viewModel.RestingPaintCount > paints;
            });
        }
        catch (Exception ex)
        {
            string dump;
            lock (lines)
            {
                dump = string.Join(" | ", lines.TakeLast(12));
            }
            throw new Xunit.Sdk.XunitException(
                $"{ex.Message} TRACE: {dump}");
        }

        Assert.True(viewModel.RestingPaintCount > paints);
        await viewModel.DisposeAsync();
    }
}
