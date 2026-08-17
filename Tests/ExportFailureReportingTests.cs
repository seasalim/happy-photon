using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class ExportFailureReportingTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        $"happy-photon-export-failures-{Guid.NewGuid():N}");

    public ExportFailureReportingTests() => Directory.CreateDirectory(_root);

    [Fact]
    public async Task MixedBatch_SurfacesEveryFailureWithoutCompleteProgress()
    {
        var exported = new ImageFile(Path.Combine(_root, "exported.dng"));
        var failedA = new ImageFile(Path.Combine(_root, "failed-a.dng"));
        var failedB = new ImageFile(Path.Combine(_root, "failed-b.dng"));
        var service = new ImageExportService(
            new RenderPipeline(),
            new SelectiveBaseLoader(exported),
            new ExportMetadataService());
        var progress = new RecordingProgress();
        var settings = new ExportSettings
        {
            OutputFolder = Path.Combine(_root, "output"),
            Format = ExportFormat.Png
        };

        var result = await service.ExportBatchAsync(
            [exported, failedA, failedB],
            settings,
            progress);
        using var viewModel = new ExportDialogViewModel(settings, 3);
        viewModel.BeginExport();
        foreach (var value in progress.Values)
        {
            viewModel.UpdateProgress(
                value.current,
                value.total,
                value.fileName);
        }
        viewModel.ShowPartialExport(result);

        Assert.Equal(1, result.ExportedCount);
        Assert.Equal([failedA, failedB], result.FailedImages);
        Assert.DoesNotContain(
            progress.Values,
            value => value.fileName == "Complete");
        Assert.False(viewModel.IsExporting);
        Assert.True(viewModel.HasError);
        Assert.Contains("Exported 1 of 3 images", viewModel.ErrorMessage);
        Assert.Contains(failedA.FilePath, viewModel.ErrorMessage);
        Assert.Contains(failedB.FilePath, viewModel.ErrorMessage);
        Assert.DoesNotContain("Complete", viewModel.ProgressText);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private sealed class SelectiveBaseLoader(ImageFile successfulImage) :
        IBaseImageLoader
    {
        public bool CanLoad(ImageFile file) => true;

        BaseImageLoadOutcome IBaseImageLoader.LoadPreviewBaseWithOutcome(
            ImageFile file,
            BaseDecodeSettings decode,
            CancellationToken cancellationToken) =>
            BaseImageLoadOutcome.FromImage(
                LoadPreviewBase(file, decode, cancellationToken),
                BaseImageLoadFailure.DecodeFailed);

        public BaseImage? LoadPreviewBase(
            ImageFile file,
            BaseDecodeSettings decode,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public BaseImage? LoadFullBase(
            ImageFile file,
            BaseDecodeSettings decode,
            CancellationToken cancellationToken) =>
            file != successfulImage
                ? null
                : new BaseImage(
                    new MagickImage(MagickColors.Orange, 16, 16),
                    new BaseImageInfo(
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
                        16));
    }

    private sealed class RecordingProgress :
        IProgress<(int current, int total, string fileName)>
    {
        public List<(int current, int total, string fileName)> Values { get; } = [];

        public void Report((int current, int total, string fileName) value) =>
            Values.Add(value);
    }
}
