using System.Security.Cryptography;
using HappyPhoton.Models;
using HappyPhoton.Services;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

[Collection(AvaloniaTestCollection.Name)]
public sealed class RawProfileRenderCorrelationTests : IDisposable
{
    private readonly string _root = Directory.CreateDirectory(Path.Combine(
        Path.GetTempPath(),
        $"happy-photon-profile-correlation-{Guid.NewGuid():N}")).FullName;

    [WindowsFact]
    public async Task ReusedBaseCarriesCurrentRequestLocationInOutcome()
    {
        using var catalog = new CatalogService(Path.Combine(_root, "catalog"));
        await catalog.InitializeAsync();
        var firstPath = SyntheticDcpFactory.WriteTemporary(
            _root,
            new SyntheticDcpOptions { Name = "Shared profile" },
            "first.dcp");
        var secondPath = Path.Combine(_root, "second.dcp");
        File.Copy(firstPath, secondPath);
        var hash = Convert.ToHexString(SHA256.HashData(
            File.ReadAllBytes(firstPath))).ToLowerInvariant();
        var first = Settings(firstPath, hash);
        var second = Settings(secondPath, hash);
        var loader = new CountingProfileLoader();
        await using var service = new PreviewService(
            catalog,
            loader,
            new RenderPipeline(),
            dcpProfiles: new DcpProfileService(
                new TestSourceAvailabilityService(
                    SourceAvailability.AvailableLocally)));
        var image = new ImageFile(Path.Combine(_root, "image.dng"));

        using var firstArtifacts = await service.ApplyEditsToPreviewArtifactsAsync(
            image,
            first,
            ThumbnailSizeRequest.For(LibraryThumbnailSize.Medium),
            skipHistogram: true,
            ClippingOverlaySide.None);
        var firstDecodeCount = loader.DecodeCount;
        using var secondArtifacts = await service.ApplyEditsToPreviewArtifactsAsync(
            image,
            second,
            ThumbnailSizeRequest.For(LibraryThumbnailSize.Medium),
            skipHistogram: true,
            ClippingOverlaySide.None);

        Assert.Equal(firstDecodeCount, loader.DecodeCount);
        Assert.Equal(
            Path.GetFullPath(firstPath),
            firstArtifacts.ProfileState?.RequestedSelection?.Location);
        Assert.Equal(
            Path.GetFullPath(secondPath),
            secondArtifacts.ProfileState?.RequestedSelection?.Location);
        Assert.NotSame(
            second.RawProfile,
            secondArtifacts.ProfileState?.RequestedSelection);
    }

    private static EditSettings Settings(string path, string hash) => new()
    {
        RawProfile = new RawProfileSelection
        {
            Source = RawProfileSource.UserFile,
            Location = Path.GetFullPath(path),
            ContentHash = hash
        }
    };

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private sealed class CountingProfileLoader : IBaseImageLoader
    {
        private int _decodeCount;
        internal int DecodeCount => Volatile.Read(ref _decodeCount);

        public bool CanLoad(ImageFile file) => true;

        public BaseImage LoadPreviewBase(
            ImageFile file,
            BaseDecodeSettings decode,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _decodeCount);
            var resolution = decode.ProfileResolution!;
            return new BaseImage(
                new MagickImage(MagickColors.Gray, 16, 12),
                new BaseImageInfo(
                    BaseSourceKind.RawLibRaw,
                    true,
                    decode,
                    [1, 1, 1],
                    new double[,] { { 1, 0, 0 }, { 0, 1, 0 }, { 0, 0, 1 } },
                    6500,
                    0,
                    HadIccProfile: false,
                    IccDescription: null,
                    ExifOrientationApplied: 1,
                    FullWidth: 16,
                    FullHeight: 12)
                {
                    DcpProfile = new DcpProfilePayload(
                        resolution.Token,
                        resolution.Profile!.Name,
                        HueSatMap: null),
                    ProfileToken = resolution.Token,
                    CameraIdentity = new CameraIdentity("Canon", "EOS R5")
                });
        }

        public BaseImageLoadOutcome LoadPreviewBaseWithOutcome(
            ImageFile file,
            BaseDecodeSettings decode,
            CancellationToken cancellationToken) =>
            BaseImageLoadOutcome.Loaded(
                LoadPreviewBase(file, decode, cancellationToken));

        public BaseImage? LoadFullBase(
            ImageFile file,
            BaseDecodeSettings decode,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
