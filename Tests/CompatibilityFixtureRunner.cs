using System.Diagnostics;
using HappyPhoton.LibRaw.Interop;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using ImageMagick;

namespace HappyPhoton.Tests;

internal sealed class CompatibilityObservation
{
    public required string Slug { get; init; }
    public Dictionary<string, string> Capabilities { get; } =
        new(StringComparer.Ordinal);
    public ObservedMetadata? Metadata { get; set; }
    public ObservedSensor? Sensor { get; set; }
    public double[]? CamMul { get; set; }
    public double[]? CamToSrgb { get; set; }
    public int MatrixRows { get; set; }
    public int MatrixColumns { get; set; }
    public ObservedUnpackError? UnpackError { get; set; }
    public int PreviewWidth { get; set; }
    public int PreviewHeight { get; set; }
    public int FullWidth { get; set; }
    public int FullHeight { get; set; }
    public int AppliedOrientation { get; set; }
    public int BrowseWidth { get; set; }
    public int BrowseHeight { get; set; }
    public double? HalfFullMeanDeltaE { get; set; }
    public double? WysiwygMeanDeltaE { get; set; }
    public double? WysiwygP99DeltaE { get; set; }
    public bool RawDecodeFailed { get; set; }
    public string? UserStatus { get; set; }
    public long PreviewDecodeMilliseconds { get; set; }
    public long FullDecodeMilliseconds { get; set; }
    public long ExportMilliseconds { get; set; }
    public long ElapsedMilliseconds { get; set; }
    public long ProcessPeakWorkingSetBytes { get; set; }
}

internal sealed record ObservedMetadata(
    string? Make,
    string? Model,
    int VisibleWidth,
    int VisibleHeight,
    int NativeOrientation,
    bool HasIso,
    bool HasExposure,
    bool HasCaptureTimestamp);

internal sealed record ObservedSensor(
    int Colors,
    uint Filters,
    uint DngVersion,
    string ColorDescription);

internal sealed record ObservedUnpackError(int NativeCode, string NativeText);

internal static partial class CompatibilityFixtureRunner
{
    internal static async Task<CompatibilityObservation> RunAsync(
        CompatibilityFixture fixture,
        string fixturePath,
        string resultsDirectory,
        bool saveReviewImage,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var observation = new CompatibilityObservation { Slug = fixture.Slug! };
        var rawService = new LibRawProcessingService();
        var loader = new RawBaseLoader();
        var imageFile = new ImageFile(fixturePath);
        BaseImage? previewBase = null;
        BaseImage? fullBase = null;
        BaseImageLoadOutcome? previewOutcome = null;
        var temporaryDirectory = Path.Combine(
            Path.GetTempPath(),
            $"happy-photon-compat-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temporaryDirectory);
        var copiedSource = Path.Combine(temporaryDirectory, fixture.FileName);
        File.Copy(fixturePath, copiedSource);

        try
        {
            ObserveNativeFacts(observation, fixturePath, cancellationToken);
            ObserveMetadata(observation, imageFile, rawService);
            ObserveBrowseThumbnail(
                observation, fixturePath, rawService, cancellationToken);

            var previewStopwatch = Stopwatch.StartNew();
            try
            {
                previewOutcome = loader.LoadPreviewBaseWithOutcome(
                    imageFile,
                    BaseDecodeSettings.Default,
                    cancellationToken);
                previewBase = previewOutcome.DetachInteractiveImage();
                observation.Capabilities["preview"] = previewBase != null
                    ? "pass"
                    : OutcomeForFailure(previewOutcome.Failure);
                if (previewBase != null)
                {
                    AssertCanonicalBase(previewBase, preview: true);
                    observation.PreviewWidth = checked((int)previewBase.Pixels.Width);
                    observation.PreviewHeight = checked((int)previewBase.Pixels.Height);
                }
            }
            catch (Exception exception)
            {
                RecordFailure(observation, "preview", exception);
            }
            observation.PreviewDecodeMilliseconds = previewStopwatch.ElapsedMilliseconds;

            var fullStopwatch = Stopwatch.StartNew();
            try
            {
                fullBase = loader.LoadFullBase(
                    new ImageFile(copiedSource),
                    BaseDecodeSettings.Default,
                    cancellationToken);
                observation.Capabilities["fullDecode"] = fullBase != null
                    ? "pass"
                    : observation.UnpackError != null ? "unsupported" : "failed";
                if (fullBase != null)
                {
                    AssertCanonicalBase(fullBase, preview: false);
                    observation.FullWidth = checked((int)fullBase.Pixels.Width);
                    observation.FullHeight = checked((int)fullBase.Pixels.Height);
                    observation.AppliedOrientation =
                        fullBase.Info.ExifOrientationApplied;
                }
            }
            catch (Exception exception)
            {
                RecordFailure(observation, "fullDecode", exception);
            }
            observation.FullDecodeMilliseconds = fullStopwatch.ElapsedMilliseconds;

            ObserveCameraColor(observation, fullBase);
            ObserveRender(
                observation,
                fixture,
                previewBase,
                fullBase,
                resultsDirectory,
                saveReviewImage);
            await ObserveExportAsync(
                observation,
                copiedSource,
                temporaryDirectory,
                rawService,
                loader,
                cancellationToken);
            await ObserveFailureAttributionAsync(
                observation,
                imageFile,
                previewOutcome,
                loader,
                temporaryDirectory);
        }
        finally
        {
            fullBase?.Dispose();
            previewBase?.Dispose();
        }

        try
        {
            var renamed = Path.Combine(
                temporaryDirectory,
                $"renamed-{fixture.FileName}");
            File.Move(copiedSource, renamed);
            File.Delete(renamed);
            observation.Capabilities["cleanup"] = "pass";
        }
        catch (Exception exception)
        {
            RecordFailure(observation, "cleanup", exception);
        }
        finally
        {
            if (Directory.Exists(temporaryDirectory))
            {
                Directory.Delete(temporaryDirectory, recursive: true);
            }
        }

        observation.ElapsedMilliseconds = stopwatch.ElapsedMilliseconds;
        observation.ProcessPeakWorkingSetBytes =
            Process.GetCurrentProcess().PeakWorkingSet64;
        return observation;
    }

    private static void ObserveNativeFacts(
        CompatibilityObservation observation,
        string path,
        CancellationToken cancellationToken)
    {
        try
        {
            using var context = LibRawContext.Open(path, cancellationToken);
            var dimensions = context.GetDimensions(cancellationToken);
            var metadata = context.GetMetadata(cancellationToken);
            var sensor = context.GetSensorIdentity(cancellationToken);
            observation.Metadata = new(
                metadata.Make?.Trim(),
                metadata.Model?.Trim(),
                checked((int)dimensions.VisibleWidth),
                checked((int)dimensions.VisibleHeight),
                dimensions.Orientation,
                metadata.Iso is > 0,
                metadata.Shutter is > 0 && metadata.Aperture is > 0,
                metadata.Timestamp is > 0);
            observation.Sensor = new(
                sensor.Colors,
                sensor.Filters,
                sensor.DngVersion,
                sensor.ColorDescription);
            try
            {
                context.Unpack(cancellationToken);
                var facts = context.GetCameraFacts(cancellationToken);
                if (facts != null)
                {
                    observation.CamMul = facts.Multipliers
                        .Select(value => (double)value).ToArray();
                    observation.MatrixRows = facts.CameraToSrgb.GetLength(0);
                    observation.MatrixColumns = facts.CameraToSrgb.GetLength(1);
                    observation.CamToSrgb = Flatten(facts.CameraToSrgb);
                }
                observation.Capabilities["unpack"] = "pass";
            }
            catch (LibRawDecodeException exception)
            {
                observation.UnpackError = new(
                    exception.NativeCode,
                    exception.NativeText);
                observation.Capabilities["unpack"] = "unsupported";
            }
        }
        catch (Exception exception)
        {
            RecordFailure(observation, "unpack", exception);
        }
    }

    private static void ObserveMetadata(
        CompatibilityObservation observation,
        ImageFile imageFile,
        IRawProcessingService rawService)
    {
        try
        {
            var metadata = MetadataService.ExtractMetadata(imageFile, rawService);
            imageFile.ApplyMetadata(metadata);
            Require(
                imageFile.MetadataLoaded && imageFile.PixelWidth > 0 &&
                imageFile.PixelHeight > 0 &&
                !string.IsNullOrWhiteSpace(imageFile.CameraMake) &&
                !string.IsNullOrWhiteSpace(imageFile.CameraModel),
                "Application metadata was incomplete.");
            observation.Capabilities["metadata"] = "pass";
        }
        catch (Exception exception)
        {
            RecordFailure(observation, "metadata", exception);
        }
    }

    private static void ObserveBrowseThumbnail(
        CompatibilityObservation observation,
        string path,
        IRawProcessingService rawService,
        CancellationToken cancellationToken)
    {
        try
        {
            using var bitmap = new EmbeddedPreviewExtractor(rawService).TryExtract(
                path, 512, cancellationToken);
            Require(bitmap != null, "No decodable browse thumbnail was returned.");
            observation.BrowseWidth = bitmap!.PixelSize.Width;
            observation.BrowseHeight = bitmap.PixelSize.Height;
            Require(
                observation.BrowseWidth > 0 && observation.BrowseHeight > 0,
                "Browse thumbnail dimensions were invalid.");
            observation.Capabilities["browseThumbnail"] = "pass";
        }
        catch (Exception exception)
        {
            RecordFailure(observation, "browseThumbnail", exception);
        }
    }

    private static void ObserveRender(
        CompatibilityObservation observation,
        CompatibilityFixture fixture,
        BaseImage? previewBase,
        BaseImage? fullBase,
        string resultsDirectory,
        bool saveReviewImage)
    {
        if (fullBase == null)
        {
            observation.Capabilities["render"] =
                observation.UnpackError != null ? "unsupported" : "not-run";
            return;
        }

        try
        {
            if (previewBase != null)
            {
                using var previewPixels = new MagickImage(previewBase.Pixels);
                using var fullPixels = new MagickImage(fullBase.Pixels);
                BitmapConversionService.ResizeToMaxDimension(
                    fullPixels,
                    checked((int)Math.Max(previewPixels.Width, previewPixels.Height)));
                observation.HalfFullMeanDeltaE =
                    GoldenImageComparer.Compare(
                        fullPixels,
                        previewPixels,
                        GoldenComparisonDomain.LinearRec2020).MeanDeltaE;
                Require(
                    observation.HalfFullMeanDeltaE <= 2.8,
                    $"Half/full decode mean ΔE was {observation.HalfFullMeanDeltaE:F3}.");
            }

            if (fixture.Expected?.MonochromeNeutrality == true)
            {
                ObserveMonochromeRender(
                    fullBase,
                    fixture,
                    resultsDirectory,
                    saveReviewImage);
                observation.Capabilities["render"] = "pass";
                return;
            }

            var pipeline = new RenderPipeline();
            var identity = new EditSettings();
            using var preview = pipeline.Render(Request(
                fullBase, identity, RenderIntent.Preview));
            using var export = pipeline.Render(Request(
                fullBase, identity, RenderIntent.Export));
            var comparison = GoldenImageComparer.Compare(
                export.Image,
                preview.Image,
                GoldenComparisonDomain.DisplaySrgb);
            observation.WysiwygMeanDeltaE = comparison.MeanDeltaE;
            observation.WysiwygP99DeltaE = comparison.P99DeltaE;
            Require(
                comparison.MeanDeltaE <= 1.5 && comparison.P99DeltaE <= 4.0,
                $"Preview/export ΔE was mean {comparison.MeanDeltaE:F3}, " +
                $"p99 {comparison.P99DeltaE:F3}.");

            var editedSettings = new EditSettings
            {
                Exposure = 1,
                Wb = new WhiteBalanceSettings
                {
                    Mode = WbMode.Custom,
                    Kelvin = 7200,
                    Tint = -10
                }
            };
            using var edited = pipeline.Render(Request(
                fullBase, editedSettings, RenderIntent.Export));
            Require(
                edited.Image.Width > 0 && edited.Image.Height > 0,
                "Edited render dimensions were invalid.");

            if (saveReviewImage)
            {
                Directory.CreateDirectory(resultsDirectory);
                using var review = new MagickImage(export.Image);
                review.Format = MagickFormat.Jpeg;
                review.Quality = 90;
                review.Write(Path.Combine(
                    resultsDirectory, $"{fixture.Slug}-default.jpg"));
            }
            observation.Capabilities["render"] = "pass";
        }
        catch (Exception exception)
        {
            RecordFailure(observation, "render", exception);
        }
    }

    private static void AssertCanonicalBase(BaseImage image, bool preview)
    {
        Require(image.Info.Kind == BaseSourceKind.RawLibRaw, "Base source was not LibRaw.");
        Require(image.Info.IsRawSource, "Base was not marked as a RAW source.");
        Require(image.Pixels.Depth == 16, "Base storage was not 16-bit.");
        Require(image.Pixels.ColorSpace == ColorSpace.RGB, "Base was not linear RGB.");
        Require(image.Pixels.Width > 0 && image.Pixels.Height > 0, "Base was empty.");
        if (image.Info.IsMonochrome)
        {
            AssertMonochromeBase(image);
        }
        if (preview)
        {
            Require(
                Math.Max(image.Pixels.Width, image.Pixels.Height) <=
                BaseImage.InteractivePreviewMaxDimension,
                "Preview base exceeded its maximum dimension.");
        }
        using var sample = new MagickImage(image.Pixels);
        sample.Resize(new MagickGeometry(64, 64) { IgnoreAspectRatio = true });
        Require(
            sample.Statistics().Composite().StandardDeviation > 0,
            "Base pixels were uniform.");
    }

    private static RenderRequest Request(
        BaseImage image,
        EditSettings settings,
        RenderIntent intent) =>
        new(image, settings, intent, 500, new RenderOptions(false, false));

    private static string OutcomeForFailure(BaseImageLoadFailure failure) =>
        failure == BaseImageLoadFailure.UnsupportedRaw ? "unsupported" : "failed";

    private static double[] Flatten(float[,] values)
    {
        var result = new double[values.Length];
        for (var row = 0; row < values.GetLength(0); row++)
        for (var column = 0; column < values.GetLength(1); column++)
        {
            result[row * values.GetLength(1) + column] = values[row, column];
        }
        return result;
    }

    private static double[] Flatten(double[,] values)
    {
        var result = new double[values.Length];
        for (var row = 0; row < values.GetLength(0); row++)
        for (var column = 0; column < values.GetLength(1); column++)
        {
            result[row * values.GetLength(1) + column] = values[row, column];
        }
        return result;
    }

    private static void RecordFailure(
        CompatibilityObservation observation,
        string capability,
        Exception exception) =>
        observation.Capabilities[capability] =
            $"failed: {exception.GetType().Name}: {exception.Message}";

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
