using HappyPhoton.Models;
using Avalonia.Media.Imaging;

namespace HappyPhoton.Services;

public sealed partial class PreviewService
{
    internal Action<string>? FullResolutionTrace { get; set; }

    internal Action<string>? FullResolutionStageStarted { get; set; }

    internal Action<int>? FullResolutionWorkersSelected { get; set; }

    internal Action<int, int, bool>? FullResolutionCrossingWorkerProgress { get; set; }

    internal Action<RenderRequest, ImageMagick.MagickImage>? FullResolutionRendered { get; set; }

    internal bool CanRefine(ImageFile image) => SourceAccessPolicy.CanRead(
        _sourceAvailability.GetAvailability(image.FilePath), SourceReadIntent.Background);

    internal int FullResolutionThreshold(ImageFile image, EditSettings settings, Bitmap bitmap)
    {
        if (!_localRangeBases.TryGetValue(bitmap, out var basis)) return 0;
        using var large = _baseCoordinator.TryAcquireLargeCurrent(image, basis.Info.Decode);
        if (large == null) return 0;

        var size = RenderGeometry.CalculateOriginalViewSize((int)large.Base.Pixels.Width,
            (int)large.Base.Pixels.Height, settings);

        return Math.Min(BaseImage.LargePreviewMaxDimension, Math.Max(size.Width, size.Height));
    }

    internal DevelopFullBase CreateFullBase(ImageFile image, EditSettings settings) => new(async token =>
    {
        if (!CanRefine(image)) return null;

        var decode = await ResolveDecodeAsync(image, settings, token).ConfigureAwait(false);
        FullResolutionTrace?.Invoke("decode-start");

        try
        {
            return await Task.Run(() => new GatedBaseImageLoader(_baseLoader, _sourceAvailability)
                .LoadFullBase(image, decode, token), token).ConfigureAwait(false);
        }
        finally { FullResolutionTrace?.Invoke("decode-end"); }
    });

    internal Task<Bitmap?> RenderFullResolutionAsync(DevelopFullBase holder, EditSettings settings,
        PreviewRenderIdentity parent, Func<int> budget, CancellationToken token) =>
        TrackDisposalTask(() => holder.RenderAsync(settings,
            RenderExecutionOptions.Resting(token, Environment.ProcessorCount, FullResolutionStageStarted)
                with { WorkerBudget = budget, WorkersSelected = FullResolutionWorkersSelected,
                    RawCrossingWorkerProgress = FullResolutionCrossingWorkerProgress },
            (bitmap, basis) =>
            {
                // Range classification uses the bounded preview; never register the held full base.
                using var preview = _baseCoordinator.TryAcquireCurrent(parent.ImageFile, basis.Info.Decode);
                if (preview != null) _localRangeBases.Add(bitmap, preview.Base);

                // Provenance and coordinates contain no reference to the held base.
                _previewIdentities.Add(bitmap, parent);
                _repairMaps.Add(bitmap, new RepairDisplayMap((int)basis.Pixels.Width,
                    (int)basis.Pixels.Height, settings));
                ImageServiceHelpers.LogDisplayTrace($"full-base source={parent.ImageFile.FilePath} " +
                    $"base={basis.Pixels.Width}x{basis.Pixels.Height} intent=Export " +
                    $"decode={parent.DecodeKey} settings={parent.SettingsHash}");
            }, FullResolutionRendered, FullResolutionTrace));
}
