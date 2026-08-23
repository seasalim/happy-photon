using HappyPhoton.Models;
using HappyPhoton.Services;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class BaseImageContractsTests
{
    [Fact]
    public void DecodeSettings_DefaultAndCacheKeyArePinned()
    {
        var settings = BaseDecodeSettings.From(new EditSettings());

        Assert.Same(BaseDecodeSettings.Default, settings);
        Assert.Equal(HlReconstructionMode.Clip, settings.HlReconstruction);
        Assert.Equal(FbddMode.Off, settings.NoiseReduction);
        Assert.Equal("base-v15;hl=clip;fbdd=off;lens=110", settings.CacheKey);
        Assert.Equal(1600, BaseImage.InteractivePreviewMaxDimension);
        Assert.Equal(3200, BaseImage.LargePreviewMaxDimension);
    }

    [Fact]
    public void DecodeSettings_AllCacheKeysAreUnique()
    {
        var keys = Enum.GetValues<HlReconstructionMode>()
            .SelectMany(highlight => Enum.GetValues<FbddMode>()
                .Select(noise => new BaseDecodeSettings(highlight, noise).CacheKey))
            .ToArray();

        Assert.Equal(keys.Length, keys.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void DecodeSettings_ProjectsV2DecodeAffectingSubset()
    {
        var settings = new EditSettings
        {
            HlReconstruction = HlReconstructionMode.Clip,
            Detail = new DetailSettings { NoiseReduction = FbddMode.Full }
        };

        var decode = BaseDecodeSettings.From(settings);

        Assert.Equal(HlReconstructionMode.Clip, decode.HlReconstruction);
        Assert.Equal(FbddMode.Full, decode.NoiseReduction);
        Assert.Equal("base-v15;hl=clip;fbdd=full;lens=110", decode.CacheKey);
    }

    [Fact]
    public void DecodeSettings_ChromaNoiseReductionDoesNotInvalidateBase()
    {
        var settings = new EditSettings
        {
            Detail = new DetailSettings { ChromaNr = 100 }
        };

        Assert.Same(BaseDecodeSettings.Default, BaseDecodeSettings.From(settings));
    }

    [Fact]
    public void DecodeSettings_ProfileOutcomeTokenInvalidatesBaseCache()
    {
        var selection = new RawProfileSelection
        {
            Source = RawProfileSource.UserFile,
            Location = "synthetic.dcp",
            ContentHash = new string('a', 64)
        };
        var requested = BaseDecodeSettings.From(new EditSettings
        {
            RawProfile = selection
        });
        var rejected = requested.WithProfileResolution(
            DcpProfileResolution.Rejected(
                selection,
                DcpProfileErrorCode.HashMismatch,
                "changed",
                new string('b', 64)));

        Assert.Contains($"dcp={selection.CacheToken}", requested.CacheKey);
        Assert.NotEqual(requested.CacheKey, rejected.CacheKey);
        Assert.Contains("hash-mismatch", rejected.CacheKey);
    }

    [Fact]
    public void DecodeSettings_BuiltInResolutionPreservesDefaultRecordIdentity()
    {
        var resolved = BaseDecodeSettings.Default.WithProfileResolution(
            DcpProfileResolution.BuiltIn);

        Assert.Equal(BaseDecodeSettings.Default, resolved);
        Assert.Equal(BaseDecodeSettings.Default.CacheKey, resolved.CacheKey);
    }

    [Fact]
    public void BaseImage_DisposeReleasesOwnedPixelsAndIsIdempotent()
    {
        var pixels = new MagickImage(MagickColors.Black, 2, 2);
        var image = new BaseImage(pixels, CreateInfo(BaseSourceKind.Standard));

        Assert.Same(pixels, image.Pixels);

        image.Dispose();
        image.Dispose();

        Assert.Throws<ObjectDisposedException>(() => image.Pixels);
        Assert.Throws<ObjectDisposedException>(() => pixels.Width);
    }

    [Fact]
    public void Router_UsesStandardLoaderForNonRawPreview()
    {
        using var expected = CreateBase(BaseSourceKind.Standard);
        var raw = new StubLoader();
        var standard = new StubLoader { PreviewResult = expected };
        var router = CreateRouter(raw, standard);
        var file = new ImageFile("photo.heic");

        var actual = router.LoadPreviewBase(
            file,
            BaseDecodeSettings.Default,
            CancellationToken.None);

        Assert.Same(expected, actual);
        Assert.Equal(0, raw.PreviewCalls);
        Assert.Equal(1, standard.PreviewCalls);
    }

    [Fact]
    public void Router_UsesRawLoaderForRawFullBase()
    {
        using var expected = CreateBase(BaseSourceKind.RawLibRaw, isRaw: true);
        var raw = new StubLoader { FullResult = expected };
        var standard = new StubLoader();
        var router = CreateRouter(raw, standard);

        var actual = router.LoadFullBase(
            new ImageFile("photo.nef"),
            BaseDecodeSettings.Default,
            CancellationToken.None);

        Assert.Same(expected, actual);
        Assert.Equal(1, raw.FullCalls);
        Assert.Equal(0, standard.FullCalls);
    }

    [Fact]
    public void Router_RawFailureNeverCallsStandardLoader()
    {
        var standard = new StubLoader();
        var router = CreateRouter(
            new StubLoader(),
            standard);

        var actual = router.LoadPreviewBase(
            new ImageFile("photo.cr2"),
            BaseDecodeSettings.Default,
            CancellationToken.None);

        Assert.Null(actual);
        Assert.Equal(0, standard.PreviewCalls);
    }

    [Fact]
    public void Router_CancellationStopsBeforeAnyLoaderCall()
    {
        var raw = new StubLoader();
        var standard = new StubLoader();
        var router = CreateRouter(raw, standard);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(() =>
            router.LoadPreviewBase(
                new ImageFile("photo.dng"),
                BaseDecodeSettings.Default,
                cancellation.Token));
        Assert.Equal(0, raw.PreviewCalls);
        Assert.Equal(0, standard.PreviewCalls);
    }

    private static BaseLoaderRouter CreateRouter(
        StubLoader raw,
        StubLoader standard) =>
        new(raw, standard);

    private static BaseImage CreateBase(
        BaseSourceKind kind,
        bool isRaw = false) =>
        new(
            new MagickImage(MagickColors.Black, 1, 1),
            CreateInfo(kind, isRaw));

    private static BaseImageInfo CreateInfo(
        BaseSourceKind kind,
        bool isRaw = false) =>
        new(
            kind,
            isRaw,
            BaseDecodeSettings.Default,
            null,
            null,
            isRaw ? 5500 : 6504,
            0,
            false,
            null,
            1,
            1,
            1);

    private sealed class StubLoader : IBaseImageLoader
    {
        public BaseImage? PreviewResult { get; init; }
        public BaseImage? FullResult { get; init; }
        public int PreviewCalls { get; private set; }
        public int FullCalls { get; private set; }

        public bool CanLoad(ImageFile file) => true;

        BaseImageLoadOutcome IBaseImageLoader.LoadPreviewBaseWithOutcome(ImageFile file, BaseDecodeSettings decode, CancellationToken cancellationToken) => BaseImageLoadOutcome.FromImage(LoadPreviewBase(file, decode, cancellationToken), BaseImageLoadFailure.DecodeFailed);

        public BaseImage? LoadPreviewBase(
            ImageFile file,
            BaseDecodeSettings decode,
            CancellationToken cancellationToken)
        {
            PreviewCalls++;
            return PreviewResult;
        }

        public BaseImage? LoadFullBase(
            ImageFile file,
            BaseDecodeSettings decode,
            CancellationToken cancellationToken)
        {
            FullCalls++;
            return FullResult;
        }
    }
}
