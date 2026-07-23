using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class ExportSafetyTests
{
    private static HashSet<string> Set(params string[] paths) =>
        ExportSafety.BuildOriginalPathSet(paths);

    [Fact]
    public void SelfCollision_Detected()
    {
        var originals = Set(@"C:\shoot\IMG_1.jpg");

        Assert.True(ExportSafety.IsOriginalPath(@"C:\shoot\IMG_1.jpg", originals));
    }

    [Fact]
    public void SiblingCollision_RawExportHitsJpegOriginal()
    {
        // Exporting IMG_1.CR2 as JPEG into the source folder targets IMG_1.jpg —
        // the sibling original.
        var originals = Set(@"C:\shoot\IMG_1.CR2", @"C:\shoot\IMG_1.jpg");

        Assert.True(ExportSafety.IsOriginalPath(@"C:\shoot\IMG_1.jpg", originals));
    }

    [Fact]
    public void CaseInsensitive_OnEveryPlatform()
    {
        var originals = Set(@"C:\shoot\img_1.JPG");

        Assert.True(ExportSafety.IsOriginalPath(@"C:\shoot\IMG_1.jpg", originals));
    }

    [Fact]
    public void ExportsSubfolder_DoesNotCollide()
    {
        var originals = Set(@"C:\shoot\IMG_1.jpg");

        Assert.False(ExportSafety.IsOriginalPath(@"C:\shoot\exports\IMG_1.jpg", originals));
    }

    [Fact]
    public void RelativeSegments_NormalizeOntoOriginal()
    {
        var originals = Set(@"C:\shoot\IMG_1.jpg");

        Assert.True(ExportSafety.IsOriginalPath(@"C:\shoot\exports\..\IMG_1.jpg", originals));
    }

    [Fact]
    public void InvalidTargetPath_FailsSafeAsCollision()
    {
        var originals = Set(@"C:\shoot\IMG_1.jpg");

        Assert.True(ExportSafety.IsOriginalPath("\0invalid", originals));
        Assert.True(ExportSafety.IsOriginalPath("", originals));
    }

    [Fact]
    public void InvalidOriginalEntry_SkippedWithoutThrow()
    {
        var originals = Set(@"C:\shoot\IMG_1.jpg", "\0bad");

        Assert.Single(originals);
        Assert.True(ExportSafety.IsOriginalPath(@"C:\shoot\IMG_1.jpg", originals));
    }
}
