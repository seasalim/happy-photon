using Avalonia.Threading;
using ImageMagick;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using Xunit;

namespace HappyPhoton.Tests;

[Collection(AvaloniaTestCollection.Name)]
public sealed class PreviewBitmapLifetimeTests
{
    private readonly AvaloniaTestFixture _fixture;

    public PreviewBitmapLifetimeTests(AvaloniaTestFixture fixture) =>
        _fixture = fixture;

    [WindowsFact]
    public async Task Replacement_DefersRetiredBitmapDisposalUntilAfterRender()
    {
        _fixture.RequireWindows();
        Dispatcher.UIThread.RunJobs();
        using var catalog = new CatalogService(Path.Combine(
            Path.GetTempPath(),
            $"happy-photon-preview-lifetime-{Guid.NewGuid():N}"));
        await using var viewModel = new MainWindowViewModel(catalog);
        using var source = new MagickImage(MagickColors.Red, 4, 3);
        var first = BitmapConversionService.ConvertToBitmap(source)!;
        var second = BitmapConversionService.ConvertToBitmap(source)!;

        viewModel.ReplacePreviewImage(first);
        viewModel.ReplacePreviewImage(second);

        Assert.Equal(4, first.PixelSize.Width);
        Dispatcher.UIThread.RunJobs();
        Assert.Throws<ObjectDisposedException>(() => _ = first.PixelSize);

        viewModel.ReplacePreviewImage(null);
        Dispatcher.UIThread.RunJobs();
        Assert.Throws<ObjectDisposedException>(() => _ = second.PixelSize);
    }

    [WindowsFact]
    public async Task Replacement_DoesNotDisposeBitmapThatBecomesCurrentAgain()
    {
        _fixture.RequireWindows();
        Dispatcher.UIThread.RunJobs();
        using var catalog = new CatalogService(Path.Combine(
            Path.GetTempPath(),
            $"happy-photon-preview-lifetime-{Guid.NewGuid():N}"));
        await using var viewModel = new MainWindowViewModel(catalog);
        using var source = new MagickImage(MagickColors.Red, 4, 3);
        var first = BitmapConversionService.ConvertToBitmap(source)!;
        var second = BitmapConversionService.ConvertToBitmap(source)!;

        viewModel.ReplacePreviewImage(first);
        viewModel.ReplacePreviewImage(second);
        viewModel.ReplacePreviewImage(first);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(4, first.PixelSize.Width);
        Assert.Throws<ObjectDisposedException>(() => _ = second.PixelSize);

        viewModel.ReplacePreviewImage(null);
        Dispatcher.UIThread.RunJobs();
        Assert.Throws<ObjectDisposedException>(() => _ = first.PixelSize);
    }

    [WindowsFact]
    public async Task DisposeAsync_DisposesOwnedBitmapsWithoutDispatcherDrain()
    {
        _fixture.RequireWindows();
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
        viewModel.Library.SetImages([image]);

        viewModel.ReplacePreviewImage(oldPreview);
        viewModel.ReplacePreviewImage(currentPreview);
        viewModel.Library.ReplaceThumbnail(image, oldThumbnail);
        viewModel.Library.ReplaceThumbnail(image, currentThumbnail);

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
