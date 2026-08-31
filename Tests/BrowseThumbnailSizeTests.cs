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
    [InlineData(BrowseThumbnailSize.Small, 116, 77, 6, 6)]
    [InlineData(BrowseThumbnailSize.Medium, 176, 117, 4, 4)]
    [InlineData(BrowseThumbnailSize.Large, 276, 184, 2, 3)]
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
        Assert.Equal(width + 10, geometry.ItemWidth);
        Assert.Equal(height + 35, geometry.ItemHeight);
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
