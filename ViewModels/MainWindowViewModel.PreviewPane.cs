using Avalonia.Media.Imaging;
using HappyPhoton.Services;

namespace HappyPhoton.ViewModels;

public partial class MainWindowViewModel
{
    private async Task LoadPreviewPaneAsync(
        ComparePaneViewModel pane,
        Func<bool> isActive,
        CancellationToken cancellationToken)
    {
        try
        {
            using var cached = await ImageService.Previews.LoadCachedPreviewAsync(
                pane.Image,
                pane.Image.EditSettings, cancellationToken);
            if (cached != null)
            {
                var cachedSize = cached.SettingsMatch
                    ? cached.OriginalViewPixelSize
                    : null;
                ApplyPreviewPaneBitmap(
                    pane,
                    cached.DetachBitmap(),
                    isActive,
                    source: PreviewPaintSource.CachedJpeg);
                if (cachedSize is { Width: > 0, Height: > 0 })
                {
                    pane.OriginalViewPixelSize = cachedSize.Value;
                    return;
                }
            }

            using var fresh = await ImageService.Previews.LoadComparePreviewAsync(
                pane.Image,
                pane.Image.EditSettings, cancellationToken: cancellationToken);
            if (fresh != null)
            {
                ImageServiceHelpers.LogDisplayTrace(
                    $"base installed key=pane image={pane.Image.FileName}");
                ApplyPreviewPaneBitmap(
                    pane,
                    fresh.DetachBitmap(),
                    isActive,
                    fresh.OriginalViewPixelSize,
                    source: PreviewPaintSource.FreshRender);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine(
                $"Preview pane failed for {pane.Image.FilePath}: {exception.Message}");
        }
        finally
        {
            pane.IsLoading = false;
        }
    }

    private async Task LoadPreviewPaneAfterAsync(
        Task? previous,
        ComparePaneViewModel pane,
        Func<bool> isActive,
        CancellationToken cancellationToken)
    {
        if (previous != null)
        {
            await previous.ConfigureAwait(
                ConfigureAwaitOptions.ContinueOnCapturedContext |
                ConfigureAwaitOptions.SuppressThrowing);
        }
        cancellationToken.ThrowIfCancellationRequested();
        await LoadPreviewPaneAsync(pane, isActive, cancellationToken);
    }

    private async Task LoadPreviewPaneRefinementAfterAsync(
        Task? previous,
        ComparePaneViewModel pane,
        Func<bool> isActive,
        CancellationToken cancellationToken)
    {
        if (previous != null)
        {
            await previous.ConfigureAwait(
                ConfigureAwaitOptions.ContinueOnCapturedContext |
                ConfigureAwaitOptions.SuppressThrowing);
        }

        try
        {
            while (isActive() && PreviewPaneNeedsRefinement(pane))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var requested = pane.RequiredDeviceLongEdge;
                using var refined = await ImageService.Previews.LoadComparePreviewAsync(
                    pane.Image,
                    pane.Image.EditSettings,
                    requested, cancellationToken);
                if (refined == null || !PreviewPaneNeedsRefinement(pane)) return;
                ImageServiceHelpers.LogDisplayTrace(
                    $"base installed key=pane image={pane.Image.FileName}");
                ApplyPreviewPaneBitmap(
                    pane,
                    refined.DetachBitmap(),
                    isActive,
                    refined.OriginalViewPixelSize,
                    isRefinement: true,
                    source: PreviewPaintSource.FreshRender);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine(
                $"Preview refinement failed for {pane.Image.FilePath}: " +
                exception.Message);
        }
        finally
        {
            pane.IsRefinementQueued = false;
        }
    }

    private static bool PreviewPaneNeedsRefinement(ComparePaneViewModel pane) =>
        pane.IsLoupeRefinementRequested &&
        pane.RequiredDeviceLongEdge > pane.RenderedLongEdge &&
        (pane.AchievableLongEdge == 0 ||
         pane.RenderedLongEdge < pane.AchievableLongEdge);

    private void ApplyPreviewPaneBitmap(
        ComparePaneViewModel pane,
        Bitmap bitmap,
        Func<bool> isActive,
        Avalonia.PixelSize originalViewPixelSize = default,
        bool isRefinement = false,
        PreviewPaintSource? source = null)
    {
        if (!isActive())
        {
            bitmap.Dispose();
            return;
        }

        if (source != null)
        {
            ImageServiceHelpers.LogDisplayTrace(
                $"paint source={PaintSourceLabel(source.Value)} bitmap={bitmap.PixelSize.Width}x{bitmap.PixelSize.Height} luma={BitmapConversionService.EstimateMeanLuma(bitmap):F4} decode=pane settings=pane");
        }
        var previous = pane.Preview;
        pane.Preview = bitmap;
        pane.RenderedLongEdge = Math.Max(bitmap.PixelSize.Width, bitmap.PixelSize.Height);
        if (!isRefinement)
        {
            var previousPreviewResolution = pane.PreviewResolutionBitmap;
            pane.PreviewResolutionBitmap = bitmap;
            pane.PreviewResolutionLongEdge = pane.RenderedLongEdge;
            if (previousPreviewResolution != null &&
                !ReferenceEquals(previousPreviewResolution, previous))
            {
                RetirePreviewPaneBitmap(pane, previousPreviewResolution);
            }
        }
        if (originalViewPixelSize.Width > 0 && originalViewPixelSize.Height > 0)
        {
            pane.OriginalViewPixelSize = originalViewPixelSize;
            pane.AchievableLongEdge = Math.Max(
                originalViewPixelSize.Width,
                originalViewPixelSize.Height);
        }
        if (previous != null && !ReferenceEquals(previous, pane.PreviewResolutionBitmap))
        {
            RetirePreviewPaneBitmap(pane, previous);
        }
    }

    private void RestorePreviewPane(ComparePaneViewModel pane)
    {
        var preview = pane.PreviewResolutionBitmap;
        if (preview == null || ReferenceEquals(pane.Preview, preview)) return;

        var refinement = pane.Preview;
        pane.Preview = preview;
        pane.RenderedLongEdge = pane.PreviewResolutionLongEdge;
        if (refinement != null) RetirePreviewPaneBitmap(pane, refinement);
    }

    private void DisposePreviewPane(ComparePaneViewModel pane)
    {
        var bitmap = pane.Preview;
        var previewResolution = pane.PreviewResolutionBitmap;
        pane.Preview = null;
        pane.PreviewResolutionBitmap = null;
        if (bitmap != null) _bitmapRetirement.Retire(bitmap, () => false);
        if (previewResolution != null && !ReferenceEquals(previewResolution, bitmap))
            _bitmapRetirement.Retire(previewResolution, () => false);
    }

    private void RetirePreviewPaneBitmap(ComparePaneViewModel pane, Bitmap bitmap) =>
        _bitmapRetirement.Retire(
            bitmap,
            () => ReferenceEquals(pane.Preview, bitmap) ||
                ReferenceEquals(pane.PreviewResolutionBitmap, bitmap));
}
