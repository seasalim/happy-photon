using Avalonia.Threading;
using ImageMagick;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using Xunit;

namespace HappyPhoton.Tests;

[Collection(AvaloniaTestCollection.Name)]
public sealed class ImageFileThumbnailLifetimeTests
{
    private readonly AvaloniaTestFixture _fixture;

    public ImageFileThumbnailLifetimeTests(AvaloniaTestFixture fixture)
    {
        _fixture = fixture;
    }

    [WindowsFact]
    public void Replacement_DefersRetiredThumbnailDisposalUntilAfterRender()
    {
        _fixture.RequireWindows();
        Dispatcher.UIThread.RunJobs();
        using var source = new MagickImage(MagickColors.Red, 4, 3);
        var firstBitmap = BitmapConversionService.ConvertToBitmap(source)!;
        var secondBitmap = BitmapConversionService.ConvertToBitmap(source)!;
        var thirdBitmap = BitmapConversionService.ConvertToBitmap(source)!;
        var image = new ImageFile("image.jpg");
        using var retirement = new UiBitmapRetirement();
        var library = CreateDeferredLibrary(retirement);

        library.ReplaceThumbnail(image, firstBitmap);
        library.ReplaceThumbnail(image, secondBitmap);

        Assert.Same(secondBitmap, image.Thumbnail);
        Assert.Equal(4, firstBitmap.PixelSize.Width);
        Dispatcher.UIThread.RunJobs();
        Assert.Throws<ObjectDisposedException>(() => _ = firstBitmap.PixelSize);

        library.ReplaceThumbnail(image, thirdBitmap);
        library.ReplaceThumbnail(image, secondBitmap);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(4, secondBitmap.PixelSize.Width);
        Assert.Throws<ObjectDisposedException>(() => _ = thirdBitmap.PixelSize);

        library.ReplaceThumbnail(image, null);
        Dispatcher.UIThread.RunJobs();
        Assert.Throws<ObjectDisposedException>(() => _ = secondBitmap.PixelSize);
    }

    [WindowsFact]
    public void SetImagesAndRemove_ClearOwnedThumbnailReferences()
    {
        _fixture.RequireWindows();
        Dispatcher.UIThread.RunJobs();
        using var source = new MagickImage(MagickColors.Red, 4, 3);
        var firstBitmap = BitmapConversionService.ConvertToBitmap(source)!;
        var secondBitmap = BitmapConversionService.ConvertToBitmap(source)!;
        var first = new ImageFile("first.jpg");
        var second = new ImageFile("second.jpg");
        using var retirement = new UiBitmapRetirement();
        var library = CreateDeferredLibrary(retirement);
        library.ReplaceThumbnail(first, firstBitmap);
        library.ReplaceThumbnail(second, secondBitmap);
        library.SetImages(new[] { first });

        library.SetImages(new[] { second });
        Assert.Null(first.Thumbnail);
        Assert.Equal(4, firstBitmap.PixelSize.Width);
        Dispatcher.UIThread.RunJobs();
        Assert.Throws<ObjectDisposedException>(() => _ = firstBitmap.PixelSize);

        library.Remove(second);
        Assert.Null(second.Thumbnail);
        Assert.Equal(4, secondBitmap.PixelSize.Width);
        Dispatcher.UIThread.RunJobs();
        Assert.Throws<ObjectDisposedException>(() => _ = secondBitmap.PixelSize);
    }

    [WindowsFact]
    public void DefaultState_DisposesRetiredThumbnailWithoutDispatcher()
    {
        _fixture.RequireWindows();
        using var source = new MagickImage(MagickColors.Red, 4, 3);
        var bitmap = BitmapConversionService.ConvertToBitmap(source)!;
        var image = new ImageFile("image.jpg");
        var library = new LibraryImageState();

        library.ReplaceThumbnail(image, bitmap);
        library.ReplaceThumbnail(image, null);

        Assert.Throws<ObjectDisposedException>(() => _ = bitmap.PixelSize);
    }

    private static LibraryImageState CreateDeferredLibrary(
        UiBitmapRetirement retirement) =>
        new((image, bitmap) => retirement.Retire(
            bitmap,
            () => ReferenceEquals(image.Thumbnail, bitmap)));
}
