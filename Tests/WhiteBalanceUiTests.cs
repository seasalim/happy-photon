using Avalonia.Headless.XUnit;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using HappyPhoton.Views;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class WhiteBalanceUiTests : IDisposable
{
    private readonly CatalogVmFixture _fx = new("wb-ui");

    [Theory]
    [InlineData(0, 2000)]
    [InlineData(0.5, 4900)]
    [InlineData(1, 12000)]
    public void KelvinPosition_UsesPinnedLogMapping(
        double position,
        double expectedKelvin)
    {
        Assert.Equal(
            expectedKelvin,
            MainWindowViewModel.PositionToKelvin(position));
    }

    [Fact]
    public async Task PickedWhiteBalance_IsPresentedWithEstimatedTemperature()
    {
        using var catalog = _fx.CreateCatalog();
        var vm = CreateViewModel(catalog);
        var image = new ImageFile(_fx.Path("photo.jpg"))
        {
            EditSettings = new EditSettings
            {
                Wb = new WhiteBalanceSettings
                {
                    Mode = WbMode.Picked,
                    Gains = [1.04, 1, 0.96]
                }
            }
        };

        vm.SelectedImage = image;

        Assert.Equal("Picked", vm.SelectedWhiteBalanceMode);
        Assert.EndsWith("K", vm.WhiteBalanceKelvinText);
        await vm.DisposeAsync();
    }

    [Theory]
    [InlineData("photo.jpg")]
    [InlineData("photo.dng")]
    public async Task SelectingAsShot_KeepsComboBoxItemsStable(string fileName)
    {
        using var catalog = _fx.CreateCatalog();
        var vm = CreateViewModel(catalog);
        var image = new ImageFile(_fx.Path(fileName))
        {
            EditSettings = new EditSettings
            {
                Wb = new WhiteBalanceSettings
                {
                    Mode = WbMode.Picked,
                    Gains = [1.2, 1, 0.8]
                }
            }
        };
        vm.SelectedImage = image;
        var options = vm.WhiteBalanceModeOptions;

        vm.SelectedWhiteBalanceMode = "As Shot";

        Assert.Equal("As Shot", vm.SelectedWhiteBalanceMode);
        Assert.Same(options, vm.WhiteBalanceModeOptions);
        await vm.DisposeAsync();
    }

    [Fact]
    public async Task KelvinChange_EnablesResetAndAsShotDisablesIt()
    {
        using var catalog = _fx.CreateCatalog();
        var vm = CreateViewModel(catalog);
        vm.SelectedImage = new ImageFile(_fx.Path("photo.jpg"));

        Assert.False(vm.CanReset);

        vm.WhiteBalanceKelvinPosition = 0;

        Assert.True(vm.CanReset);

        vm.SelectedWhiteBalanceMode = "As Shot";

        Assert.False(vm.CanReset);
        await vm.DisposeAsync();
    }

    [AvaloniaFact]
    public async Task AsShotAndUndo_RefreshEditedThumbnail()
    {
        var sourcePath = _fx.Path("thumbnail.jpg");
        TestImages.WriteJpeg(sourcePath, width: 320, height: 200);

        using var catalog = await _fx.CreateCatalogAsync();
        await using var imageService = new ImageService(catalog);
        var image = new ImageFile(sourcePath);
        using (var result = await imageService.Thumbnails.LoadUneditedThumbnailAsync(
            image,
            CancellationToken.None))
        {
            image.Thumbnail = result.DetachBitmap();
        }
        var original = RedBlueDelta(image);
        var vm = CreateViewModel(catalog);
        vm.IsDevelopMode = true;
        vm.Library.SetImages([image]);
        vm.SelectedImage = image;

        vm.WhiteBalanceKelvinPosition = 0;
        await WaitForThumbnailAsync(
            image,
            delta => Math.Abs(delta - original) > 10);

        vm.SelectedWhiteBalanceMode = "As Shot";
        await WaitForThumbnailAsync(
            image,
            delta => Math.Abs(delta - original) < 2);

        vm.WhiteBalanceKelvinPosition = 0;
        await WaitForThumbnailAsync(
            image,
            delta => Math.Abs(delta - original) > 10);
        await vm.UndoCommand.ExecuteAsync(null);
        await WaitForThumbnailAsync(
            image,
            delta => Math.Abs(delta - original) < 2);

        await vm.DisposeAsync();
    }

    [Fact]
    public async Task ResetWhiteBalance_IsOneUndoStep()
    {
        using var catalog = await _fx.CreateCatalogAsync();
        var vm = CreateViewModel(catalog);
        vm.IsDevelopMode = true;
        var image = new ImageFile(_fx.Path("photo.jpg"))
        {
            EditSettings = new EditSettings
            {
                Wb = new WhiteBalanceSettings
                {
                    Mode = WbMode.Custom,
                    Kelvin = 7200,
                    Tint = 12
                }
            }
        };
        vm.SelectedImage = image;

        await vm.ResetEditsCommand.ExecuteAsync(null);

        Assert.Equal(WbMode.AsShot, image.EditSettings.Wb.Mode);
        Assert.Equal("As Shot", vm.SelectedWhiteBalanceMode);
        Assert.Equal("6500K", vm.WhiteBalanceKelvinText);
        Assert.Equal("0", vm.WhiteBalanceTintText);
        Assert.True(vm.CanUndo);

        await vm.UndoCommand.ExecuteAsync(null);

        Assert.Equal(WbMode.Custom, image.EditSettings.Wb.Mode);
        Assert.Equal(7200, image.EditSettings.Wb.Kelvin);
        Assert.Equal(12, image.EditSettings.Wb.Tint);
        await vm.DisposeAsync();
    }

    [Fact]
    public void ShortcutDialog_ListsWhiteBalancePicker()
    {
        Assert.Contains(
            ShortcutCatalog.Groups.SelectMany(
                group => group.Entries),
            entry => entry.Keys == "W" &&
                     entry.Action.Contains(
                         "white balance",
                         StringComparison.OrdinalIgnoreCase));
    }

    public void Dispose() => _fx.Dispose();

    private static double RedBlueDelta(ImageFile image)
    {
        var bitmap = Assert.IsAssignableFrom<Avalonia.Media.Imaging.Bitmap>(
            image.Thumbnail);
        var pixels = BitmapConversionService.CopyBgraPixels(bitmap);
        long total = 0;
        for (var offset = 0; offset < pixels.Length; offset += 4)
        {
            total += pixels[offset + 2] - pixels[offset];
        }
        return total / (pixels.Length / 4.0);
    }

    private static async Task WaitForThumbnailAsync(
        ImageFile image,
        Func<double, bool> predicate)
    {
        var deadline = DateTime.UtcNow + TestWaits.Condition;
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                if (image.Thumbnail != null &&
                    predicate(RedBlueDelta(image)))
                {
                    return;
                }
            }
            catch (ObjectDisposedException)
            {
            }

            await Task.Delay(10);
        }

        Assert.Fail("The edited thumbnail did not reach the expected state.");
    }

    private MainWindowViewModel CreateViewModel(CatalogService catalog) =>
        _fx.CreateViewModel(
            catalog,
            loadMetadataAsync: _ => Task.CompletedTask);
}
