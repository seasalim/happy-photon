using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class XmpSidecarPathTests : IDisposable
{
    private readonly TemporaryDirectory _root = new();

    [Fact]
    public void BothNames_NewerWinsAndOtherIsReportedAsShadowed()
    {
        var image = Path.Combine(_root.Path, "IMG_1234.CR3");
        var full = image + ".xmp";
        var baseName = Path.ChangeExtension(image, ".xmp");
        File.WriteAllText(full, "full");
        File.WriteAllText(baseName, "base");
        var captured = new DateTime(2026, 8, 12, 12, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(full, captured);
        File.SetLastWriteTimeUtc(baseName, captured.AddMinutes(1));

        var result = XmpSidecarPaths.Resolve(
            image, [image], XmpSidecarNaming.FullName);

        Assert.Equal(baseName, result.Winner!.Path, ignoreCase: true);
        Assert.Equal(full, result.Shadowed!.Path, ignoreCase: true);
    }

    [Fact]
    public void RawJpegPair_MakesBaseNameAmbiguousAndCreationUsesFullName()
    {
        var raw = Path.Combine(_root.Path, "PAIR.CR3");
        var jpeg = Path.Combine(_root.Path, "PAIR.JPG");
        File.WriteAllText(Path.ChangeExtension(raw, ".xmp"), "shared");

        var result = XmpSidecarPaths.Resolve(
            raw, [raw, jpeg], XmpSidecarNaming.BaseName);

        Assert.True(result.BaseNameAmbiguous);
        Assert.Null(result.Winner);
        Assert.Equal(raw + ".xmp", result.CreationPath, ignoreCase: true);
    }

    [Fact]
    public void FolderScan_IndexesXmpWithoutTreatingItAsAnImage()
    {
        File.WriteAllText(Path.Combine(_root.Path, "one.jpg"), "image");
        File.WriteAllText(Path.Combine(_root.Path, "one.jpg.xmp"), "sidecar");

        var scan = new FolderService().ScanFolder(_root.Path);

        Assert.Single(scan.Images);
        Assert.Single(scan.SidecarPaths);
        Assert.EndsWith(".xmp", scan.SidecarPaths[0],
            StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose() => _root.Dispose();
}
