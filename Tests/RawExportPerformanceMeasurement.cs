using System.Diagnostics;
using HappyPhoton.Models;
using HappyPhoton.Services;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

internal sealed record RawExportMeasurement(
    TimeSpan Elapsed,
    long AfterDecodePrivateBytes,
    long PeakPrivateBytes,
    uint Width,
    uint Height,
    BaseSourceKind SourceKind);

internal static class RawExportPerformanceMeasurement
{
    public const int SamplingIntervalMilliseconds = 10;

    public static async Task<RawExportMeasurement> MeasureAsync(
        string rawPath,
        string outputFolder)
    {
        Directory.CreateDirectory(outputFolder);
        var file = new ImageFile(rawPath)
        {
            EditSettings = new EditSettings
            {
                Detail = new DetailSettings { ChromaNr = 100 }
            }
        };
        var loader = new MeasuringLoader(new BaseLoaderRouter(
            new RawBaseLoader(),
            new StandardBaseLoader()));
        var service = new ImageExportService(
            new RenderPipeline(), loader, new ExportMetadataService());
        var settings = new ExportSettings
        {
            OutputFolder = outputFolder,
            Format = ExportFormat.Jpeg,
            Quality = 85
        };
        var process = Process.GetCurrentProcess();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        process.Refresh();
        var baseline = process.PrivateMemorySize64;
        var peak = baseline;
        var stopwatch = Stopwatch.StartNew();

        var export = service.ExportBatchAsync([file], settings);
        while (!export.IsCompleted)
        {
            await Task.Delay(SamplingIntervalMilliseconds);
            process.Refresh();
            peak = Math.Max(peak, process.PrivateMemorySize64);
        }

        Assert.Equal(1, await export);
        stopwatch.Stop();
        process.Refresh();
        peak = Math.Max(peak, process.PrivateMemorySize64);
        Assert.Equal(BaseSourceKind.RawLibRaw, loader.SourceKind);
        var outputPath = Path.Combine(
            outputFolder,
            $"{Path.GetFileNameWithoutExtension(rawPath)}.jpg");
        var info = new MagickImageInfo(outputPath);
        return new RawExportMeasurement(
            stopwatch.Elapsed,
            Math.Max(0, loader.AfterFullDecodeBytes - baseline),
            Math.Max(0, peak - baseline),
            info.Width,
            info.Height,
            loader.SourceKind!.Value);
    }

    private sealed class MeasuringLoader(IBaseImageLoader inner) : IBaseImageLoader
    {
        public long AfterFullDecodeBytes { get; private set; }
        public BaseSourceKind? SourceKind { get; private set; }

        public bool CanLoad(ImageFile file) => inner.CanLoad(file);

        public BaseImageLoadOutcome LoadPreviewBaseWithOutcome(ImageFile file, BaseDecodeSettings decode, CancellationToken cancellationToken) => inner.LoadPreviewBaseWithOutcome(file, decode, cancellationToken);

        public BaseImage? LoadPreviewBase(
            ImageFile file,
            BaseDecodeSettings decode,
            CancellationToken cancellationToken) =>
            inner.LoadPreviewBase(file, decode, cancellationToken);

        public BaseImage? LoadFullBase(
            ImageFile file,
            BaseDecodeSettings decode,
            CancellationToken cancellationToken)
        {
            var result = inner.LoadFullBase(file, decode, cancellationToken);
            SourceKind = result?.Info.Kind;
            var process = Process.GetCurrentProcess();
            process.Refresh();
            AfterFullDecodeBytes = process.PrivateMemorySize64;
            return result;
        }
    }
}
