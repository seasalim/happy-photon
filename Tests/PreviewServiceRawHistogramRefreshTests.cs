using HappyPhoton.Models;
using HappyPhoton.Services;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

[Collection(AvaloniaTestCollection.Name)]
public sealed class PreviewServiceRawHistogramRefreshTests : IDisposable
{
    private readonly AvaloniaTestFixture _fixture;
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), $"happy-photon-raw-hist-{Guid.NewGuid():N}");

    public PreviewServiceRawHistogramRefreshTests(AvaloniaTestFixture fixture) =>
        _fixture = fixture;

    [WindowsFact]
    public async Task Accessor_ExactOnlyAndNeverStartsIo()
    {
        _fixture.RequireWindows();
        Directory.CreateDirectory(_root);
        using var catalog = new CatalogService(Path.Combine(_root, "catalog"));
        await catalog.InitializeAsync();
        var loader = new HistogramLoader();
        var file = new ImageFile(Path.Combine(_root, "held.dng"));
        await using var service = CreateService(catalog, loader);
        var (preview, _) = await service.LoadPreviewWithHistogramAsync(
            file, new EditSettings(), skipHistogram: true);
        preview?.Dispose();
        var decodeCount = loader.DecodeCount;

        Assert.Same(loader.LastHistogram, service.TryGetRawHistogram(
            file, BaseDecodeSettings.Default));
        Assert.Null(service.TryGetRawHistogram(
            new ImageFile(Path.Combine(_root, "other.dng")),
            BaseDecodeSettings.Default));
        Assert.Null(service.TryGetRawHistogram(file,
            new BaseDecodeSettings(HlReconstructionMode.Blend, FbddMode.Off)));
        Assert.Equal(decodeCount, loader.DecodeCount);

        service.ClearPreviewCache();
        Assert.Null(service.TryGetRawHistogram(file, BaseDecodeSettings.Default));
        Assert.Equal(decodeCount, loader.DecodeCount);
    }

    [WindowsFact]
    public async Task ReplacementRefresh_CarriesReplacementHistogramByIdentity()
    {
        _fixture.RequireWindows();
        Directory.CreateDirectory(_root);
        using var catalog = new CatalogService(Path.Combine(_root, "refresh-catalog"));
        await catalog.InitializeAsync();
        var loader = new HistogramLoader();
        var file = new ImageFile(Path.Combine(_root, "refresh.dng"));
        await using var service = CreateService(catalog, loader);
        var (initial, _) = await service.LoadPreviewWithHistogramAsync(
            file, new EditSettings(), skipHistogram: true);
        initial?.Dispose();
        var refreshed = new TaskCompletionSource<HistogramData?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        service.PreviewRefreshed += (_, refresh) =>
            refreshed.TrySetResult(refresh.RawHistogram);

        var settings = new EditSettings
        {
            HlReconstruction = HlReconstructionMode.Blend
        };
        var (stale, _) = await service.ApplyEditsToPreviewAsync(
            file, settings, skipHistogram: true);
        stale?.Dispose();
        var carried = await refreshed.Task.WaitAsync(TestWaits.Condition);

        Assert.Same(loader.LastHistogram, carried);
        Assert.Equal(HistogramDomain.RawSensor, carried!.Domain);
    }

    [WindowsFact]
    public async Task ClippingRequestCannotDowngradePendingReplacementHistogram()
    {
        _fixture.RequireWindows();
        Directory.CreateDirectory(_root);
        using var catalog = new CatalogService(Path.Combine(_root, "coalesce-catalog"));
        await catalog.InitializeAsync();
        var loader = new HistogramLoader { GateReplacement = true };
        var file = new ImageFile(Path.Combine(_root, "coalesce.dng"));
        await using var service = CreateService(catalog, loader);
        using (var initial = await service.ApplyEditsToPreviewArtifactsAsync(
                   file,
                   new EditSettings(),
                   ThumbnailSizeRequest.For(LibraryThumbnailSize.Medium),
                   skipHistogram: false,
                   ClippingOverlaySide.None,
                   surfaceGeneration: 1))
        {
            Assert.NotNull(initial.Bitmap);
        }

        var refreshed = new TaskCompletionSource<(bool HasHistogram,
            HistogramData Histogram)>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        service.PreviewRefreshed += (_, refresh) => refreshed.TrySetResult(
            (refresh.HasHistogram, refresh.Histogram));
        var settings = new EditSettings
        {
            HlReconstruction = HlReconstructionMode.Blend
        };

        try
        {
            using var stateDefining = await service.ApplyEditsToPreviewArtifactsAsync(
                file,
                settings,
                ThumbnailSizeRequest.For(LibraryThumbnailSize.Medium),
                skipHistogram: false,
                ClippingOverlaySide.None,
                surfaceGeneration: 2);
            Assert.True(loader.ReplacementStarted.Wait(TestWaits.Condition));

            using var clipping = await service.ApplyEditsToPreviewArtifactsAsync(
                file,
                settings,
                ThumbnailSizeRequest.For(LibraryThumbnailSize.Medium),
                skipHistogram: true,
                ClippingOverlaySide.Both,
                surfaceGeneration: 2);
            loader.ReleaseReplacement.Set();
            var result = await refreshed.Task.WaitAsync(TestWaits.Condition);

            Assert.True(result.HasHistogram);
            Assert.Contains(result.Histogram.Luminance, value => value > 0);
        }
        finally
        {
            loader.ReleaseReplacement.Set();
        }
    }

    [WindowsFact]
    public async Task DisposedAccessor_ReturnsNullWithoutIo()
    {
        _fixture.RequireWindows();
        Directory.CreateDirectory(_root);
        using var catalog = new CatalogService(Path.Combine(_root, "disposed-catalog"));
        await catalog.InitializeAsync();
        var loader = new HistogramLoader();
        var file = new ImageFile(Path.Combine(_root, "disposed.dng"));
        var service = CreateService(catalog, loader);
        await service.DisposeAsync();

        Assert.Null(service.TryGetRawHistogram(file, BaseDecodeSettings.Default));
        Assert.Equal(0, loader.DecodeCount);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private static PreviewService CreateService(
        CatalogService catalog,
        IBaseImageLoader loader) =>
        new(catalog, loader, new RenderPipeline());

    private sealed class HistogramLoader : IBaseImageLoader
    {
        public int DecodeCount { get; private set; }
        public HistogramData? LastHistogram { get; private set; }
        public bool GateReplacement { get; init; }
        public ManualResetEventSlim ReplacementStarted { get; } = new();
        public ManualResetEventSlim ReleaseReplacement { get; } = new();
        public bool CanLoad(ImageFile file) => true;

        public BaseImage? LoadPreviewBase(ImageFile file, BaseDecodeSettings decode,
            CancellationToken cancellationToken)
        {
            DecodeCount++;
            if (GateReplacement && DecodeCount == 2)
            {
                ReplacementStarted.Set();
                ReleaseReplacement.Wait(cancellationToken);
            }
            LastHistogram = new HistogramData { Domain = HistogramDomain.RawSensor };
            LastHistogram.Red[DecodeCount] = 1;
            LastHistogram.Normalize();
            return new BaseImage(
                new MagickImage(MagickColors.Gray, 32, 24)
                {
                    Depth = 16,
                    ColorSpace = ColorSpace.RGB
                },
                new BaseImageInfo(BaseSourceKind.RawLibRaw, true, decode,
                    null, null, 5500, 0, false, null, 1, 32, 24));
        }

        BaseImageLoadOutcome IBaseImageLoader.LoadPreviewBaseWithOutcome(
            ImageFile file, BaseDecodeSettings decode,
            CancellationToken cancellationToken) =>
            BaseImageLoadOutcome.FromImage(
                LoadPreviewBase(file, decode, cancellationToken),
                BaseImageLoadFailure.DecodeFailed,
                new PreviewSourceAnalysis(LastHistogram, null));

        public BaseImage? LoadFullBase(ImageFile file, BaseDecodeSettings decode,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
