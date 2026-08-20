using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using ImageMagick;
using System.Diagnostics;

namespace HappyPhoton.Tests;

internal static partial class CompatibilityFixtureRunner
{
    private static async Task ObserveExportAsync(
        CompatibilityObservation observation,
        string copiedSource,
        string temporaryDirectory,
        IRawProcessingService rawService,
        IBaseImageLoader loader,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var source = new ImageFile(copiedSource);
            source.ApplyMetadata(MetadataService.ExtractMetadata(source, rawService));
            var settings = new ExportSettings
            {
                OutputFolder = temporaryDirectory,
                Format = ExportFormat.Jpeg,
                Quality = 90,
                ExportHiRes = false,
                ExportSmall = true,
                SmallMaxSize = 500,
                OutputSharpening = false
            };
            var count = await new ImageExportService(
                new RenderPipeline(), loader, new ExportMetadataService())
                .ExportBatchAsync(
                    [source],
                    settings,
                    [new ExportVariant("review", 500)],
                    useSubfolders: false,
                    cancellationToken: cancellationToken);
            if (count == 0)
            {
                observation.Capabilities["export"] =
                    observation.UnpackError != null ? "unsupported" : "failed";
                return;
            }

            Require(count == 1, $"Expected one export, observed {count}.");
            var outputPath = settings.GetOutputPath(
                source.FileName,
                new ExportVariant("review", 500),
                useSubfolders: false);
            using var exported = new MagickImage(outputPath);
            Require(
                Math.Max(exported.Width, exported.Height) == 500,
                "Export long edge was not 500 pixels.");
            Require(
                exported.GetColorProfile() is { } profile &&
                profile.ToByteArray().Length > 0,
                "Export did not carry an ICC profile.");
            var exif = exported.GetExifProfile() ??
                throw new InvalidOperationException(
                    "Export did not carry an EXIF profile.");
            Require(
                exif.GetValue(ExifTag.Orientation)?.Value == 1,
                "Export orientation was not normalized to 1.");
            Require(
                !string.IsNullOrWhiteSpace(exif.GetValue(ExifTag.Make)?.Value) &&
                !string.IsNullOrWhiteSpace(exif.GetValue(ExifTag.Model)?.Value) &&
                exif.GetValue(ExifTag.DateTimeOriginal) != null,
                "Export capture metadata was incomplete.");
            observation.Capabilities["export"] = "pass";
        }
        catch (Exception exception)
        {
            RecordFailure(observation, "export", exception);
        }
        finally
        {
            observation.ExportMilliseconds = stopwatch.ElapsedMilliseconds;
        }
    }

    private static async Task ObserveFailureAttributionAsync(
        CompatibilityObservation observation,
        ImageFile imageFile,
        BaseImageLoadOutcome? previewOutcome,
        IBaseImageLoader loader,
        string temporaryDirectory)
    {
        if (previewOutcome is not { Pair: null })
        {
            observation.Capabilities["attribution"] = "not-applicable";
            return;
        }

        try
        {
            using var catalog = new CatalogService(Path.Combine(
                temporaryDirectory, "attribution"));
            var viewModel = new MainWindowViewModel(
                catalog,
                loader,
                loadMetadataAsync: _ => Task.CompletedTask,
                availabilityService: new TestSourceAvailabilityService(
                    SourceAvailability.AvailableLocally),
                rawRuntimeHealth: LibRawNativeSupport.Health);
            viewModel.SelectedImage = imageFile;
            viewModel.ApplyPreviewLoadOutcome(new PreviewLoadOutcome(
                imageFile, 1, previewOutcome.Failure));
            observation.RawDecodeFailed = imageFile.RawDecodeFailed;
            observation.UserStatus = viewModel.StatusMessage;
            await viewModel.DisposeAsync();
            Require(
                observation.RawDecodeFailed &&
                observation.UserStatus?.Contains("Nikon HE", StringComparison.Ordinal) == true,
                "Unsupported RAW failure did not reach the existing user attribution.");
            observation.Capabilities["attribution"] = "pass";
        }
        catch (Exception exception)
        {
            RecordFailure(observation, "attribution", exception);
        }
    }
}
