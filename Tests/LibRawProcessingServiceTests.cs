using HappyPhoton.Services;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class LibRawProcessingServiceTests
{
    [Fact]
    public void ExtractThumbnail_ReturnsDecodableBundledPreview()
    {
        var service = new LibRawProcessingService();
        var path = Path.Combine(
            GoldenTestPaths.AssetDirectory,
            "canon-eos-350d.cr2");

        var data = service.ExtractThumbnail(path);

        Assert.True(service.IsAvailable);
        Assert.NotNull(data);
        Assert.NotEmpty(data!);
        using var image = new MagickImage(data);
        Assert.True(image.Width > 0);
        Assert.True(image.Height > 0);
    }
}
