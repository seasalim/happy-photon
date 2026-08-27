using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class VersionReviewRegressionTests : IDisposable
{
    private readonly CatalogVmFixture _fixture = new("version-review");

    [Fact]
    public async Task NewVersion_InheritsAssessmentsAndRemainsVisibleUnderFilters()
    {
        using var catalog = await _fixture.CreateCatalogAsync("filtered-catalog");
        var folder = Directory.CreateDirectory(_fixture.Path("filtered-photos")).FullName;
        var path = Path.Combine(folder, "rated.jpg");
        TestImages.WriteJpeg(path);
        var primaryId = await catalog.GetOrCreateImageAsync(path);
        await catalog.SaveEditSettingsAsync(
            primaryId, new EditSettings { Exposure = 0.75 });
        await catalog.MutateAssessmentsAsync([
            new AssessmentMutation(
                primaryId,
                AssessmentAxes.Rating | AssessmentAxes.Flag | AssessmentAxes.Label,
                ImageFlag.Picked,
                5,
                ColorLabel.Red,
                AssessmentAxes.Rating | AssessmentAxes.Flag | AssessmentAxes.Label)
        ]);
        await using var viewModel = _fixture.CreateViewModel(
            catalog,
            availabilityService: new TestSourceAvailabilityService(
                SourceAvailability.RequiresHydration),
            postSelection: action => action());
        await viewModel.LoadFolderAsync(folder);
        var primary = Assert.Single(viewModel.Browse.AllImages);
        viewModel.Browse.MinimumRating = 5;
        viewModel.Browse.FlagFilter = FlagFilter.Picked;
        viewModel.Browse.ColorLabelFilter = ColorLabelFilter.Red;
        viewModel.Browse.SelectOnly(primary);
        viewModel.SelectedImage = primary;

        await viewModel.NewVersionFromCurrentCommand.ExecuteAsync(null);

        Assert.Equal(2, viewModel.Browse.VisibleImages.Count);
        var sibling = Assert.Single(
            viewModel.Browse.AllImages, image => image.Version == 2);
        Assert.Same(sibling, viewModel.SelectedImage);
        Assert.True(sibling.IsSelected);
        Assert.Equal(0.75, sibling.EditSettings.Exposure);
        Assert.Equal(5, sibling.Rating);
        Assert.Equal(ImageFlag.Picked, sibling.Flag);
        Assert.Equal(ColorLabel.Red, sibling.ColorLabel);
        Assert.Equal(AssessmentAxes.None, sibling.PendingAssessmentAxes);
    }

    [Fact]
    public async Task DeleteFile_RemovesAllRowsAndEveryDiscoveredAssetSet()
    {
        using var catalog = await _fixture.CreateCatalogAsync("delete-catalog");
        var path = _fixture.Path("delete-source.jpg");
        await File.WriteAllBytesAsync(path, [1]);
        var primary = await catalog.GetOrCreateImageAsync(path);
        var second = (await catalog.CreateVersionAsync(primary))!;
        var third = (await catalog.CreateVersionAsync(primary))!;
        await catalog.MutateAssessmentsAsync([
            new AssessmentMutation(second.CatalogId, AssessmentAxes.Rating, Rating: 4),
            new AssessmentMutation(third.CatalogId, AssessmentAxes.Flag,
                Flag: ImageFlag.Rejected)
        ]);
        var assets = new[] { primary, second.CatalogId, third.CatalogId }
            .SelectMany(id => new[]
            {
                catalog.GetThumbnailPath(id),
                catalog.GetPreviewPath(id),
                Path.ChangeExtension(catalog.GetPreviewPath(id), ".meta"),
                catalog.GetRenderedThumbnailPath(id),
                Path.ChangeExtension(catalog.GetRenderedThumbnailPath(id), ".meta")
            }).ToArray();
        foreach (var asset in assets)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(asset)!);
            await File.WriteAllTextAsync(asset, "cache");
        }

        await catalog.DeleteFileAsync(path);

        Assert.Empty(await catalog.LoadImageStatesAsync([path]));
        Assert.All(assets, asset => Assert.False(File.Exists(asset)));
        Assert.True(File.Exists(path));
    }

    [Fact]
    public async Task NewVersion_ReappliesDisplayedBurstMembership()
    {
        using var catalog = await _fixture.CreateCatalogAsync("burst-catalog");
        var folder = Directory.CreateDirectory(_fixture.Path("burst-photos")).FullName;
        TestImages.WriteJpeg(Path.Combine(folder, "one.jpg"));
        TestImages.WriteJpeg(Path.Combine(folder, "two.jpg"));
        await using var viewModel = _fixture.CreateViewModel(
            catalog,
            loadMetadataAsync: image =>
            {
                image.ApplyMetadata(new ImageMetadata
                {
                    DateTaken = new DateTime(2026, 8, 8, 12, 0,
                        image.FileName == "one.jpg" ? 0 : 1)
                });
                return Task.CompletedTask;
            },
            postSelection: action => action());
        await viewModel.LoadFolderAsync(folder);
        viewModel.ShowBurstGroups = true;
        await viewModel.WaitForBurstAnalysisAsync();
        var source = viewModel.Browse.AllImages.Single(
            image => image.FileName == "one.jpg");
        viewModel.Browse.SelectOnly(source);
        viewModel.SelectedImage = source;

        await viewModel.NewVersionFromCurrentCommand.ExecuteAsync(null);

        var sibling = viewModel.Browse.AllImages.Single(
            image => image.FilePath == source.FilePath && image.Version == 2);
        Assert.True(sibling.HasBurstGroup);
        Assert.Equal(source.BurstGroupOrdinal, sibling.BurstGroupOrdinal);
        Assert.Equal(source.BurstIndex, sibling.BurstIndex);
        Assert.Equal(source.BurstSize, sibling.BurstSize);
    }

    public void Dispose() => _fixture.Dispose();
}
