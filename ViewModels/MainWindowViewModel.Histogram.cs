using HappyPhoton.Models;
using HappyPhoton.Services;

namespace HappyPhoton.ViewModels;

public partial class MainWindowViewModel
{
    private void ScheduleHistogramUpdate()
    {
        var selectedImage = SelectedImage;
        if (selectedImage == null) return;
        if ((IsDevelopMode || IsFullScreenMode) &&
            selectedImage.SourceRequiresHydration)
        {
            Histogram = null;
            return;
        }
        var debounce = ReplaceDebounce(ref _histogramDebounce);
        var ct = debounce.Token;
        _ = DebouncedAction.RunAsync(
            "histogram update",
            TimeSpan.FromMilliseconds(300),
            ct,
            () => UpdateScheduledHistogramAsync(selectedImage, ct));
    }

    private async Task UpdateScheduledHistogramAsync(
        ImageFile imageFile,
        CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested ||
            !ReferenceEquals(SelectedImage, imageFile))
        {
            return;
        }

        if (IsDevelopMode || IsFullScreenMode)
        {
            if (imageFile.SourceRequiresHydration)
            {
                return;
            }

            await UpdatePreviewWithCurrentSliders(
                skipHistogram: false,
                cancellationToken);
            return;
        }

        if (imageFile.Thumbnail != null)
        {
            var generation = imageFile.ThumbnailGeneration;
            // Own the pixels before leaving the UI thread so retirement never waits.
            using var histogramSource = BitmapConversionService.CloneBitmap(
                imageFile.Thumbnail);
            var histogram = await Task.Run(
                () => ImageService.CalculateLibraryHistogram(histogramSource),
                cancellationToken);
            if (!cancellationToken.IsCancellationRequested &&
                ReferenceEquals(SelectedImage, imageFile) &&
                imageFile.ThumbnailGeneration == generation)
            {
                Histogram = histogram;
            }
        }
    }
}
