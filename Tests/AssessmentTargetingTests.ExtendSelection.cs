using HappyPhoton.Models;
using Xunit;

namespace HappyPhoton.Tests;

public sealed partial class AssessmentTargetingTests
{
    [Fact]
    public async Task ExtendSelection_SelectsExactlyTheSpanFromTheAnchorInBrowseGrid()
    {
        using var catalog = await CreateCatalogAsync();
        await using var vm = CreateViewModel(catalog);
        var images = new List<ImageFile>();
        for (var i = 1; i <= 5; i++) images.Add(await CreateImageAsync(catalog, $"{i}.jpg"));
        vm.Browse.SetImages(images);
        vm.SelectedImage = images[1];
        vm.Browse.SelectOnly(images[1]);

        var anchor = vm.ExtendSelection(null, 1);
        Assert.Same(images[1], anchor);
        vm.ExtendSelection(anchor, 1);
        Assert.Same(images[3], vm.SelectedImage);
        Assert.Equal(images[1..4], vm.Browse.GetSelectedImages());
        Assert.Equal(3, vm.SelectedCount);

        vm.ExtendSelection(anchor, -1);
        Assert.Equal(images[1..3], vm.Browse.GetSelectedImages());

        vm.ExtendSelection(anchor, -2);
        Assert.Same(images[0], vm.SelectedImage);
        Assert.Equal(images[0..2], vm.Browse.GetSelectedImages());

        vm.ExtendSelection(anchor, -1);
        Assert.Same(images[0], vm.SelectedImage);
        Assert.Equal(images[0..2], vm.Browse.GetSelectedImages());

        Assert.Same(images[1], vm.ExtendSelectionToEdge(anchor, last: true));
        Assert.Same(images[4], vm.SelectedImage);
        Assert.Equal(images[1..], vm.Browse.GetSelectedImages());
    }

    [Fact]
    public async Task ExtendSelection_OnlyMovesFocusOutsideBrowseGrid()
    {
        using var catalog = await CreateCatalogAsync();
        await using var vm = CreateViewModel(catalog);
        var first = await CreateImageAsync(catalog, "first.jpg");
        var second = await CreateImageAsync(catalog, "second.jpg");
        vm.Browse.SetImages([first, second]);
        vm.SelectedImage = first;
        vm.Browse.SelectOnly(first);
        vm.IsDevelopMode = true;

        vm.ExtendSelection(first, 1);

        Assert.Same(second, vm.SelectedImage);
        Assert.Same(first, Assert.Single(vm.Browse.GetSelectedImages()));
    }
}
