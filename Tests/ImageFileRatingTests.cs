using HappyPhoton.Models;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class ImageFileRatingTests
{
    [Fact]
    public void RatingDefaultsToZeroUnrated()
    {
        var image = new ImageFile(@"C:\photos\a.jpg");

        Assert.Equal(0, image.Rating);
        Assert.False(image.HasRating);
        Assert.Equal(string.Empty, image.RatingStars);
    }

    [Fact]
    public void RatingStarsShowsFilledStarsOnly()
    {
        var image = new ImageFile(@"C:\photos\a.jpg");

        image.Rating = 3;
        Assert.True(image.HasRating);
        Assert.Equal("★★★", image.RatingStars);

        image.Rating = 5;
        Assert.Equal("★★★★★", image.RatingStars);
    }

    [Fact]
    public void RatingChangeNotifiesComputedProperties()
    {
        var image = new ImageFile(@"C:\photos\a.jpg");
        var changed = new List<string?>();
        image.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        image.Rating = 4;

        Assert.Contains(nameof(ImageFile.Rating), changed);
        Assert.Contains(nameof(ImageFile.HasRating), changed);
        Assert.Contains(nameof(ImageFile.RatingStars), changed);
    }
}
