using Avalonia.Media.Imaging;
using HappyPhoton.Models;
using HappyPhoton.Services;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

[Collection(AvaloniaTestCollection.Name)]
public sealed class DcpAtomicityTests : IDisposable
{
    private readonly AvaloniaTestFixture _fixture;
    private readonly TemporaryDirectory _directory = new();

    public DcpAtomicityTests(AvaloniaTestFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task RapidAtoBtoC_InstallsOnlyNewestMatrixAndTablePayload()
    {
        var path = Path.Combine(_directory.Path, "image.cr2");
        File.WriteAllBytes(path, []);
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(-2));
        var image = new ImageFile(path);
        using var catalog = new CatalogService(Path.Combine(_directory.Path, "catalog"));
        await catalog.InitializeAsync();
        await image.EnsureCatalogIdAsync(catalog);
        var loader = new ProfileLoader();
        await using var coordinator = new PreviewBaseCoordinator(loader);
        var a = Decode('a', 0.8f);
        var b = Decode('b', 0.9f);
        var c = Decode('c', 1.1f);

        var requestA = coordinator.GetPreviewAsync(
            image, a, CancellationToken.None);
        Assert.True(loader.StartedA.Wait(TestWaits.Condition));
        var requestB = coordinator.GetPreviewAsync(
            image, b, CancellationToken.None);
        Assert.True(loader.StartedB.Wait(TestWaits.Condition));
        using var installed = await coordinator.GetPreviewAsync(
            image, c, CancellationToken.None);

        loader.ReleaseA.Set();
        loader.ReleaseB.Set();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => requestA);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => requestB);

        Assert.NotNull(installed);
        Assert.Equal(c.ProfileResolution!.Token,
            installed!.Base.Info.ProfileToken);
        Assert.Equal("Profile c", installed.Base.Info.DcpProfile?.Name);
        Assert.Equal(1.1f,
            installed.Base.Info.DcpProfile?.HueSatMap?.Table1[4]);
        Assert.Equal(9, installed.Base.Info.CamToSrgb?[0, 0]);
        Assert.Null(coordinator.TryAcquireCurrent(image, a));
        Assert.Null(coordinator.TryAcquireCurrent(image, b));
        using var current = coordinator.TryAcquireCurrent(image, c);
        Assert.NotNull(current);

        var settings = new EditSettings
        {
            RawProfile = c.ProfileSelection?.Clone()
        };
        var installedHash = RenderSettingsHash.Compute(
            settings,
            installed.Base.Info.ProfileToken);
        Assert.NotEqual(installedHash,
            RenderSettingsHash.Compute(settings, a.ProfileResolution!.Token));
        Assert.NotEqual(installedHash,
            RenderSettingsHash.Compute(settings, b.ProfileResolution!.Token));

        using var rendered = new RenderPipeline().Render(new RenderRequest(
            installed.Base,
            settings,
            RenderIntent.Preview,
            BaseImage.InteractivePreviewMaxDimension,
            new RenderOptions(false, false)));
        // The disposed writer's drained output is read back below, so the
        // drain gets the standard wait ceiling instead of the production
        // shutdown timeout.
        await using (var writer = new PreviewCacheService(
            catalog,
            8,
            Task.CompletedTask,
            TestWaits.Condition))
        {
            writer.QueueSaveToCache(image, rendered.Image, installedHash);
        }
        await using var reloadedWriter = new PreviewCacheService(catalog);
        using var reloaded = reloadedWriter.LoadRenderedPreview(image);

        Assert.NotNull(reloaded);
        Assert.Equal(installedHash, reloaded!.SettingsHash);
        Assert.NotEqual(
            RenderSettingsHash.Compute(settings, a.ProfileResolution.Token),
            reloaded.SettingsHash);
        Assert.NotEqual(
            RenderSettingsHash.Compute(settings, b.ProfileResolution.Token),
            reloaded.SettingsHash);
        Assert.True(
            MeanAbsoluteDifference(rendered.Image, reloaded.Image) < 1500,
            "The reloaded cache pixels do not match C's installed matrix/table render.");
    }

    [WindowsFact]
    public async Task RapidAtoBtoC_PromotesAndReloadsOnlyNewestRenderedPayload()
    {
        _fixture.RequireWindows();
        var path = Path.Combine(_directory.Path, "rendered.cr2");
        File.WriteAllBytes(path, []);
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(-2));
        var image = new ImageFile(path);
        using var catalog = new CatalogService(Path.Combine(
            _directory.Path,
            "rendered-catalog"));
        await catalog.InitializeAsync();
        await image.EnsureCatalogIdAsync(catalog);
        var settingsA = WriteSettings('a', 0.8f);
        var settingsB = WriteSettings('b', 0.9f);
        var settingsC = WriteSettings('c', 1.1f);
        var loader = new ProfileLoader();
        // The reader below asserts on the drained cache contents, so the
        // drain gets the standard wait ceiling: an expired production timeout
        // would abandon C's write and make the A/B Null asserts vacuous.
        var renderedCache = new RenderedThumbnailCacheService(
            catalog,
            8,
            Task.CompletedTask,
            TestWaits.Condition);
        await using var service = new PreviewService(
            catalog,
            loader,
            new RenderPipeline(),
            new PreviewCacheService(catalog),
            renderedCache,
            dcpProfiles: new DcpProfileService(
                new TestSourceAvailabilityService(
                    SourceAvailability.AvailableLocally)));
        var thumbnailCreated = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        service.RenderedThumbnailCreated += () => thumbnailCreated.TrySetResult();

        var requestA = service.ApplyEditsToPreviewArtifactsAsync(
            image,
            settingsA,
            ThumbnailSizeRequest.For(BrowseThumbnailSize.Medium),
            skipHistogram: true,
            ClippingOverlaySide.None);
        Assert.True(loader.StartedA.Wait(TestWaits.Condition));
        var requestB = service.ApplyEditsToPreviewArtifactsAsync(
            image,
            settingsB,
            ThumbnailSizeRequest.For(BrowseThumbnailSize.Medium),
            skipHistogram: true,
            ClippingOverlaySide.None);
        Assert.True(loader.StartedB.Wait(TestWaits.Condition));
        using var newest = await service.ApplyEditsToPreviewArtifactsAsync(
            image,
            settingsC,
            ThumbnailSizeRequest.For(BrowseThumbnailSize.Medium),
            skipHistogram: true,
            ClippingOverlaySide.None);
        newest.CommitPromotion();

        loader.ReleaseA.Set();
        loader.ReleaseB.Set();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => requestA);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => requestB);
        await thumbnailCreated.Task.WaitAsync(TestWaits.Condition);
        Assert.Equal(
            settingsC.RawProfile!.CacheToken,
            newest.ProfileState?.Token);
        Assert.Equal(DcpProfileErrorCode.None, newest.ProfileState?.Status);
        Assert.Equal("Profile c", newest.ProfileState?.ProfileName);
        Assert.Null(service.TryPromoteRenderedThumbnail(image, settingsA));
        Assert.Null(service.TryPromoteRenderedThumbnail(image, settingsB));
        using var promoted = service.TryPromoteRenderedThumbnail(image, settingsC);
        Assert.NotNull(promoted);

        service.ClearPreviewCache();
        await service.DisposeAsync();
        await using var reader = new RenderedThumbnailCacheService(catalog);
        var hashA = RenderSettingsHash.Compute(settingsA);
        var hashB = RenderSettingsHash.Compute(settingsB);
        var hashC = RenderSettingsHash.Compute(settingsC);
        Assert.Null(reader.LoadMatching(image, hashA));
        Assert.Null(reader.LoadMatching(image, hashB));
        using var restored = reader.LoadMatching(image, hashC);
        Assert.NotNull(restored);
        Assert.Equal(promoted!.PixelSize, restored!.PixelSize);
        Assert.True(BitmapDifference(promoted, restored) < 8);
    }

    private EditSettings WriteSettings(char id, float saturationScale)
    {
        var profilePath = SyntheticDcpFactory.WriteTemporary(
            _directory.Path,
            new SyntheticDcpOptions
            {
                Name = $"Profile {id}",
                ForwardMatrix1 = DcpProfileReaderTests.D50Forward(
                    id == 'a' ? 0.8 : id == 'b' ? 0.9 : 1.1),
                HueSatDimensions = [2, 2, 1],
                HueSatTable1 = DcpProfileReaderTests.CreateTable(
                    2, 2, 1, 0, saturationScale, 1)
            },
            $"{id}.dcp");
        var snapshot = new DcpProfileReader().ReadExternalSnapshot(profilePath);
        return new EditSettings
        {
            RawProfile = new RawProfileSelection
            {
                Source = RawProfileSource.UserFile,
                Location = profilePath,
                ContentHash = snapshot.ContentHash
            }
        };
    }

    private static double BitmapDifference(Bitmap left, Bitmap right)
    {
        var first = BitmapConversionService.CopyBgraPixels(left);
        var second = BitmapConversionService.CopyBgraPixels(right);
        Assert.Equal(first.Length, second.Length);
        return first.Zip(second, (a, b) => Math.Abs(a - b)).Average();
    }

    private static double MeanAbsoluteDifference(
        MagickImage expected,
        MagickImage actual)
    {
        using var expectedPixels = expected.GetPixels();
        using var actualPixels = actual.GetPixels();
        var left = expectedPixels.ToShortArray(PixelMapping.RGB)!;
        var right = actualPixels.ToShortArray(PixelMapping.RGB)!;
        Assert.Equal(left.Length, right.Length);
        return left.Zip(right, (first, second) =>
                Math.Abs((int)first - second))
            .Average();
    }

    private static BaseDecodeSettings Decode(char id, float saturationScale)
    {
        var selection = new RawProfileSelection
        {
            Source = RawProfileSource.UserFile,
            Location = $"{id}.dcp",
            ContentHash = new string(id, 64)
        };
        var profile = new DcpProfile(
            $"Profile {id}",
            "Synthetic",
            ChromaticAdaptation.Identity(),
            null,
            null,
            null,
            21,
            null,
            string.Empty,
            3,
            2,
            2,
            1,
            false,
            DcpProfileReaderTests.CreateTable(
                2, 2, 1, 0, saturationScale, 1),
            null,
            selection.ContentHash);
        return new BaseDecodeSettings(
            HlReconstructionMode.Clip)
        {
            ProfileSelection = selection,
            ProfileResolution = DcpProfileResolution.Success(selection, profile)
        };
    }

    public void Dispose() => _directory.Dispose();

    private sealed class ProfileLoader : IBaseImageLoader
    {
        internal ManualResetEventSlim StartedA { get; } = new();
        internal ManualResetEventSlim StartedB { get; } = new();
        internal ManualResetEventSlim ReleaseA { get; } = new();
        internal ManualResetEventSlim ReleaseB { get; } = new();

        public bool CanLoad(ImageFile file) => true;

        public BaseImageLoadOutcome LoadPreviewBaseWithOutcome(
            ImageFile file,
            BaseDecodeSettings decode,
            CancellationToken cancellationToken) =>
            BaseImageLoadOutcome.FromImage(
                LoadPreviewBase(file, decode, cancellationToken),
                BaseImageLoadFailure.DecodeFailed);

        public BaseImage? LoadPreviewBase(
            ImageFile file,
            BaseDecodeSettings decode,
            CancellationToken cancellationToken)
        {
            var resolution = decode.ProfileResolution!;
            var id = char.ToLowerInvariant(resolution.Profile!.Name[^1]);
            if (id == 'a')
            {
                StartedA.Set();
                ReleaseA.Wait(TestWaits.Condition);
            }
            else if (id == 'b')
            {
                StartedB.Set();
                ReleaseB.Wait(TestWaits.Condition);
            }
            var table = resolution.Profile!.HueSatTable1!;
            var payload = new DcpProfilePayload(
                resolution.Token,
                resolution.Profile.Name,
                new DcpHueSatMap(2, 2, 1, false, table, null, 0));
            var marker = id == 'a' ? 3 : id == 'b' ? 6 : 9;
            var color = id == 'a'
                ? MagickColors.Orange
                : id == 'b'
                    ? MagickColors.Green
                    : MagickColors.Blue;
            return new BaseImage(
                new MagickImage(color, 8, 8),
                new BaseImageInfo(
                    BaseSourceKind.RawLibRaw,
                    true,
                    decode,
                    [1, 1, 1],
                    new double[,] { { marker, 0, 0 }, { 0, 1, 0 }, { 0, 0, 1 } },
                    6500,
                    0,
                    HadIccProfile: false,
                    IccDescription: null,
                    ExifOrientationApplied: 1,
                    FullWidth: 8,
                    FullHeight: 8)
                {
                    DcpProfile = payload,
                    ProfileToken = resolution.Token
                });
        }

        public BaseImage? LoadFullBase(
            ImageFile file,
            BaseDecodeSettings decode,
            CancellationToken cancellationToken) =>
            LoadPreviewBase(file, decode, cancellationToken);
    }
}
