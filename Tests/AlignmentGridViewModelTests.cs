using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class AlignmentGridViewModelTests : IDisposable
{
    private readonly TestTimeProvider _clock = new();
    private readonly CatalogVmFixture _fx = new("alignment-grid");

    [Theory]
    [InlineData("vertical")]
    [InlineData("horizontal")]
    [InlineData("aspect")]
    [InlineData("distortion")]
    public async Task GeometryChangesShowThenHideGrid(string control)
    {
        using var catalog = await _fx.CreateCatalogAsync("catalog");
        await using var vm = CreateViewModel(catalog);

        SetGeometry(vm, control, 1);

        Assert.True(vm.IsAlignmentGridVisible);
        _clock.Advance(TimeSpan.FromMilliseconds(1490));
        Assert.True(vm.IsAlignmentGridVisible);
        _clock.Advance(TimeSpan.FromMilliseconds(20));
        await TestWaits.UntilAsync(() => !vm.IsAlignmentGridVisible);
    }

    [Fact]
    public async Task RepeatedGeometryChangeRestartsHold()
    {
        using var catalog = await _fx.CreateCatalogAsync("restart-catalog");
        await using var vm = CreateViewModel(catalog);

        vm.GeometryVertical = 1;
        _clock.Advance(TimeSpan.FromMilliseconds(1400));
        vm.GeometryHorizontal = 1;
        _clock.Advance(TimeSpan.FromMilliseconds(1400));

        Assert.True(vm.IsAlignmentGridVisible);
        _clock.Advance(TimeSpan.FromMilliseconds(110));
        await TestWaits.UntilAsync(() => !vm.IsAlignmentGridVisible);
    }

    [Fact]
    public async Task NonGeometryChangeAndSettingsLoadDoNotShowGrid()
    {
        using var catalog = await _fx.CreateCatalogAsync("load-catalog");
        await using var vm = _fx.CreateViewModel(
            catalog,
            new NullBaseLoader(),
            _ => Task.CompletedTask,
            timeProvider: _clock);
        vm.IsDevelopMode = true;
        var image = new ImageFile(_fx.Path("loaded.jpg"))
        {
            EditSettings = new EditSettings
            {
                Geometry = new GeometrySettings { Vertical = 25 }
            }
        };

        vm.SelectedImage = image;
        Assert.False(vm.IsAlignmentGridVisible);

        vm.Contrast = 1;
        Assert.False(vm.IsAlignmentGridVisible);
    }

    [Fact]
    public async Task TransitionClearsCannotBeUndoneByArmedHide()
    {
        using var catalog = await _fx.CreateCatalogAsync("transition-catalog");
        await using var vm = CreateViewModel(catalog);

        vm.GeometryVertical = 1;
        vm.IsCropMode = true;
        Assert.False(vm.IsAlignmentGridVisible);

        vm.IsCropMode = false;
        vm.GeometryHorizontal = 1;
        vm.SelectedImage = new ImageFile(_fx.Path("second.jpg"));
        Assert.False(vm.IsAlignmentGridVisible);

        vm.GeometryAspect = 1;
        vm.IsDevelopMode = false;
        Assert.False(vm.IsAlignmentGridVisible);

        _clock.Advance(TimeSpan.FromSeconds(2));
        Assert.False(vm.IsAlignmentGridVisible);
    }

    private MainWindowViewModel CreateViewModel(CatalogService catalog)
    {
        var vm = _fx.CreateViewModel(
            catalog,
            new NullBaseLoader(),
            _ => Task.CompletedTask,
            timeProvider: _clock);
        vm.IsDevelopMode = true;
        vm.SelectedImage = new ImageFile(_fx.Path("photo.jpg"));
        return vm;
    }

    private static void SetGeometry(
        MainWindowViewModel vm,
        string control,
        int value)
    {
        switch (control)
        {
            case "vertical":
                vm.GeometryVertical = value;
                break;
            case "horizontal":
                vm.GeometryHorizontal = value;
                break;
            case "aspect":
                vm.GeometryAspect = value;
                break;
            case "distortion":
                vm.GeometryDistortion = value;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(control));
        }
    }

    public void Dispose() => _fx.Dispose();
}
