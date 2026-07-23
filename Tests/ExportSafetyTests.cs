using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class ExportSafetyTests
{
    private static readonly string ShootPath = Path.Combine(
        Path.GetTempPath(), "happy-photon-export-safety");

    private static HashSet<string> Set(params string[] paths) =>
        ExportSafety.BuildOriginalPathSet(paths);

    private static string PhotoPath(string fileName) =>
        Path.Combine(ShootPath, fileName);

    [Fact]
    public void SelfCollision_Detected()
    {
        var originals = Set(PhotoPath("IMG_1.jpg"));

        Assert.True(ExportSafety.IsOriginalPath(PhotoPath("IMG_1.jpg"), originals));
    }

    [Fact]
    public void SiblingCollision_RawExportHitsJpegOriginal()
    {
        // Exporting IMG_1.CR2 as JPEG into the source folder targets IMG_1.jpg —
        // the sibling original.
        var originals = Set(PhotoPath("IMG_1.CR2"), PhotoPath("IMG_1.jpg"));

        Assert.True(ExportSafety.IsOriginalPath(PhotoPath("IMG_1.jpg"), originals));
    }

    [Fact]
    public void CaseInsensitive_OnEveryPlatform()
    {
        var originals = Set(PhotoPath("img_1.JPG"));

        Assert.True(ExportSafety.IsOriginalPath(PhotoPath("IMG_1.jpg"), originals));
    }

    [Fact]
    public void ExportsSubfolder_DoesNotCollide()
    {
        var originals = Set(PhotoPath("IMG_1.jpg"));

        var target = Path.Combine(ShootPath, "exports", "IMG_1.jpg");
        Assert.False(ExportSafety.IsOriginalPath(target, originals));
    }

    [Fact]
    public void RelativeSegments_NormalizeOntoOriginal()
    {
        var originals = Set(PhotoPath("IMG_1.jpg"));

        var target = Path.Combine(ShootPath, "exports", "..", "IMG_1.jpg");
        Assert.True(ExportSafety.IsOriginalPath(target, originals));
    }

    [Fact]
    public void InvalidTargetPath_FailsSafeAsCollision()
    {
        var originals = Set(PhotoPath("IMG_1.jpg"));

        Assert.True(ExportSafety.IsOriginalPath("\0invalid", originals));
        Assert.True(ExportSafety.IsOriginalPath("", originals));
    }

    [Fact]
    public void InvalidOriginalEntry_SkippedWithoutThrow()
    {
        var originalPath = PhotoPath("IMG_1.jpg");
        var originals = Set(originalPath, "\0bad");

        Assert.Single(originals);
        Assert.True(ExportSafety.IsOriginalPath(originalPath, originals));
    }
}
