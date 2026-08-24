using Avalonia.Threading;
using Avalonia.Headless.XUnit;
using ImageMagick;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class ImageFileThumbnailLifetimeTests
{
    [AvaloniaFact]
    public void Replacement_DefersRetiredThumbnailDisposalUntilAfterRender()
    {
        Dispatcher.UIThread.RunJobs();
        using var source = new MagickImage(MagickColors.Red, 4, 3);
        var firstBitmap = BitmapConversionService.ConvertToBitmap(source)!;
        var secondBitmap = BitmapConversionService.ConvertToBitmap(source)!;
        var thirdBitmap = BitmapConversionService.ConvertToBitmap(source)!;
        var image = new ImageFile("image.jpg");
        using var retirement = new UiBitmapRetirement();
        var browse = CreateDeferredBrowse(retirement);

        browse.ReplaceThumbnail(image, firstBitmap);
        browse.ReplaceThumbnail(image, secondBitmap);

        Assert.Same(secondBitmap, image.Thumbnail);
        Assert.Equal(4 * 3 * 4, retirement.PendingBytes);
        Assert.True(retirement.PeakPendingBytes >= retirement.PendingBytes);
        Assert.Equal(4, firstBitmap.PixelSize.Width);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(0, retirement.PendingBytes);
        Assert.Throws<ObjectDisposedException>(() => _ = firstBitmap.PixelSize);

        browse.ReplaceThumbnail(image, thirdBitmap);
        browse.ReplaceThumbnail(image, secondBitmap);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(4, secondBitmap.PixelSize.Width);
        Assert.Throws<ObjectDisposedException>(() => _ = thirdBitmap.PixelSize);

        browse.ReplaceThumbnail(image, null);
        Dispatcher.UIThread.RunJobs();
        Assert.Throws<ObjectDisposedException>(() => _ = secondBitmap.PixelSize);
    }

    [AvaloniaFact]
    public void SetImagesAndRemove_ClearOwnedThumbnailReferences()
    {
        Dispatcher.UIThread.RunJobs();
        using var source = new MagickImage(MagickColors.Red, 4, 3);
        var firstBitmap = BitmapConversionService.ConvertToBitmap(source)!;
        var secondBitmap = BitmapConversionService.ConvertToBitmap(source)!;
        var first = new ImageFile("first.jpg");
        var second = new ImageFile("second.jpg");
        using var retirement = new UiBitmapRetirement();
        var browse = CreateDeferredBrowse(retirement);
        browse.ReplaceThumbnail(first, firstBitmap);
        browse.ReplaceThumbnail(second, secondBitmap);
        browse.SetImages(new[] { first });

        browse.SetImages(new[] { second });
        Assert.Null(first.Thumbnail);
        Assert.Equal(4, firstBitmap.PixelSize.Width);
        Dispatcher.UIThread.RunJobs();
        Assert.Throws<ObjectDisposedException>(() => _ = firstBitmap.PixelSize);

        browse.Remove(second);
        Assert.Null(second.Thumbnail);
        Assert.Equal(4, secondBitmap.PixelSize.Width);
        Dispatcher.UIThread.RunJobs();
        Assert.Throws<ObjectDisposedException>(() => _ = secondBitmap.PixelSize);
    }

    [AvaloniaFact]
    public void DefaultState_DisposesRetiredThumbnailWithoutDispatcher()
    {
        using var source = new MagickImage(MagickColors.Red, 4, 3);
        var bitmap = BitmapConversionService.ConvertToBitmap(source)!;
        var image = new ImageFile("image.jpg");
        var browse = new BrowseImageState();

        browse.ReplaceThumbnail(image, bitmap);
        browse.ReplaceThumbnail(image, null);

        Assert.Throws<ObjectDisposedException>(() => _ = bitmap.PixelSize);
    }

    private static BrowseImageState CreateDeferredBrowse(
        UiBitmapRetirement retirement) =>
        new((image, bitmap) => retirement.Retire(
            bitmap,
            () => ReferenceEquals(image.Thumbnail, bitmap)));
}
