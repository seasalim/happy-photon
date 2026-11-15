using HappyPhoton.LibRaw.Interop;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class RawDecodeFailureStatusTests : IDisposable
{
    private readonly TemporaryDirectory _root = new();

    [Fact]
    public async Task UnsupportedFailure_PersistsAcrossSelectionAndRetryClearsIt()
    {
        using var catalog = new CatalogService(_root.Path);
        var vm = CreateViewModel(catalog);
        var raw = new ImageFile(Path.Combine(_root.Path, "unsupported.nef"));
        var other = new ImageFile(Path.Combine(_root.Path, "other.jpg"));
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
        using var catalog = new CatalogService(_root.Path);
        var vm = CreateViewModel(catalog);
        var first = new ImageFile(Path.Combine(_root.Path, "first.dng"));
        var second = new ImageFile(Path.Combine(_root.Path, "second.dng"));
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
        using var catalog = new CatalogService(_root.Path);
        var vm = CreateViewModel(catalog, RejectedHealth());
        var raw = new ImageFile(Path.Combine(_root.Path, "priority.cr3"));
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
        using var catalog = new CatalogService(_root.Path);
        var vm = CreateViewModel(catalog, RejectedHealth());
        var raw = new ImageFile(Path.Combine(_root.Path, "runtime.raf"));
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
        var image = new ImageFile(Path.Combine(_root.Path, "thumb.orf"));
        image.ThumbnailLoadFailed = true;
        Assert.True(image.HasVisibleLoadFailure);

        image.ThumbnailLoadFailed = false;
        image.RawDecodeFailed = true;
        Assert.True(image.HasVisibleLoadFailure);
        Assert.Contains("Nikon HE", image.LoadFailureText);
    }

    [Theory]
    [InlineData("broken.jpg")]
    [InlineData("broken.tiff")]
    public async Task StandardDecodeFailure_ShowsStatusAndClearsOnRetryOrSelection(string name)
    {
        var file = new ImageFile(Path.Combine(_root.Path, name));
        File.WriteAllBytes(file.FilePath, [0, 1, 2, 3]);
        var loader = new StandardBaseLoader();
        var outcome = loader.LoadPreviewBaseWithOutcome(
            file, BaseDecodeSettings.Default, CancellationToken.None);
        Assert.Equal(BaseImageLoadFailure.DecodeFailed, outcome.Failure);
        using var catalog = new CatalogService(Path.Combine(_root.Path, "catalog"));
        await using var vm = CreateViewModel(catalog);
        vm.SelectedImage = file;
        vm.ApplyPreviewLoadOutcome(new PreviewLoadOutcome(
            file, vm.LatestPreviewOutcomeGeneration, outcome.Failure));

        Assert.Equal("This file could not be decoded.", vm.StatusMessage);
        Assert.False(file.RawDecodeFailed);
        vm.ApplyPreviewLoadOutcome(new PreviewLoadOutcome(
            file, vm.LatestPreviewOutcomeGeneration, BaseImageLoadFailure.None));
        Assert.Null(vm.StatusMessage);
        vm.ApplyPreviewLoadOutcome(new PreviewLoadOutcome(
            file, vm.LatestPreviewOutcomeGeneration, outcome.Failure));
        vm.SelectedImage = new ImageFile(Path.Combine(_root.Path, "other.jpg"));
        Assert.Null(vm.StatusMessage);
    }

    private static MainWindowViewModel CreateViewModel(
        CatalogService catalog,
        LibRawRuntimeHealth? health = null) =>
        new(
            catalog,
            new NullBaseLoader(BaseImageLoadFailure.UnsupportedRaw),
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

    public void Dispose() => _root.Dispose();
}
