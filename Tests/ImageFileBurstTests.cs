using HappyPhoton.Models;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class ImageFileBurstTests
{
    [Fact]
    public void DefaultsToNoBurst()
    {
        var image = new ImageFile(@"C:\photos\a.jpg");

        Assert.False(image.HasBurstGroup);
        Assert.Equal(0, image.BurstSize);
    }

    [Fact]
    public void ChipTextAndColorIndex()
    {
        var image = new ImageFile(@"C:\photos\a.jpg")
        {
            BurstGroupOrdinal = 8, BurstIndex = 2, BurstSize = 5
        };

        Assert.True(image.HasBurstGroup);
        Assert.Equal("2/5", image.BurstChipText);
        Assert.Equal(1, image.BurstColorIndex);
    }

    [Fact]
    public void BurstChangesNotifyComputedProperties()
    {
        var image = new ImageFile(@"C:\photos\a.jpg");
        var changed = new List<string?>();
        image.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        image.BurstSize = 4;
        image.BurstIndex = 3;
        image.BurstGroupOrdinal = 2;

        Assert.Contains(nameof(ImageFile.HasBurstGroup), changed);
        Assert.Contains(nameof(ImageFile.BurstChipText), changed);
        Assert.Contains(nameof(ImageFile.BurstColorIndex), changed);
    }
}
