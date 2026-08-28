using System.Collections.Concurrent;
using System.Reflection;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using Xunit;

namespace HappyPhoton.Tests;

[Collection(AvaloniaTestCollection.Name)]
public sealed class ThumbnailPairingPerformanceTests
{
    private const int SampleCount = 5;

    private readonly ITestOutputHelper _output;

    public ThumbnailPairingPerformanceTests(ITestOutputHelper output) =>
        _output = output;

    [WindowsFact]
    public async Task PairingFixture_ReportsThumbnailSourceReadsAndInitialBatch()
    {
        Assert.SkipWhen(
            Environment.GetEnvironmentVariable("HAPPY_PHOTON_PERF") != "1",
            "Set HAPPY_PHOTON_PERF=1 to run thumbnail diagnostics.");

        var samples = new ThumbnailReadSample[SampleCount];
        for (var index = 0; index < samples.Length; index++)
            samples[index] = await MeasureAsync(index);

        _output.WriteLine(
            $"pairing_fixture_thumbnail_distinct_source_reads=[{string.Join(", ", samples.Select(sample => sample.SourceFiles.Count))}]");
        _output.WriteLine(
            $"pairing_fixture_initial_batch_counts=[{string.Join(", ", samples.Select(sample => sample.InitialBatchFiles.Count))}]");
        for (var index = 0; index < samples.Length; index++)
        {
            _output.WriteLine(
                $"pairing_fixture_run_{index + 1}_source_files=[{string.Join(", ", samples[index].SourceFiles)}]");
            _output.WriteLine(
                $"pairing_fixture_run_{index + 1}_initial_batch_files=[{string.Join(", ", samples[index].InitialBatchFiles)}]");
        }

        Assert.All(samples, sample => Assert.True(
            samples[0].SourceFiles.SequenceEqual(
                sample.SourceFiles,
                StringComparer.OrdinalIgnoreCase)));
        Assert.All(samples, sample => Assert.True(
            sample.SourceFiles.SequenceEqual(
                sample.InitialBatchFiles,
                StringComparer.OrdinalIgnoreCase)));
        Assert.All(samples, sample => Assert.Equal(
            ["jpeg-only.jpg", "pair-a.jpg", "pair-b.jpg", "pair-c.jpg", "raw-only.dng"],
            sample.InitialBatchFiles));
        Assert.All(samples, sample => Assert.Equal(
            ["pair-a.dng", "pair-b.dng", "pair-c.dng"],
            sample.UnpairedSourceFiles));
    }

    private static async Task<ThumbnailReadSample> MeasureAsync(int sample)
    {
        using var fixture = new CatalogVmFixture($"thumbnail-pairing-{sample}");
        var photos = fixture.Path("photos");
        Directory.CreateDirectory(photos);
        WritePairingFixture(photos);
        using var catalog = await fixture.CreateCatalogAsync("catalog");
        await using var viewModel = fixture.CreateViewModel(
            catalog,
            loadMetadataAsync: _ => Task.CompletedTask,
            availabilityService: new TestSourceAvailabilityService(
                SourceAvailability.AvailableLocally),
            postSelection: _ => { });
        var sourceFiles = new ConcurrentDictionary<string, byte>(
            StringComparer.OrdinalIgnoreCase);
        var initialBatchFiles = new ConcurrentDictionary<string, byte>(
            StringComparer.OrdinalIgnoreCase);
        ReplaceThumbnailSource(viewModel, image =>
        {
            sourceFiles.TryAdd(image.FileName, 0);
            if (viewModel.InitialThumbnailBatchCount > 0)
                initialBatchFiles.TryAdd(image.FileName, 0);
            return CreateBitmap();
        });

        await viewModel.LoadFolderAsync(photos);
        await TestWaits.UntilAsync(() =>
            viewModel.InitialThumbnailBatchCount == 0 &&
            viewModel.Browse.VisibleImages.All(image => image.Thumbnail != null));

        var pairedSourceFiles = sourceFiles.Keys
            .Order(StringComparer.OrdinalIgnoreCase).ToArray();
        viewModel.ShowCapturePairs = false;
        await TestWaits.UntilAsync(() =>
            viewModel.Browse.AllImages.All(image => image.Thumbnail != null));

        return new ThumbnailReadSample(
            pairedSourceFiles,
            initialBatchFiles.Keys.Order(StringComparer.OrdinalIgnoreCase).ToArray(),
            sourceFiles.Keys.Except(pairedSourceFiles, StringComparer.OrdinalIgnoreCase)
                .Order(StringComparer.OrdinalIgnoreCase).ToArray());
    }

    private static void ReplaceThumbnailSource(
        MainWindowViewModel viewModel,
        Func<ImageFile, Bitmap?> loadSource)
    {
        var field = typeof(ThumbnailService).GetField(
            "_loadSource",
            BindingFlags.Instance | BindingFlags.NonPublic) ??
            throw new InvalidOperationException("Thumbnail source seam not found.");
        field.SetValue(
            viewModel.ImageService.Thumbnails,
            new Func<ImageFile, int, CancellationToken, Bitmap?>(
                (image, _, _) => loadSource(image)));
    }

    private static void WritePairingFixture(string folder)
    {
        foreach (var name in new[]
                 {
                     "pair-a.jpg", "pair-a.dng",
                     "pair-b.jpg", "pair-b.dng",
                     "pair-c.jpg", "pair-c.dng",
                     "raw-only.dng", "jpeg-only.jpg"
                 })
        {
            TestImages.WriteJpeg(Path.Combine(folder, name));
        }
    }

    private static WriteableBitmap CreateBitmap() => new(
        new PixelSize(16, 16),
        new Vector(96, 96),
        PixelFormat.Bgra8888,
        AlphaFormat.Opaque);

    private sealed record ThumbnailReadSample(
        IReadOnlyList<string> SourceFiles,
        IReadOnlyList<string> InitialBatchFiles,
        IReadOnlyList<string> UnpairedSourceFiles);
}
