using System.ComponentModel;
using HappyPhoton.Models;
using HappyPhoton.ViewModels;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class BrowseImageStateTests
{
    private static ImageFile CreateImage(string name, int rating = 0,
        ImageFlag flag = ImageFlag.Unflagged)
    {
        var path = Path.Combine(Path.GetTempPath(), "happy-photon-browse-state", name);
        var image = new ImageFile(path) { Rating = rating, Flag = flag };
        return image;
    }

    private static BrowseImageState CreateState(params ImageFile[] images)
    {
        var state = new BrowseImageState();
        state.SetImages(images);
        return state;
    }

    [Fact]
    public void MinimumRatingZero_ShowsAll()
    {
        var state = CreateState(
            CreateImage("a.jpg", rating: 0),
            CreateImage("b.jpg", rating: 3),
            CreateImage("c.jpg", rating: 5));

        Assert.Equal(3, state.VisibleImages.Count);
    }

    [Fact]
    public void MinimumRating_ShowsRatingAndUp()
    {
        var state = CreateState(
            CreateImage("a.jpg", rating: 0),
            CreateImage("b.jpg", rating: 2),
            CreateImage("c.jpg", rating: 3),
            CreateImage("d.jpg", rating: 5));

        state.MinimumRating = 3;

        Assert.Equal(2, state.VisibleImages.Count);
        Assert.All(state.VisibleImages, i => Assert.True(i.Rating >= 3));
    }

    [Fact]
    public void RatingFilter_CombinesWithFlagAndFileTypeFilters()
    {
        var state = CreateState(
            CreateImage("a.jpg", rating: 4, flag: ImageFlag.Picked),
            CreateImage("b.jpg", rating: 4),
            CreateImage("c.jpg", rating: 1, flag: ImageFlag.Picked),
            CreateImage("d.cr2", rating: 5, flag: ImageFlag.Picked));

        state.FlagFilter = FlagFilter.Picked;
        state.MinimumRating = 4;

        Assert.Equal(2, state.VisibleImages.Count);

        state.FileTypeFilter = ImageFileTypeFilter.Jpeg;

        var only = Assert.Single(state.VisibleImages);
        Assert.Equal("a.jpg", only.FileName);
    }

    [Fact]
    public void RatingFilter_DeselectsHiddenImages()
    {
        var lowRated = CreateImage("a.jpg", rating: 1);
        var state = CreateState(lowRated, CreateImage("b.jpg", rating: 5));
        state.ToggleSelection(lowRated);
        Assert.True(lowRated.IsSelected);

        state.MinimumRating = 4;

        Assert.False(lowRated.IsSelected);
        Assert.Equal(0, state.SelectedCount);
    }

    [Fact]
    public void MatchesCurrentFilters_ProposedRatingOverload()
    {
        var image = CreateImage("a.jpg", rating: 5);
        var state = CreateState(image);
        state.MinimumRating = 3;

        Assert.True(state.MatchesCurrentFilters(image, 3));
        Assert.False(state.MatchesCurrentFilters(image, 2));
    }

    [Fact]
    public void MatchesCurrentFilters_FlagOverloadRespectsImageRating()
    {
        var image = CreateImage("a.jpg", rating: 1);
        var state = CreateState(image);
        state.MinimumRating = 3;

        Assert.False(state.MatchesCurrentFilters(image, ImageFlag.Picked));
    }

    [Fact]
    public void PhotoCountText_ShowsFilteredCountWhenRatingFilterActive()
    {
        var state = CreateState(
            CreateImage("a.jpg", rating: 5),
            CreateImage("b.jpg", rating: 0));

        Assert.Equal("2 photos", state.PhotoCountText);

        state.MinimumRating = 4;

        Assert.Equal("1 of 2 photos", state.PhotoCountText);
    }

    [Fact]
    public void EmptyMessage_MentionsRatingThreshold()
    {
        var state = CreateState(CreateImage("a.jpg", rating: 1));

        state.MinimumRating = 4;

        Assert.Empty(state.VisibleImages);
        Assert.Equal("No images rated 4+ match this filter", state.EmptyMessage);
    }

    [Fact]
    public void ClearFilters_ResetsEveryAxisInOneBatch()
    {
        var state = CreateState(
            CreateImage("first.jpg"),
            CreateImage("second.cr2", rating: 5, flag: ImageFlag.Picked));
        state.FileTypeFilter = ImageFileTypeFilter.Raw;
        state.FlagFilter = FlagFilter.Picked;
        state.MinimumRating = 5;
        state.ColorLabelFilter = ColorLabelFilter.Red;
        var filterChanges = 0;
        var stateChanges = 0;
        var propertyChanges = new List<string>();
        state.FilterChanged += (_, _) => filterChanges++;
        state.StateChanged += (_, _) => stateChanges++;
        state.PropertyChanged += (_, e) => TrackFilterProperty(e, propertyChanges);

        state.ClearFilters();

        Assert.Equal(ImageFileTypeFilter.All, state.FileTypeFilter);
        Assert.Equal(FlagFilter.All, state.FlagFilter);
        Assert.Equal(0, state.MinimumRating);
        Assert.Equal(ColorLabelFilter.All, state.ColorLabelFilter);
        Assert.Equal(2, state.VisibleCount);
        Assert.Equal(1, filterChanges);
        Assert.Equal(1, stateChanges);
        Assert.Equal(
            [nameof(state.FileTypeFilter), nameof(state.FlagFilter),
             nameof(state.MinimumRating), nameof(state.ColorLabelFilter)],
            propertyChanges);
    }

    [Fact]
    public void ClearFilters_WhenAlreadyClear_DoesNotNotify()
    {
        var state = CreateState(CreateImage("first.jpg"));
        var filterChanges = 0;
        var stateChanges = 0;
        state.FilterChanged += (_, _) => filterChanges++;
        state.StateChanged += (_, _) => stateChanges++;

        state.ClearFilters();

        Assert.Equal(0, filterChanges);
        Assert.Equal(0, stateChanges);
    }

    [Fact]
    public void Contains_TracksSetAndRemovalByReference()
    {
        var first = CreateImage("same.jpg");
        var equalPathDifferentInstance = CreateImage("same.jpg");
        var state = CreateState(first);

        Assert.True(state.Contains(first));
        Assert.False(state.Contains(equalPathDifferentInstance));

        state.Remove(first);

        Assert.False(state.Contains(first));
    }

    private static void TrackFilterProperty(
        PropertyChangedEventArgs args,
        ICollection<string> changes)
    {
        if (args.PropertyName is nameof(BrowseImageState.FileTypeFilter) or
            nameof(BrowseImageState.FlagFilter) or
            nameof(BrowseImageState.MinimumRating) or
            nameof(BrowseImageState.ColorLabelFilter))
        {
            changes.Add(args.PropertyName);
        }
    }
}
