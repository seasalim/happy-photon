using Avalonia.Threading;
using Avalonia.Headless.XUnit;
using ImageMagick;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class PreviewBitmapLifetimeTests
{
    [AvaloniaFact]
    public async Task Replacement_DefersRetiredBitmapDisposalUntilAfterRender()
    {
        Dispatcher.UIThread.RunJobs();
        using var catalog = new CatalogService(Path.Combine(
            Path.GetTempPath(),
            $"happy-photon-preview-lifetime-{Guid.NewGuid():N}"));
        await using var viewModel = new MainWindowViewModel(catalog);
        using var source = new MagickImage(MagickColors.Red, 4, 3);
        var first = BitmapConversionService.ConvertToBitmap(source)!;
        var second = BitmapConversionService.ConvertToBitmap(source)!;

        viewModel.ReplacePreviewImage(first, PreviewPaintSource.FreshRender);
        viewModel.ReplacePreviewImage(second, PreviewPaintSource.FreshRender);

        Assert.Equal(4, first.PixelSize.Width);
        Dispatcher.UIThread.RunJobs();
        Assert.Throws<ObjectDisposedException>(() => _ = first.PixelSize);

        viewModel.ClearPreviewImage();
        Dispatcher.UIThread.RunJobs();
        Assert.Throws<ObjectDisposedException>(() => _ = second.PixelSize);
    }

    [AvaloniaFact]
    public async Task Replacement_DoesNotDisposeBitmapThatBecomesCurrentAgain()
    {
        Dispatcher.UIThread.RunJobs();
        using var catalog = new CatalogService(Path.Combine(
            Path.GetTempPath(),
            $"happy-photon-preview-lifetime-{Guid.NewGuid():N}"));
        await using var viewModel = new MainWindowViewModel(catalog);
        using var source = new MagickImage(MagickColors.Red, 4, 3);
        var first = BitmapConversionService.ConvertToBitmap(source)!;
        var second = BitmapConversionService.ConvertToBitmap(source)!;

        viewModel.ReplacePreviewImage(first, PreviewPaintSource.FreshRender);
        viewModel.ReplacePreviewImage(second, PreviewPaintSource.FreshRender);
        viewModel.ReplacePreviewImage(first, PreviewPaintSource.FreshRender);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(4, first.PixelSize.Width);
        Assert.Throws<ObjectDisposedException>(() => _ = second.PixelSize);

        viewModel.ClearPreviewImage();
        Dispatcher.UIThread.RunJobs();
        Assert.Throws<ObjectDisposedException>(() => _ = first.PixelSize);
    }

    [AvaloniaFact]
    public async Task DisposeAsync_DisposesOwnedBitmapsWithoutDispatcherDrain()
    {
        Dispatcher.UIThread.RunJobs();
        using var catalog = new CatalogService(Path.Combine(
            Path.GetTempPath(),
            $"happy-photon-preview-lifetime-{Guid.NewGuid():N}"));
        var viewModel = new MainWindowViewModel(catalog);
        using var source = new MagickImage(MagickColors.Red, 4, 3);
        var oldPreview = BitmapConversionService.ConvertToBitmap(source)!;
        var currentPreview = BitmapConversionService.ConvertToBitmap(source)!;
        var oldThumbnail = BitmapConversionService.ConvertToBitmap(source)!;
        var currentThumbnail = BitmapConversionService.ConvertToBitmap(source)!;
        var image = new ImageFile("image.jpg");
        viewModel.Browse.SetImages([image]);

        viewModel.ReplacePreviewImage(oldPreview, PreviewPaintSource.FreshRender);
        viewModel.ReplacePreviewImage(currentPreview, PreviewPaintSource.FreshRender);
        viewModel.Browse.ReplaceThumbnail(image, oldThumbnail);
        viewModel.Browse.ReplaceThumbnail(image, currentThumbnail);

        await viewModel.DisposeAsync();

        Assert.Null(viewModel.PreviewImage);
        Assert.Null(image.Thumbnail);
        Assert.Throws<ObjectDisposedException>(() => _ = oldPreview.PixelSize);
        Assert.Throws<ObjectDisposedException>(() => _ = currentPreview.PixelSize);
        Assert.Throws<ObjectDisposedException>(() => _ = oldThumbnail.PixelSize);
        Assert.Throws<ObjectDisposedException>(() => _ = currentThumbnail.PixelSize);
        Dispatcher.UIThread.RunJobs();
    }
}
