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

    [Fact]
    public void SetImagesAndRemove_ClearOwnedThumbnailReferences()
    {
        _fixture.RequireWindows();
        using var source = new MagickImage(MagickColors.Red, 4, 3);
        var firstBitmap = BitmapConversionService.ConvertToBitmap(source);
        var secondBitmap = BitmapConversionService.ConvertToBitmap(source);
        var first = new ImageFile("first.jpg");
        var second = new ImageFile("second.jpg");
        first.ReplaceThumbnail(firstBitmap);
        second.ReplaceThumbnail(secondBitmap);
        var library = new LibraryImageState();
        library.SetImages(new[] { first });

        library.SetImages(new[] { second });
        Assert.Null(first.Thumbnail);

        library.Remove(second);
        Assert.Null(second.Thumbnail);
    }
}
