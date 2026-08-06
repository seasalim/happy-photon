using HappyPhoton.Models;
using HappyPhoton.Services;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class PreviewBaseCoordinatorTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine(
        Path.GetTempPath(),
        $"HappyPhotonBaseCoordinator_{Guid.NewGuid():N}");

    [Fact]
    public async Task SameIdentity_CoalescesDecodeAndLeasesHeldBase()
    {
        var path = CreateSource("same.jpg");
        var loader = new ControlledLoader(blockFirst: true);
        await using var coordinator = new PreviewBaseCoordinator(loader);
        var file = new ImageFile(path);

        var first = coordinator.GetPreviewAsync(
            file, BaseDecodeSettings.Default, CancellationToken.None);
        Assert.True(loader.FirstDecodeStarted.Wait(TimeSpan.FromSeconds(5)));
        var second = coordinator.GetPreviewAsync(
            file, BaseDecodeSettings.Default, CancellationToken.None);
        loader.ReleaseFirstDecode.Set();

        using var firstResult = await first;
        using var secondResult = await second;
        Assert.Equal(1, loader.DecodeCount);
        Assert.NotNull(firstResult);
        Assert.NotNull(secondResult);
        Assert.Same(firstResult!.Base, secondResult!.Base);
    }

    [Fact]
    public async Task NewIdentity_SupersedesAndDisposesLateResult()
    {
        var firstPath = CreateSource("first.jpg");
        var secondPath = CreateSource("second.jpg");
        var loader = new ControlledLoader(blockFirst: true);
        await using var coordinator = new PreviewBaseCoordinator(loader);

        var first = coordinator.GetPreviewAsync(
            new ImageFile(firstPath),
            BaseDecodeSettings.Default,
            CancellationToken.None);
        Assert.True(loader.FirstDecodeStarted.Wait(TimeSpan.FromSeconds(5)));
        var second = coordinator.GetPreviewAsync(
            new ImageFile(secondPath),
            BaseDecodeSettings.Default,
            CancellationToken.None);

        using var secondResult = await second;
        loader.ReleaseFirstDecode.Set();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        Assert.NotNull(secondResult);
        Assert.Equal(2, loader.DecodeCount);
        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => Task.Run(() => _ = loader.FirstBase!.Pixels));
    }

    [Fact]
    public async Task CancelledWaiter_DoesNotCancelSharedDecode()
    {
        var path = CreateSource("shared.jpg");
        var loader = new ControlledLoader(blockFirst: true);
        await using var coordinator = new PreviewBaseCoordinator(loader);
        using var cancelled = new CancellationTokenSource();

        var first = coordinator.GetPreviewAsync(
            new ImageFile(path),
            BaseDecodeSettings.Default,
            cancelled.Token);
        Assert.True(loader.FirstDecodeStarted.Wait(TimeSpan.FromSeconds(5)));
        var second = coordinator.GetPreviewAsync(
            new ImageFile(path),
            BaseDecodeSettings.Default,
            CancellationToken.None);
        cancelled.Cancel();
        loader.ReleaseFirstDecode.Set();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        using var result = await second;
        Assert.NotNull(result);
        Assert.Equal(1, loader.DecodeCount);
    }

    [Fact]
    public async Task SupersededBase_IsDisposedAfterItsLastRenderLease()
    {
        var firstPath = CreateSource("leased-first.jpg");
        var secondPath = CreateSource("leased-second.jpg");
        var loader = new ControlledLoader(blockFirst: false);
        await using var coordinator = new PreviewBaseCoordinator(loader);

        var first = await coordinator.GetPreviewAsync(
            new ImageFile(firstPath),
            BaseDecodeSettings.Default,
            CancellationToken.None);
        Assert.NotNull(first);
        using var second = await coordinator.GetPreviewAsync(
            new ImageFile(secondPath),
            BaseDecodeSettings.Default,
            CancellationToken.None);

        Assert.NotNull(second);
        Assert.False(second!.IsStale);
        Assert.NotNull(first!.Base.Pixels);
        first.Dispose();
        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => Task.Run(() => _ = loader.FirstBase!.Pixels));
    }

    [Theory]
    [InlineData(FbddMode.Light)]
    [InlineData(FbddMode.Full)]
    public async Task NoiseReductionChange_ReplacesHeldBase(FbddMode mode)
    {
        var path = CreateSource($"fbdd-{mode}.cr2");
        var loader = new ControlledLoader(blockFirst: false);
        await using var coordinator = new PreviewBaseCoordinator(loader);
        var file = new ImageFile(path);
        var replacementDecode = new BaseDecodeSettings(
            HlReconstructionMode.Blend,
            mode);

        using var first = await coordinator.GetPreviewAsync(
            file,
            BaseDecodeSettings.Default,
            CancellationToken.None);
        using var stale = await coordinator.GetPreviewAsync(
            file,
            replacementDecode,
            CancellationToken.None);

        Assert.NotNull(first);
        Assert.NotNull(stale);
        Assert.True(stale!.IsStale);
        Assert.Same(first!.Base, stale.Base);
        await stale.RefreshTask!;

        using var replacement = coordinator.TryAcquireCurrent(
            file,
            replacementDecode);
        Assert.NotNull(replacement);
        Assert.NotSame(first.Base, replacement!.Base);
        Assert.Equal(replacementDecode, replacement.Base.Info.Decode);
        Assert.Null(coordinator.TryAcquireCurrent(
            file,
            BaseDecodeSettings.Default));
        Assert.Equal(2, loader.DecodeCount);
    }

    [Fact]
    public async Task FailedReplacementDecode_RetainsUsableHeldBase()
    {
        var firstPath = CreateSource("retained-first.jpg");
        var loader = new ControlledLoader(blockFirst: false, failSecond: true);
        await using var coordinator = new PreviewBaseCoordinator(loader);
        var file = new ImageFile(firstPath);
        var replacementDecode = new BaseDecodeSettings(
            HlReconstructionMode.Blend,
            FbddMode.Off);

        using var first = await coordinator.GetPreviewAsync(
            file,
            BaseDecodeSettings.Default,
            CancellationToken.None);
        using var stale = await coordinator.GetPreviewAsync(
            file,
            replacementDecode,
            CancellationToken.None);

        Assert.NotNull(first);
        Assert.NotNull(stale);
        Assert.True(stale!.IsStale);
        Assert.Same(first!.Base, stale.Base);
        await stale.RefreshTask!;

        using var retained = await coordinator.GetPreviewAsync(
            file,
            BaseDecodeSettings.Default,
            CancellationToken.None);
        Assert.NotNull(retained);
        Assert.False(retained!.IsStale);
        Assert.Same(first.Base, retained.Base);
        Assert.Equal(2, loader.DecodeCount);
    }

    private string CreateSource(string name)
    {
        Directory.CreateDirectory(_tempDirectory);
        var path = Path.Combine(_tempDirectory, name);
        File.WriteAllBytes(path, []);
        return path;
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }

    private sealed class ControlledLoader : IBaseImageLoader
    {
        private readonly bool _blockFirst;
        private readonly bool _failSecond;
        private int _decodeCount;

        public ManualResetEventSlim FirstDecodeStarted { get; } = new();
        public ManualResetEventSlim ReleaseFirstDecode { get; } = new();
        public int DecodeCount => Volatile.Read(ref _decodeCount);
        public BaseImage? FirstBase { get; private set; }

        public ControlledLoader(bool blockFirst, bool failSecond = false)
        {
            _blockFirst = blockFirst;
            _failSecond = failSecond;
        }

        public bool CanLoad(ImageFile file) => true;

        public BaseImage? LoadPreviewBase(
            ImageFile file,
            BaseDecodeSettings decode,
            CancellationToken cancellationToken)
        {
            var call = Interlocked.Increment(ref _decodeCount);
            if (_blockFirst && call == 1)
            {
                FirstDecodeStarted.Set();
                ReleaseFirstDecode.Wait();
            }
            if (call != 1)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }
            if (_failSecond && call == 2)
            {
                return null;
            }

            var result = new BaseImage(
                new MagickImage(
                    call == 1 ? MagickColors.Red : MagickColors.Blue,
                    16,
                    12),
                CreateInfo(decode));
            if (call == 1)
            {
                FirstBase = result;
            }
            return result;
        }

        public BaseImage? LoadFullBase(
            ImageFile file,
            BaseDecodeSettings decode,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        private static BaseImageInfo CreateInfo(BaseDecodeSettings decode) =>
            new(
                BaseSourceKind.Standard,
                false,
                decode,
                null,
                null,
                6504,
                0,
                false,
                null,
                1,
                16,
                12);
    }

}
