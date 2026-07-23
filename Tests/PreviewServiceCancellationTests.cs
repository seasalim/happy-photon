using ImageMagick;
using HappyPhoton.Models;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

[CollectionDefinition(PreviewServiceCancellationTestCollection.Name, DisableParallelization = true)]
public sealed class PreviewServiceCancellationTestCollection
{
    public const string Name = "Preview service cancellation";
}

[Collection(PreviewServiceCancellationTestCollection.Name)]
public sealed class PreviewServiceCancellationTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine(
        Path.GetTempPath(), $"HappyPhotonPreviewTests_{Guid.NewGuid():N}");
    private CatalogService? _catalogService;

    [Fact]
    public async Task UncachedRawPreview_CancelledQueuedRequestDoesNotDecode()
    {
        Directory.CreateDirectory(_tempDirectory);
        var firstPath = Path.Combine(_tempDirectory, "first.dng");
        var secondPath = Path.Combine(_tempDirectory, "second.dng");
        File.WriteAllBytes(firstPath, []);
        File.WriteAllBytes(secondPath, []);

        _catalogService = new CatalogService(Path.Combine(_tempDirectory, "catalog"));
        await _catalogService.InitializeAsync();

        var rawService = new BlockingRawProcessingService();
        var editService = new EditApplicationService();
        await using var previewService = new PreviewService(
            _catalogService,
            rawService,
            editService,
            new HistogramService());
        var firstImage = new ImageFile(firstPath) { CatalogId = 1 };
        var secondImage = new ImageFile(secondPath) { CatalogId = 2 };
        using var firstCts = new CancellationTokenSource();
        using var secondCts = new CancellationTokenSource();

        var firstLoad = previewService.LoadPreviewWithHistogramAsync(
            firstImage, firstImage.EditSettings, skipHistogram: true, firstCts.Token);

        try
        {
            Assert.True(rawService.WaitForFirstDecode(TimeSpan.FromSeconds(5)));

            var secondLoad = previewService.LoadPreviewWithHistogramAsync(
                secondImage, secondImage.EditSettings, skipHistogram: true, secondCts.Token);
            secondCts.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => secondLoad.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.Equal(1, rawService.DecodeCount);
        }
        finally
        {
            firstCts.Cancel();
            rawService.ReleaseFirstDecode();
        }

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => firstLoad.WaitAsync(TimeSpan.FromSeconds(5)));
        previewService.ClearPreviewCache();
    }

    public void Dispose()
    {
        _catalogService?.Dispose();
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }

    private sealed class BlockingRawProcessingService : IRawProcessingService
    {
        private readonly ManualResetEventSlim _firstDecodeStarted = new();
        private readonly ManualResetEventSlim _releaseFirstDecode = new();
        private int _decodeCount;

        public bool IsAvailable => true;

        public int DecodeCount => Volatile.Read(ref _decodeCount);

        public MagickImage? DecodeHalfSize(string filePath)
        {
            if (Interlocked.Increment(ref _decodeCount) == 1)
            {
                _firstDecodeStarted.Set();
                _releaseFirstDecode.Wait();
            }

            return new MagickImage(MagickColors.Black, 32, 24);
        }

        public bool WaitForFirstDecode(TimeSpan timeout) => _firstDecodeStarted.Wait(timeout);

        public void ReleaseFirstDecode() => _releaseFirstDecode.Set();

        public byte[]? ExtractThumbnail(string filePath) => null;

        public MagickImage? DecodeFull(string filePath) => null;

        public RawMetadata? ExtractMetadata(string filePath) => null;
    }
}
