using System.Diagnostics;
using HappyPhoton.Models;
using ImageMagick;
using static HappyPhoton.Services.ImageServiceHelpers;

namespace HappyPhoton.Services;

public sealed record ExportWarning(
    ImageFile Image,
    string Code,
    string Message);

public sealed record ExportTargetOutcome(
    ImageFile Capture,
    ExportVariant Recipe,
    string ResolvedPath,
    string? FailureReason)
{
    public bool Succeeded => FailureReason == null;
}

public sealed record ExportBatchResult
{
    // Image-level compatibility projections keep the dialog working until WP3b-ii.
    public int ExportedCount { get; }
    public IReadOnlyList<ImageFile> FailedImages { get; }
    public IReadOnlyList<ExportTargetOutcome> Outcomes { get; }
    public IReadOnlyList<ExportTargetOutcome> FailedTargets { get; }
    public int SuccessfulTargetCount { get; }
    public IReadOnlyList<ExportWarning> Warnings { get; }

    internal ExportBatchResult(
        ExportJob job,
        IReadOnlyList<ExportTargetOutcome> outcomes,
        IReadOnlyList<ExportWarning>? warnings = null)
    {
        Outcomes = Array.AsReadOnly(outcomes.ToArray());
        FailedTargets = Array.AsReadOnly(
            outcomes.Where(outcome => !outcome.Succeeded).ToArray());
        SuccessfulTargetCount = Outcomes.Count(outcome => outcome.Succeeded);
        FailedImages = Array.AsReadOnly(job.Captures
            .Where(capture => FailedTargets.Any(outcome =>
                ReferenceEquals(outcome.Capture, capture)))
            .ToArray());
        ExportedCount = job.Captures.Count(capture =>
        {
            var captureOutcomes = outcomes.Where(outcome =>
                ReferenceEquals(outcome.Capture, capture)).ToList();
            return captureOutcomes.Count > 0 &&
                captureOutcomes.All(outcome => outcome.Succeeded);
        });
        Warnings = Array.AsReadOnly((warnings ?? []).ToArray());
    }
}

public sealed class ImageExportService
{
    private readonly IBaseImageLoader _baseLoader;
    private readonly ExportMetadataService _metadataService;
    private readonly DcpProfileService _dcpProfiles;
    private readonly Func<RenderRequest, MagickImage> _renderDisplayRec2020;

    public ImageExportService(
        RenderPipeline renderPipeline,
        IBaseImageLoader baseLoader,
        ExportMetadataService metadataService) : this(
            renderPipeline,
            baseLoader,
            metadataService,
            new DcpProfileService(new SourceAvailabilityService()))
    {
    }

    internal ImageExportService(
        RenderPipeline renderPipeline,
        IBaseImageLoader baseLoader,
        ExportMetadataService metadataService,
        DcpProfileService dcpProfiles,
        Func<RenderRequest, MagickImage>? renderDisplayRec2020 = null)
    {
        _baseLoader = baseLoader;
        _metadataService = metadataService;
        _dcpProfiles = dcpProfiles ??
            throw new ArgumentNullException(nameof(dcpProfiles));
        _renderDisplayRec2020 = renderDisplayRec2020 ??
            renderPipeline.RenderDisplayRec2020;
    }

    public Task<ExportBatchResult> ExportBatchAsync(
        IEnumerable<ImageFile> images,
        ExportSettings settings,
        IProgress<(int current, int total, string fileName)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var job = settings.CreateJob(images);
        return ExportBatchAsync(
            job,
            progress,
            cancellationToken);
    }

    public Task<ExportBatchResult> ExportBatchAsync(
        ExportJob job,
        IProgress<(int current, int total, string fileName)>? progress = null,
        CancellationToken cancellationToken = default) =>
        ExportBatchCoreAsync(
            job,
            progress,
            SourceReadIntent.Background,
            cancellationToken);

    public async Task<int> ExportBatchAsync(
        IEnumerable<ImageFile> images,
        ExportSettings settings,
        IReadOnlyList<ExportVariant> variants,
        bool useSubfolders,
        IProgress<(int current, int total, string fileName)>? progress = null,
        CancellationToken cancellationToken = default,
        IProgress<ExportWarning>? warningProgress = null)
    {
        var job = settings.CreateJob(images, variants, useSubfolders);
        var result = await ExportBatchAsync(
            job,
            progress,
            cancellationToken);
        foreach (var warning in result.Warnings)
        {
            warningProgress?.Report(warning);
        }
        return result.ExportedCount;
    }

    internal Task<ExportBatchResult> ExportBatchVariantsAsync(
        IEnumerable<ImageFile> images,
        ExportSettings settings,
        IReadOnlyList<ExportVariant> variants,
        bool useSubfolders,
        CancellationToken cancellationToken)
    {
        var job = settings.CreateJob(images, variants, useSubfolders);
        return ExportBatchAsync(job, progress: null, cancellationToken);
    }

    internal async Task<int> ExportBatchApprovedAsync(
        IEnumerable<ImageFile> images,
        ExportSettings settings,
        IReadOnlyList<ExportVariant> variants,
        bool useSubfolders,
        IProgress<(int current, int total, string fileName)>? progress,
        CancellationToken cancellationToken)
    {
        var job = settings.CreateJob(images, variants, useSubfolders);
        var result = await ExportBatchCoreAsync(
            job,
            progress,
            SourceReadIntent.UserApprovedHydration,
            cancellationToken);
        return result.ExportedCount;
    }

    internal Task<ExportBatchResult> ExportBatchApprovedAsync(
        IEnumerable<ImageFile> images,
        ExportSettings settings,
        IProgress<(int current, int total, string fileName)>? progress,
        CancellationToken cancellationToken)
    {
        var job = settings.CreateJob(images);
        return ExportBatchCoreAsync(
            job,
            progress,
            SourceReadIntent.UserApprovedHydration,
            cancellationToken);
    }

    internal Task<ExportBatchResult> ExportBatchApprovedAsync(
        ExportJob job,
        IProgress<(int current, int total, string fileName)>? progress,
        CancellationToken cancellationToken) =>
        ExportBatchCoreAsync(
            job,
            progress,
            SourceReadIntent.UserApprovedHydration,
            cancellationToken);

    private async Task<ExportBatchResult> ExportBatchCoreAsync(
        ExportJob job,
        IProgress<(int current, int total, string fileName)>? progress,
        SourceReadIntent intent,
        CancellationToken cancellationToken)
    {
        var targetsByCapture = job.Targets.ToLookup(target => target.Capture);
        var captures = job.Captures
            .Where(capture => targetsByCapture.Contains(capture))
            .ToList();
        var total = job.Targets.Count;
        var completed = 0;
        var outcomes = new List<ExportTargetOutcome>(job.Targets.Count);
        var warnings = new List<ExportWarning>();
        if (captures.Count > 0)
        {
            progress?.Report((completed, total, captures[0].FileName));
        }

        for (var captureIndex = 0; captureIndex < captures.Count; captureIndex++)
        {
            var capture = captures[captureIndex];
            cancellationToken.ThrowIfCancellationRequested();
            var targets = targetsByCapture[capture].ToList();
            var completedForCapture = 0;

            var imageResult = await Task.Run(
                () => ExportImage(
                    job,
                    capture,
                    targets,
                    intent,
                    cancellationToken,
                    outcome =>
                    {
                        outcomes.Add(outcome);
                        completed++;
                        completedForCapture++;
                        var fileName = completedForCapture == targets.Count &&
                                       captureIndex + 1 < captures.Count
                            ? captures[captureIndex + 1].FileName
                            : capture.FileName;
                        progress?.Report((completed, total, fileName));
                    }),
                cancellationToken);
            if (imageResult.Warning != null)
            {
                warnings.Add(imageResult.Warning);
            }
        }

        return new ExportBatchResult(job, outcomes, warnings);
    }

    private ExportImageResult ExportImage(
        ExportJob job,
        ImageFile imageFile,
        IReadOnlyList<ExportTarget> targets,
        SourceReadIntent intent,
        CancellationToken cancellationToken,
        Action<ExportTargetOutcome> targetCompleted)
    {
        try
        {
            return ExportImageCore(
                job,
                imageFile,
                targets,
                intent,
                cancellationToken,
                targetCompleted);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            foreach (var target in targets)
            {
                targetCompleted(Failed(target, exception));
            }
            return new ExportImageResult(null);
        }
    }

    private ExportImageResult ExportImageCore(
        ExportJob job,
        ImageFile imageFile,
        IReadOnlyList<ExportTarget> targets,
        SourceReadIntent intent,
        CancellationToken cancellationToken,
        Action<ExportTargetOutcome> targetCompleted)
    {
        var stopwatch = Stopwatch.StartNew();
        var editSnapshot = job.GetEditSettings(imageFile);
        var decode = BaseDecodeSettings.From(editSnapshot);
        if (editSnapshot.RawProfile != null)
        {
            var resolution = _dcpProfiles.ResolveAsync(
                    imageFile,
                    editSnapshot.RawProfile,
                    forceRefresh: true,
                    cancellationToken)
                .GetAwaiter()
                .GetResult();
            decode = decode.WithProfileResolution(resolution);
        }
        using var baseImage = _baseLoader is GatedBaseImageLoader gatedLoader
            ? gatedLoader.LoadFullBase(
                imageFile,
                decode,
                intent,
                cancellationToken)
            : _baseLoader.LoadFullBase(
                imageFile,
                decode,
                cancellationToken);
        if (baseImage == null)
        {
            foreach (var target in targets)
            {
                targetCompleted(Failed(
                    target,
                    "The source image could not be loaded."));
            }
            return new ExportImageResult(null);
        }

        var warning = CreateProfileWarning(imageFile, editSnapshot, baseImage.Info);

        cancellationToken.ThrowIfCancellationRequested();
        MagickImage? displayRec2020 = _renderDisplayRec2020(
            new RenderRequest(
            baseImage,
            editSnapshot,
            RenderIntent.Export,
            null,
            new RenderOptions(
                ComputeStats: false,
                ComputeOverlayMasks: false),
            job.Output.OutputColorSpace));
        baseImage.Dispose();
        try
        {
            var fullLongEdge = Math.Max(
                displayRec2020.Width,
                displayRec2020.Height);
            var fullSize = $"{displayRec2020.Width}x{displayRec2020.Height}";
            var encoderSettings = job.Output.CreateEncoderSettings();

            for (var index = 0; index < targets.Count; index++)
            {
                var target = targets[index];
                var variant = target.Recipe;
                cancellationToken.ThrowIfCancellationRequested();
                var shared = displayRec2020 ??
                    throw new InvalidOperationException(
                        "The shared render was already consumed.");
                if (variant.MaxDimension is int maxDimension)
                {
                    RenderColorEncoding.ResizeInLinearLight(
                        shared,
                        maxDimension);
                }

                try
                {
                    var directory = Path.GetDirectoryName(target.ResolvedPath);
                    if (!string.IsNullOrEmpty(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }
                    using var destination = index == targets.Count - 1
                        ? RenderFinalizer.FinalizeOwned(
                            Take(ref displayRec2020),
                            maxDimension: null,
                            job.Output.OutputColorSpace,
                            job.Output.OutputSharpening,
                            variant.MaxDimension is int ownedLongEdge &&
                            ownedLongEdge < fullLongEdge,
                            effects: editSnapshot.Effects)
                        : RenderFinalizer.Finalize(
                            shared,
                            maxDimension: null,
                            job.Output.OutputColorSpace,
                            job.Output.OutputSharpening,
                            variant.MaxDimension is int sizedLongEdge &&
                            sizedLongEdge < fullLongEdge,
                            effects: editSnapshot.Effects);
                    _metadataService.Apply(
                        imageFile,
                        destination,
                        job.Output.StripLocationData,
                        intent);
                    ExportEncoder.Write(
                        destination,
                        encoderSettings,
                        job.Output.OutputColorSpace,
                        target.ResolvedPath,
                        target.OverwriteAuthorized);
                    targetCompleted(Succeeded(target));
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    targetCompleted(Failed(target, exception));
                }
            }

            LogPerformance(
                nameof(ImageExportService),
                nameof(ExportImage),
                stopwatch.ElapsedMilliseconds,
                imageFile.FilePath,
                $"variants={targets.Count};size={fullSize}");
            return new ExportImageResult(warning);
        }
        finally
        {
            displayRec2020?.Dispose();
        }
    }

    private static ExportTargetOutcome Succeeded(ExportTarget target) => new(
        target.Capture,
        target.Recipe,
        target.ResolvedPath,
        FailureReason: null);

    private static ExportTargetOutcome Failed(
        ExportTarget target,
        Exception exception) =>
        Failed(
            target,
            string.IsNullOrWhiteSpace(exception.Message)
                ? exception.GetType().Name
                : exception.Message);

    private static ExportTargetOutcome Failed(
        ExportTarget target,
        string reason) => new(
        target.Capture,
        target.Recipe,
        target.ResolvedPath,
        reason);

    private static MagickImage Take(ref MagickImage? image)
    {
        var result = image ?? throw new InvalidOperationException(
            "The shared render was already consumed.");
        image = null;
        return result;
    }

    private static ExportWarning? CreateProfileWarning(
        ImageFile image,
        EditSettings settings,
        BaseImageInfo info)
    {
        if (settings.RawProfile == null ||
            info.ProfileStatus == DcpProfileErrorCode.None)
        {
            return null;
        }
        return new ExportWarning(
            image,
            $"profile_{info.ProfileStatus.ToString().ToLowerInvariant()}",
            info.ProfileMessage ??
                "The selected camera profile could not be applied; the built-in characterization was exported.");
    }

    private sealed record ExportImageResult(ExportWarning? Warning);
}
