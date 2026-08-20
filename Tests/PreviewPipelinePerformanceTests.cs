using System.Diagnostics;
using HappyPhoton.Models;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

[Collection(AvaloniaTestCollection.Name)]
public sealed class PreviewPipelinePerformanceTests
{
    private readonly AvaloniaTestFixture _fixture;
    private readonly ITestOutputHelper _output;

    public PreviewPipelinePerformanceTests(
        AvaloniaTestFixture fixture,
        ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    [WindowsFact]
    public async Task DevelopEntryLatencyAndMemory_WhenEnabled()
    {
        _fixture.RequireWindows();
        if (Environment.GetEnvironmentVariable("HAPPY_PHOTON_PERF") != "1")
        {
            return;
        }

        var root = Path.Combine(
            Path.GetTempPath(),
            $"HappyPhotonPreviewPerf_{Guid.NewGuid():N}");
        try
        {
            using var catalog = new CatalogService(root);
            await catalog.InitializeAsync();
            await Measure(
                catalog,
                "JPEG",
                new ImageFile(Asset("display-p3-reference.jpg")));
            await Measure(
                catalog,
                "RAW",
                new ImageFile(Asset("canon-eos-350d.cr2")));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [WindowsFact]
    public async Task RawCandidateLatency_WhenEnabled()
    {
        _fixture.RequireWindows();
        if (Environment.GetEnvironmentVariable("HAPPY_PHOTON_PERF") != "1")
        {
            return;
        }

        var root = Path.Combine(
            Path.GetTempPath(),
            $"HappyPhotonRawCandidatePerf_{Guid.NewGuid():N}");
        try
        {
            using var catalog = new CatalogService(root);
            await catalog.InitializeAsync();
            var file = new ImageFile(Asset("canon-eos-350d.cr2"));
            await CompareRawCandidateCost(
                catalog,
                file,
                ThumbnailSizeRequest.For(LibraryThumbnailSize.Medium),
                "192px");
            await CompareRawCandidateCost(
                catalog,
                file,
                ThumbnailSizeRequest.For(LibraryThumbnailSize.Large),
                "512px");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [WindowsFact]
    public async Task RenderedThumbnailCacheLatency_WhenEnabled()
    {
        _fixture.RequireWindows();
        if (Environment.GetEnvironmentVariable("HAPPY_PHOTON_PERF") != "1")
        {
            return;
        }

        var root = Path.Combine(
            Path.GetTempPath(),
            $"HappyPhotonRenderedThumbPerf_{Guid.NewGuid():N}");
        try
        {
            using var catalog = new CatalogService(root);
            await catalog.InitializeAsync();
            var files = await CreateCachedThumbnails(catalog, 100);
            var sourceCache = new ThumbnailCacheService(catalog);
            var renderedCache = new RenderedThumbnailCacheService(catalog);
            var hash = RenderSettingsHash.Compute(
                new EditSettings { Exposure = 0.5 });

            foreach (var size in Enum.GetValues<LibraryThumbnailSize>())
            {
                var request = ThumbnailSizeRequest.For(size);
                MeasureCache(files, file => sourceCache.LoadFromCache(
                    file, request, out _, out _));
                MeasureCache(files, file => renderedCache.LoadMatching(
                    file, hash, request, out _));
                var sourceMedian = Median(Enumerable.Range(0, 3)
                    .Select(_sample => MeasureCache(
                        files,
                        file => sourceCache.LoadFromCache(
                            file, request, out _, out _))));
                var renderedMedian = Median(Enumerable.Range(0, 3)
                    .Select(_sample => MeasureCache(
                        files,
                        file => renderedCache.LoadMatching(
                            file, hash, request, out _))));

                _output.WriteLine(
                    $"100 {size} source thumbnails: {sourceMedian:F1} ms; " +
                    $"rendered thumbnails: {renderedMedian:F1} ms");
                Assert.True(
                    renderedMedian <= sourceMedian * 1.15,
                    $"{size} rendered thumbnail cache was " +
                    $"{renderedMedian / sourceMedian:P1} of source-cache latency.");
            }
            await sourceCache.DisposeAsync();
            await renderedCache.DisposeAsync();
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [WindowsFact]
    public void LibraryHistogramLatency_WhenEnabled()
    {
        _fixture.RequireWindows();
        if (Environment.GetEnvironmentVariable("HAPPY_PHOTON_PERF") != "1")
        {
            return;
        }

        using var source = new ImageMagick.MagickImage(
            ImageMagick.MagickColors.Orange,
            512,
            341);
        using var bitmap = BitmapConversionService.ConvertToBitmap(source)!;
        var histogramService = new HistogramService();
        using (var warmup = BitmapConversionService.CloneBitmap(bitmap))
        {
            histogramService.CalculateLibraryHistogram(warmup);
        }

        var samples = new List<double>();
        for (var index = 0; index < 9; index++)
        {
            var stopwatch = Stopwatch.StartNew();
            using var snapshot = BitmapConversionService.CloneBitmap(bitmap);
            histogramService.CalculateLibraryHistogram(snapshot);
            stopwatch.Stop();
            samples.Add(stopwatch.Elapsed.TotalMilliseconds);
        }

        var median = Median(samples);
        _output.WriteLine($"512px Library histogram median: {median:F1} ms");
        Assert.True(median <= 100, $"Library histogram median was {median:F1} ms.");
    }

    private async Task CompareRawCandidateCost(
        CatalogService catalog,
        ImageFile file,
        ThumbnailSizeRequest request,
        string requestLabel)
    {
        await using var baseline = CreateService(
            catalog,
            createRenderedThumbnail: false);
        await using var enabled = CreateService(
            catalog,
            createRenderedThumbnail: true);
        var warm = new EditSettings { Exposure = 0.25 };
        DisposePreview(await baseline.ApplyEditsToPreviewAsync(
            file, warm, request, skipHistogram: true));
        DisposePreview(await enabled.ApplyEditsToPreviewAsync(
            file, warm, request, skipHistogram: true));

        ForceCollection();
        var baselineSamples = new List<double>();
        var enabledSamples = new List<double>();
        for (var index = 0; index < 7; index++)
        {
            var settings = new EditSettings
            {
                Exposure = 0.25 + index * 0.05,
                Saturation = 10
            };
            if (index % 2 == 0)
            {
                baselineSamples.Add(await MeasureRender(
                    baseline, file, settings, request));
                enabledSamples.Add(await MeasureRender(
                    enabled, file, settings, request));
            }
            else
            {
                enabledSamples.Add(await MeasureRender(
                    enabled, file, settings, request));
                baselineSamples.Add(await MeasureRender(
                    baseline, file, settings, request));
            }
        }
        var baselineMedian = Median(baselineSamples);
        var enabledMedian = Median(enabledSamples);
        _output.WriteLine(
            $"RAW {requestLabel} preview median without thumbnail: " +
            $"{baselineMedian:F1} ms; " +
            $"with thumbnail: {enabledMedian:F1} ms");
        Assert.True(
            enabledMedian <= 150,
            $"RAW {requestLabel} render with thumbnail exceeded 150 ms: " +
            $"{enabledMedian:F1} ms (baseline {baselineMedian:F1} ms).");
    }

    private static async Task<double> MeasureRender(
        PreviewService service,
        ImageFile file,
        EditSettings settings,
        ThumbnailSizeRequest request)
    {
        var stopwatch = Stopwatch.StartNew();
        var result = await service.ApplyEditsToPreviewAsync(
            file,
            settings,
            request,
            skipHistogram: true);
        stopwatch.Stop();
        result.preview?.Dispose();
        return stopwatch.Elapsed.TotalMilliseconds;
    }

    private static void DisposePreview(
        (Avalonia.Media.Imaging.Bitmap? preview, HistogramData histogram) result) =>
        result.preview?.Dispose();

    private static async Task<List<ImageFile>> CreateCachedThumbnails(
        CatalogService catalog,
        int count)
    {
        using var image = new ImageMagick.MagickImage(
            ImageMagick.MagickColors.Orange,
            512,
            341);
        image.Quality = 85;
        var jpeg = image.ToByteArray(ImageMagick.MagickFormat.Jpeg);
        var settingsHash = RenderSettingsHash.Compute(
            new EditSettings { Exposure = 0.5 });
        var files = new List<ImageFile>(count);
        for (var index = 0; index < count; index++)
        {
            var sourcePath = Path.Combine(catalog.CatalogPath, $"source-{index}.dng");
            await File.WriteAllBytesAsync(sourcePath, [1]);
            File.SetLastWriteTimeUtc(sourcePath, DateTime.UtcNow.AddMinutes(-2));
            var file = new ImageFile(sourcePath);
            await file.EnsureCatalogIdAsync(catalog);
            var sourceCachePath = catalog.GetThumbnailPath(file.CatalogId);
            var renderedCachePath = catalog.GetRenderedThumbnailPath(file.CatalogId);
            Directory.CreateDirectory(Path.GetDirectoryName(sourceCachePath)!);
            Directory.CreateDirectory(Path.GetDirectoryName(renderedCachePath)!);
            await File.WriteAllBytesAsync(sourceCachePath, jpeg);
            await File.WriteAllBytesAsync(renderedCachePath, jpeg);
            await File.WriteAllTextAsync(
                Path.ChangeExtension(renderedCachePath, ".meta"),
                settingsHash);
            files.Add(file);
        }
        return files;
    }

    private static double MeasureCache(
        IReadOnlyList<ImageFile> files,
        Func<ImageFile, Avalonia.Media.Imaging.Bitmap?> load)
    {
        var stopwatch = Stopwatch.StartNew();
        foreach (var file in files)
        {
            using var bitmap = load(file);
            Assert.NotNull(bitmap);
        }
        stopwatch.Stop();
        return stopwatch.Elapsed.TotalMilliseconds;
    }

    private static double Median(IEnumerable<double> samples)
    {
        var ordered = samples.Order().ToArray();
        return ordered[ordered.Length / 2];
    }

    private async Task Measure(
        CatalogService catalog,
        string label,
        ImageFile file)
    {
        ForceCollection();
        var process = Process.GetCurrentProcess();
        process.Refresh();
        var memoryBefore = process.PrivateMemorySize64;
        var stopwatch = Stopwatch.StartNew();

        await using (var service = CreateService(catalog))
        {
            var (preview, _) = await service.LoadPreviewWithHistogramAsync(
                file,
                file.EditSettings,
                skipHistogram: true);
            stopwatch.Stop();
            Assert.NotNull(preview);
            process.Refresh();
            var pixels = (long)preview!.PixelSize.Width * preview.PixelSize.Height;
            var ownedPixelBytes = pixels * (6 + 4);
            _output.WriteLine(
                $"{label} fresh base + v{RenderPipeline.Version} render: " +
                $"{stopwatch.Elapsed.TotalMilliseconds:F1} ms, " +
                $"private-memory delta " +
                $"{(process.PrivateMemorySize64 - memoryBefore) / 1048576.0:F1} MiB, " +
                $"steady owned pixels {ownedPixelBytes / 1048576.0:F1} MiB");

            var coreSettings = new EditSettings
            {
                Exposure = 0.5,
                Saturation = 10
            };
            using var corePreview = await MeasureSliderTick(
                service,
                file,
                label,
                "core",
                coreSettings);
            var detailSettings = new EditSettings
            {
                Exposure = 0.5,
                Detail = new DetailSettings { ChromaNr = 100 }
            };
            using var detailPreview = await MeasureSliderTick(
                service,
                file,
                label,
                "chroma NR",
                detailSettings);
            var leaveStopwatch = Stopwatch.StartNew();
            service.ClearPreviewCache();
            leaveStopwatch.Stop();
            _output.WriteLine(
                $"{label} image-leave cache enqueue: " +
                $"{leaveStopwatch.Elapsed.TotalMilliseconds:F1} ms");
            preview.Dispose();
        }

        await using var reader = CreateService(catalog);
        stopwatch.Restart();
        using var cached = await reader.LoadCachedPreviewAsync(
            file,
            new EditSettings
            {
                Exposure = 0.5,
                Detail = new DetailSettings { ChromaNr = 100 }
            });
        stopwatch.Stop();
        Assert.NotNull(cached);
        Assert.True(cached!.SettingsMatch);
        _output.WriteLine(
            $"{label} rendered-cache paint: " +
            $"{stopwatch.Elapsed.TotalMilliseconds:F1} ms");
    }

    private async Task<Avalonia.Media.Imaging.Bitmap> MeasureSliderTick(
        PreviewService service,
        ImageFile file,
        string sourceLabel,
        string operationLabel,
        EditSettings settings)
    {
        ForceCollection();
        var samples = new List<double>();
        Avalonia.Media.Imaging.Bitmap? preview = null;
        for (var index = 0; index < 5; index++)
        {
            preview?.Dispose();
            var stopwatch = Stopwatch.StartNew();
            (preview, _) = await service.ApplyEditsToPreviewAsync(
                file,
                settings,
                skipHistogram: true);
            stopwatch.Stop();
            Assert.NotNull(preview);
            samples.Add(stopwatch.Elapsed.TotalMilliseconds);
        }
        var median = Median(samples);
        _output.WriteLine(
            $"{sourceLabel} {operationLabel} slider-tick " +
            $"v{RenderPipeline.Version} render: " +
            $"{median:F1} ms median");
        Assert.True(
            median <= 150,
            $"{sourceLabel} {operationLabel} slider tick exceeded 150 ms: " +
            $"{median:F1} ms median.");
        return preview!;
    }

    private static PreviewService CreateService(CatalogService catalog) =>
        CreateService(catalog, createRenderedThumbnail: true);

    private static PreviewService CreateService(
        CatalogService catalog,
        bool createRenderedThumbnail) =>
        new(
            catalog,
            new BaseLoaderRouter(
                new RawBaseLoader(),
                new StandardBaseLoader()),
            new RenderPipeline(),
            new HistogramService(),
            new PreviewCacheService(catalog),
            new RenderedThumbnailCacheService(catalog),
            createRenderedThumbnail: createRenderedThumbnail);

    private static string Asset(string fileName) =>
        Path.Combine(GoldenTestPaths.AssetDirectory, fileName);

    private static void ForceCollection()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }
}
