using HappyPhoton.Models;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class ImageFileRoutingTests
{
    [Theory]
    [InlineData("image.cr2")]
    [InlineData("image.CR3")]
    [InlineData("image.nef")]
    [InlineData("image.NRW")]
    [InlineData("image.arw")]
    [InlineData("image.DNG")]
    [InlineData("image.raf")]
    [InlineData("image.ORF")]
    [InlineData("image.rw2")]
    [InlineData("image.PEF")]
    public void MosaicRawExtension_IsRaw(string filePath)
    {
        Assert.True(new ImageFile(filePath).IsRaw);
    }

    [Theory]
    [InlineData("image.heic")]
    [InlineData("image.HEIC")]
    [InlineData("image.heif")]
    [InlineData("image.HEIF")]
    public void HeicExtension_IsStandard(string filePath)
    {
        Assert.False(new ImageFile(filePath).IsRaw);
    }
}
