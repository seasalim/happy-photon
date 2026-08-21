using HappyPhoton.LibRaw.Interop;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class RawDecodeFailureStatusTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        $"happy-photon-raw-status-{Guid.NewGuid():N}");

    [Fact]
    public async Task UnsupportedFailure_PersistsAcrossSelectionAndRetryClearsIt()
    {
        using var catalog = new CatalogService(_root);
        var vm = CreateViewModel(catalog);
        var raw = new ImageFile(Path.Combine(_root, "unsupported.nef"));
        var other = new ImageFile(Path.Combine(_root, "other.jpg"));
        vm.SelectedImage = raw;

        vm.ApplyPreviewLoadOutcome(new PreviewLoadOutcome(
            raw,
            vm.LatestPreviewOutcomeGeneration,
            BaseImageLoadFailure.UnsupportedRaw));

        Assert.True(raw.RawDecodeFailed);
        Assert.True(raw.HasVisibleLoadFailure);
        Assert.Contains("unsupported encoding", vm.StatusMessage);
        vm.SelectedImage = other;
        vm.SelectedImage = raw;
        Assert.Contains("unsupported encoding", vm.StatusMessage);

        vm.ApplyPreviewLoadOutcome(new PreviewLoadOutcome(
            raw,
            vm.LatestPreviewOutcomeGeneration,
            BaseImageLoadFailure.None));
        Assert.False(raw.RawDecodeFailed);
        Assert.False(raw.HasVisibleLoadFailure);
        Assert.Null(vm.StatusMessage);
        await vm.DisposeAsync();
    }

    [Fact]
    public async Task StaleSelectionAndGenerationOutcomesCannotPinOrClearFailure()
    {
        using var catalog = new CatalogService(_root);
        var vm = CreateViewModel(catalog);
        var first = new ImageFile(Path.Combine(_root, "first.dng"));
        var second = new ImageFile(Path.Combine(_root, "second.dng"));
        vm.SelectedImage = first;
        vm.SelectedImage = second;

        vm.ApplyPreviewLoadOutcome(new PreviewLoadOutcome(
            first,
            vm.LatestPreviewOutcomeGeneration - 1,
            BaseImageLoadFailure.UnsupportedRaw));
        Assert.False(first.RawDecodeFailed);

        vm.ApplyPreviewLoadOutcome(new PreviewLoadOutcome(
            second,
            vm.LatestPreviewOutcomeGeneration,
            BaseImageLoadFailure.UnsupportedRaw));
        vm.ApplyPreviewLoadOutcome(new PreviewLoadOutcome(
            second,
            vm.LatestPreviewOutcomeGeneration - 1,
            BaseImageLoadFailure.None));
        Assert.True(second.RawDecodeFailed);
        Assert.Contains("could not be decoded", vm.StatusMessage);
        await vm.DisposeAsync();
    }

    [Fact]
    public async Task SourceFailureOutranksFileRuntimeAndTransientReasons()
    {
        using var catalog = new CatalogService(_root);
        var vm = CreateViewModel(catalog, RejectedHealth());
        var raw = new ImageFile(Path.Combine(_root, "priority.cr3"));
        vm.SelectedImage = raw;
        vm.TransientStatus = "transient";
        vm.ApplyPreviewLoadOutcome(new PreviewLoadOutcome(
            raw,
            vm.LatestPreviewOutcomeGeneration,
            BaseImageLoadFailure.UnsupportedRaw));
        Assert.Contains("could not be decoded", vm.StatusMessage);

        vm.ApplyThumbnailLoadStatus(
            raw,
            ThumbnailLoadStatus.DeferredForHydration);
        Assert.Contains("online-only", vm.StatusMessage);
        await vm.DisposeAsync();
    }

    [Fact]
    public async Task RuntimeRejectionIsGlobalAndDoesNotMarkIndividualTile()
    {
        using var catalog = new CatalogService(_root);
        var vm = CreateViewModel(catalog, RejectedHealth());
        var raw = new ImageFile(Path.Combine(_root, "runtime.raf"));
        vm.SelectedImage = raw;

        vm.ApplyPreviewLoadOutcome(new PreviewLoadOutcome(
            raw,
            vm.LatestPreviewOutcomeGeneration,
            BaseImageLoadFailure.RawRuntimeUnavailable));

        Assert.False(raw.RawDecodeFailed);
        Assert.Contains("Reinstall Happy Photon", vm.StatusMessage);
        await vm.DisposeAsync();
    }

    [Fact]
    public void ThumbnailFailureMarkerRemainsVisibleWithOrWithoutPixels()
    {
        var image = new ImageFile(Path.Combine(_root, "thumb.orf"));
        image.ThumbnailLoadFailed = true;
        Assert.True(image.HasVisibleLoadFailure);

        image.ThumbnailLoadFailed = false;
        image.RawDecodeFailed = true;
        Assert.True(image.HasVisibleLoadFailure);
        Assert.Contains("Nikon HE", image.LoadFailureText);
    }

    private static MainWindowViewModel CreateViewModel(
        CatalogService catalog,
        LibRawRuntimeHealth? health = null) =>
        new(
            catalog,
            new NullLoader(),
            loadMetadataAsync: _ => Task.CompletedTask,
            availabilityService: new TestSourceAvailabilityService(
                SourceAvailability.AvailableLocally),
            rawRuntimeHealth: health ?? HealthyHealth());

    private static LibRawRuntimeHealth HealthyHealth() =>
        LibRawRuntimeHealthEvaluator.Evaluate(new(
            LibRawOutputConfiguration.Version,
            LibRawRuntimeHealthEvaluator.SupportedLibRawVersion,
            "0.22.2-Release",
            LibRawCapabilities.Jpeg | LibRawCapabilities.Zlib));

    private static LibRawRuntimeHealth RejectedHealth() =>
        LibRawRuntimeHealthEvaluator.Evaluate(new(
            LibRawOutputConfiguration.Version,
            0x001601,
            "0.22.1-Release",
            LibRawCapabilities.Jpeg | LibRawCapabilities.Zlib));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private sealed class NullLoader : IBaseImageLoader
    {
        public bool CanLoad(ImageFile file) => true;

        BaseImageLoadOutcome IBaseImageLoader.LoadPreviewBaseWithOutcome(ImageFile file, BaseDecodeSettings decode, CancellationToken cancellationToken) => BaseImageLoadOutcome.FromImage(LoadPreviewBase(file, decode, cancellationToken), BaseImageLoadFailure.UnsupportedRaw);

        public BaseImage? LoadPreviewBase(
            ImageFile file,
            BaseDecodeSettings decode,
            CancellationToken cancellationToken) => null;

        public BaseImage? LoadFullBase(
            ImageFile file,
            BaseDecodeSettings decode,
            CancellationToken cancellationToken) => null;
    }
}
