using HappyPhoton.Models;
using HappyPhoton.Views;
using HappyPhoton.ViewModels;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class BrowseThumbnailSizeTests
{
    [Theory]
    [InlineData(BrowseThumbnailSize.Small, 150, 150)]
    [InlineData(BrowseThumbnailSize.Medium, 150, 192)]
    [InlineData(BrowseThumbnailSize.Large, 512, 512)]
    public void RequestMapping_UsesPresetQualityTargets(
        BrowseThumbnailSize size,
        int minimum,
        int generation)
    {
        var request = ThumbnailSizeRequest.For(size);

        Assert.Equal(minimum, request.MinimumDimension);
        Assert.Equal(generation, request.GenerationDimension);
    }

    [Fact]
    public void Request_RejectsGenerationBelowMinimum() =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ThumbnailSizeRequest(512, 192));

    [Theory]
    [InlineData(BrowseThumbnailSize.Small, 120, 80, 5, 5)]
    [InlineData(BrowseThumbnailSize.Medium, 180, 120, 4, 4)]
    [InlineData(BrowseThumbnailSize.Large, 280, 187, 2, 2)]
    public void Geometry_DrivesGridCalculations(
        BrowseThumbnailSize size,
        double width,
        double height,
        int itemsPerRow,
        int rowsPerPage)
    {
        var geometry = BrowseThumbnailGeometry.For(size);

        Assert.Equal(width, geometry.ImageWidth);
        Assert.Equal(height, geometry.ImageHeight);
        Assert.Equal(itemsPerRow, (int)Math.Floor(
            (780 + geometry.ColumnSpacing) /
            (geometry.ItemWidth + geometry.ColumnSpacing)));
        Assert.Equal(rowsPerPage, (int)Math.Floor(720 / geometry.RowHeight));
    }

    [Fact]
    public void LargeInitialBurstUsesSmallStageWithoutChangingDesiredRequest()
    {
        var desired = ThumbnailSizeRequest.For(BrowseThumbnailSize.Large);

        var initial = MainWindowViewModel.GetInitialThumbnailRequest(desired);

        Assert.Equal(ThumbnailSizeRequest.For(BrowseThumbnailSize.Small), initial);
        Assert.Equal(new ThumbnailSizeRequest(512, 512), desired);
    }
}
