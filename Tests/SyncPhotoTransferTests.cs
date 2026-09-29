using HappyPhoton.Models;
using HappyPhoton.Services;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class SyncPhotoTransferTests
{
    [Fact]
    public void SameAspectTargetReceivesCropAndStraightenExactly()
    {
        using var directory = new TemporaryDirectory();
        var source = Image(directory.Path, "source.png", 300, 200);
        var target = Image(directory.Path, "target.png", 600, 400);
        source.EditSettings = SyncPhotoGateSupport.PhotoA();
        var result = Apply(source, target, out var reframed, out var unavailable);
        Assert.False(reframed);
        Assert.False(unavailable);
        Assert.Equal(EditSettingsJson.Serialize(source.EditSettings), EditSettingsJson.Serialize(result));
    }

    [Theory]
    [InlineData(400, 300, 0)]
    [InlineData(300, 200, 90)]
    [InlineData(300, 200, 270)]
    public void DifferentAspectOrRotatedTargetReceivesD4Crop(int width, int height, int rotation)
    {
        using var directory = new TemporaryDirectory();
        var source = Image(directory.Path, "source.png", 300, 200);
        var target = Image(directory.Path, "target.png", width, height);
        source.EditSettings = SyncPhotoGateSupport.PhotoA();
        target.EditSettings.Rotation = rotation;
        var result = Apply(source, target, out var reframed, out var unavailable);
        Assert.True(reframed);
        Assert.False(unavailable);
        Assert.Equal(rotation, result.Rotation);
        Assert.Equal(source.EditSettings.HorizonRotation, result.HorizonRotation);
        var frame = rotation == 0 ? (width, height) : (height, width);
        AssertCropRatio(result.Crop!, frame, rotation == 0 ? 1.5 : 1 / 1.5);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(.8, 0)]
    [InlineData(0, .8)]
    [InlineData(.8, .8)]
    public void D4LargestRectangleShiftsInsideFrame(double left, double top)
    {
        var crop = new CropRegion { Left = left, Top = top, Right = left + .2, Bottom = top + .2 };
        var result = CropTransfer.Apply(crop, (300, 200), (400, 300));
        AssertCropRatio(result, (400, 300), 1.5);
        Assert.Equal(1, result.Right - result.Left, 10);
        Assert.Equal(Math.Clamp(top + .1 - (result.Bottom - result.Top) / 2, 0,
            1 - (result.Bottom - result.Top)), result.Top, 10);
    }

    [Fact]
    public void AspectToleranceAndOrientationBoundaryArePinned()
    {
        Assert.True(CropTransfer.Matches((1500, 1000), (1501, 1000)));
        Assert.False(CropTransfer.Matches((1500, 1000), (1520, 1000)));
        Assert.False(CropTransfer.Matches((1001, 1000), (1000, 1001)));
        Assert.True(CropTransfer.Matches((1000, 1000), (2000, 2000)));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void FactsUnavailableSkipsWholeCropGroupInEitherDirection(bool sourceMissing)
    {
        using var directory = new TemporaryDirectory();
        var source = Image(directory.Path, "source.png", 300, 200);
        var target = Image(directory.Path, "target.png", 300, 200);
        source.EditSettings = SyncPhotoGateSupport.PhotoA();
        target.EditSettings.Crop = new CropRegion { Left = .2 };
        target.EditSettings.HorizonRotation = -2;
        File.Delete(sourceMissing ? source.FilePath : target.FilePath);
        var result = Apply(source, target, out var reframed, out var unavailable);
        Assert.True(unavailable);
        Assert.False(reframed);
        Assert.Equal(.2, result.Crop!.Left);
        Assert.Equal(-2, result.HorizonRotation);
        Assert.Equal(8, result.Locals!.Count);
        Assert.Equal(source.EditSettings.Geometry!.Vertical, result.Geometry!.Vertical);
    }

    [Fact]
    public void FullCropNeedsNoFactsAndQuarterTurnNeverTransfers()
    {
        var source = new ImageFile("absent-source.dng") { EditSettings = new() { Rotation = 90, HorizonRotation = 3 } };
        var target = new ImageFile("absent-target.dng") { EditSettings = new() { Rotation = 270, Crop = new() { Left = .2 } } };
        var result = Apply(source, target, out _, out var unavailable);
        Assert.False(unavailable);
        Assert.Null(result.Crop);
        Assert.Equal(3, result.HorizonRotation);
        Assert.Equal(270, result.Rotation);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LocalsReplaceMutableStateAndShareImmutableStrokesWithIdsAndCapsIntact(bool brush)
    {
        var source = new ImageFile("source.dng") { EditSettings = brush ? SyncPhotoBrushFixture.Create() : HealWorkloads.LH8() };
        var target = new ImageFile("target.dng") { EditSettings = new() { Locals = [new LocalAdjustment()] } };
        var result = Apply(source, target, out _, out _);
        Assert.Equal(EditSettingsJson.Serialize(source.EditSettings), EditSettingsJson.Serialize(result));
        Assert.Equal(8, result.Locals!.Count);
        Assert.NotSame(source.EditSettings.Locals![0], result.Locals[0]);
        Assert.Equal(source.EditSettings.Locals[0].Id, result.Locals[0].Id);

        if (brush)
        {
            Assert.Equal(96, result.Locals.Sum(local => local.Strokes!.Count));
            Assert.Equal(4000, result.Locals.Sum(local => local.Strokes!.Sum(stroke => stroke.Points.Count)));
            var clone = source.EditSettings.Clone();
            Assert.Same(source.EditSettings.Locals[0].Strokes, clone.Locals![0].Strokes);
            var transferred = EditSettingsTransfer.CopyGroups(source.EditSettings,
                [EditSettingsTransfer.Groups.Single(group => group.Name == "Locals")]);
            Assert.Same(source.EditSettings.Locals[0].Strokes, transferred.Locals![0].Strokes);
            Assert.Same(source.EditSettings.Locals[0].Strokes![0], transferred.Locals[0].Strokes![0]);
            Assert.Same(source.EditSettings.Locals[0].Strokes![0].Points, transferred.Locals[0].Strokes![0].Points);
        }

        var before = source.EditSettings.Locals[0].Exposure;
        result.Locals[0].Exposure = before - .5;
        Assert.Equal(before, source.EditSettings.Locals[0].Exposure);
    }

    [Fact]
    public void SourceLocalAndRangeEditsCannotMutatePastedTarget()
    {
        var source = new ImageFile("source.dng") { EditSettings = SyncPhotoBrushFixture.Create() };
        var local = source.EditSettings.Locals![0];
        local.Luminance = new() { Enabled = true, Lower = .2, Upper = .8 };
        local.Hue = new() { Enabled = true, Center = 120, Width = 40 };
        var transferred = EditSettingsTransfer.CopyGroups(source.EditSettings,
            [EditSettingsTransfer.Groups.Single(group => group.Name == "Locals")]);
        Assert.NotSame(local, transferred.Locals![0]);
        Assert.Same(local.Strokes, transferred.Locals[0].Strokes);
        var expectedTransfer = EditSettingsJson.Serialize(transferred);
        var result = Apply(source, new ImageFile("target.dng"), out _, out _);
        var expected = EditSettingsJson.Serialize(result);
        Assert.NotSame(local, result.Locals![0]);

        local.Exposure = -1;
        // Range records and strokes are immutable; edits replace them on the mutable local.
        local.Luminance = local.Luminance with { Lower = .4 };
        local.Hue = local.Hue with { Center = 240 };
        local.Strokes = [local.Strokes![0] with { Flow = .5 }];
        source.EditSettings.Locals.Clear();

        Assert.Equal(expectedTransfer, EditSettingsJson.Serialize(transferred));
        Assert.Equal(expected, EditSettingsJson.Serialize(result));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void FailedHeaderReadIsRetriedOnNextPaste(bool sourceFails)
    {
        using var directory = new TemporaryDirectory();
        var source = Image(directory.Path, "source.png", 300, 200);
        var target = Image(directory.Path, "target.png", 300, 200);
        source.EditSettings.Crop = new() { Left = .2 };
        var reader = new PhotoFrameFactsReader(new SourceAvailabilityService());
        var attempts = 0;
        reader.Opening = (path, _) =>
        {
            if (path != (sourceFails ? source.FilePath : target.FilePath)) return;

            if (++attempts == 1) throw new IOException("Transient sharing violation");
        };
        var groups = EditSettingsTransfer.Groups.Where(group => group.Name == "Crop & Straighten").ToArray();
        var first = PhotoSettingsTransfer.Apply(source, source.EditSettings, target, target.EditSettings,
            groups, reader, out _, out var unavailable);
        Assert.True(unavailable);
        Assert.Null(first.Crop);
        var second = PhotoSettingsTransfer.Apply(source, source.EditSettings, target, target.EditSettings,
            groups, reader, out _, out unavailable);
        Assert.False(unavailable);
        Assert.Equal(.2, second.Crop!.Left);
        Assert.Equal(2, attempts);
        Assert.Equal((300, 200), reader.Read(sourceFails ? source : target, second, cachedOnly: true));
        Assert.Equal(2, attempts);
    }

    [Fact]
    public void CloudOnlyFrameIsRefusedBeforeAnyRead()
    {
        var reader = new PhotoFrameFactsReader(new TestSourceAvailabilityService(SourceAvailability.RequiresHydration));
        reader.Opening = (_, _) => Assert.Fail("An online-only file was opened");
        Assert.Null(reader.Read(new ImageFile("online.dng"), new EditSettings()));
    }

    private static EditSettings Apply(ImageFile source, ImageFile target, out bool reframed, out bool unavailable) =>
        PhotoSettingsTransfer.Apply(source, source.EditSettings, target, target.EditSettings,
            EditSettingsTransfer.Groups.Where(group => group.Name is "Crop & Straighten" or "Geometry" or "Locals").ToArray(),
            new PhotoFrameFactsReader(new SourceAvailabilityService()), out reframed, out unavailable);

    private static ImageFile Image(string directory, string name, int width, int height)
    {
        var path = Path.Combine(directory, name);
        using var image = new MagickImage(MagickColors.Gray, (uint)width, (uint)height);
        image.Write(path);

        return new ImageFile(path);
    }

    private static void AssertCropRatio(CropRegion crop, (int Width, int Height) frame, double ratio)
    {
        Assert.InRange(crop.Left, 0, 1);
        Assert.InRange(crop.Top, 0, 1);
        Assert.InRange(crop.Right, crop.Left, 1);
        Assert.InRange(crop.Bottom, crop.Top, 1);
        Assert.Equal(ratio, (crop.Right - crop.Left) * frame.Width / ((crop.Bottom - crop.Top) * frame.Height), 10);
    }
}
