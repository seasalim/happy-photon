using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class CapturePairingServiceTests
{
    [Fact]
    public void GroupsSameDirectoryRawAndJpegWithCaseInsensitiveBasename()
    {
        var raw = Path.Combine("photos", "IMG_0001.CR3");
        var jpeg = Path.Combine("photos", "img_0001.jpeg");

        Assert.True(CapturePairingService.IsRawJpegPair(raw, jpeg));
        var capture = Assert.Single(CapturePairingService.GroupCaptures([jpeg, raw]));
        Assert.Equal(
            new[] { jpeg, raw }.OrderBy(path => path, StringComparer.Ordinal),
            capture.ImageIds);
    }

    [Fact]
    public void DoesNotPairAcrossDirectories()
    {
        var raw = Path.Combine("first", "same.nef");
        var jpeg = Path.Combine("second", "same.jpg");

        Assert.False(CapturePairingService.IsRawJpegPair(raw, jpeg));
        Assert.Equal(2, CapturePairingService.GroupCaptures([raw, jpeg]).Count);
    }

    [Fact]
    public void DoesNotPairFilesWithTheSameRole()
    {
        var firstJpeg = Path.Combine("photos", "same.jpg");
        var secondJpeg = Path.Combine("photos", "same.jpeg");
        var firstRaw = Path.Combine("photos", "same.cr3");
        var secondRaw = Path.Combine("photos", "same.dng");

        Assert.False(CapturePairingService.IsRawJpegPair(firstJpeg, secondJpeg));
        Assert.False(CapturePairingService.IsRawJpegPair(firstRaw, secondRaw));
    }

    [Theory]
    [InlineData("same.cr3", "same.jpg", "same.jpeg")]
    [InlineData("same.cr3", "same.dng", "same.jpg")]
    public void SameRoleDuplicatesMakeTheStemAmbiguous(
        string first,
        string second,
        string third)
    {
        var paths = new[] { first, second, third }
            .Select(name => Path.Combine("photos", name))
            .ToArray();

        var captures = CapturePairingService.GroupCaptures(paths);

        Assert.Equal(3, captures.Count);
        Assert.All(captures, capture => Assert.Single(capture.ImageIds));
    }

    [Fact]
    public void OtherFormatsStaySingletonWithoutInvalidatingPair()
    {
        var raw = Path.Combine("photos", "same.arw");
        var jpeg = Path.Combine("photos", "same.jpg");
        var png = Path.Combine("photos", "same.png");

        var captures = CapturePairingService.GroupCaptures([png, jpeg, raw]);

        Assert.Equal(2, captures.Count);
        Assert.Contains(captures, capture =>
            capture.ImageIds.SequenceEqual(
                new[] { jpeg, raw }.OrderBy(path => path, StringComparer.Ordinal)));
        Assert.Contains(captures, capture => capture.ImageIds.SequenceEqual([png]));
    }
}
