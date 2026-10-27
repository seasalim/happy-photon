using Xunit;

namespace HappyPhoton.Tests;

public sealed class CompressedDngQualificationTests
{
    [Fact]
    public void CheckedInDng_CompressionTagParserReadsUncompressedIfd()
    {
        var path = Path.Combine(
            GoldenTestPaths.AssetDirectory,
            "pentax-k-r.dng");

        Assert.Contains((ushort)1, DngCompressionInspection.ReadCompressionTags(path));
    }
}
