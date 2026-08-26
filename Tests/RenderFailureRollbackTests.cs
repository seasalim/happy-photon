using Avalonia.Headless.XUnit;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class RenderFailureRollbackTests : IDisposable
{
    private readonly TemporaryDirectory _root = new();

    [AvaloniaFact]
    public async Task CurrentEditFailureRestoresPaintedSettingsWithoutAutosave()
    {
        using var catalog = new CatalogService(Path.Combine(_root.Path, "render"));
        await catalog.InitializeAsync();
        var vm = CreateViewModel(catalog);
        var image = new ImageFile(Path.Combine(_root.Path, "render.jpg"));
        vm.SelectedImage = image;

        try
        {
            await TestWaits.UntilAsync(() => vm.PreviewImage != null);
            vm.ImageService.Previews.RenderGateAsync = () =>
                Task.FromException(new InvalidOperationException("render failed"));

            vm.Exposure = 1;
            await TestWaits.UntilAsync(() =>
                vm.Exposure == 0 && image.EditSettings.Exposure == 0);
            var states = await catalog.LoadImageStatesAsync([image.FilePath]);

            Assert.Equal(0, states[image.FilePath].EditSettings.Exposure);
            Assert.False(image.HasEdits);
        }
        finally
        {
            await vm.DisposeAsync();
        }
    }

    [AvaloniaFact]
    public async Task CropSaveFailureRestoresSettingsAndTerminatesReservation()
    {
        var catalog = new CatalogService(Path.Combine(_root.Path, "crop"));
        await catalog.InitializeAsync();
        var vm = CreateViewModel(catalog);
        var image = new ImageFile(Path.Combine(_root.Path, "crop.jpg"));
        vm.SelectedImage = image;

        try
        {
            await TestWaits.UntilAsync(() => vm.PreviewImage != null);
            vm.ToggleCropModeCommand.Execute(null);
            var draft = new CropRegion
            {
                Left = 0.1,
                Top = 0.1,
                Right = 0.9,
                Bottom = 0.9
            };
            vm.CurrentCrop = draft;
            var priorGeneration = vm.LatestPreviewOutcomeGeneration;
            catalog.Dispose();

            var apply = vm.ApplyCropCommand.ExecuteAsync(null);
            await Assert.ThrowsAnyAsync<Exception>(() => apply);

            Assert.True(vm.LatestPreviewOutcomeGeneration > priorGeneration);
            Assert.Null(image.EditSettings.Crop);
            Assert.False(image.HasEdits);
            Assert.True(vm.IsCropMode);
            Assert.Same(draft, vm.CurrentCrop);
        }
        finally
        {
            await vm.DisposeAsync();
            catalog.Dispose();
        }
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        _root.Dispose();
    }

    private static MainWindowViewModel CreateViewModel(CatalogService catalog) =>
        new(
            catalog,
            new GrayLoader(),
            loadMetadataAsync: _ => Task.CompletedTask,
            availabilityService: new TestSourceAvailabilityService(
                SourceAvailability.AvailableLocally))
        {
            IsDevelopMode = true
        };

    private sealed class GrayLoader : IBaseImageLoader
    {
        public bool CanLoad(ImageFile file) => true;

        public BaseImage LoadPreviewBase(
            ImageFile file,
            BaseDecodeSettings decode,
            CancellationToken cancellationToken) =>
            new(
                new MagickImage(MagickColors.Gray, 64, 48),
                new BaseImageInfo(
                    BaseSourceKind.Standard,
                    false,
                    decode,
                    null,
                    null,
                    6504,
                    0,
                    false,
                    null,
                    1,
                    64,
                    48));

        BaseImageLoadOutcome IBaseImageLoader.LoadPreviewBaseWithOutcome(
            ImageFile file,
            BaseDecodeSettings decode,
            CancellationToken cancellationToken) =>
            BaseImageLoadOutcome.Loaded(LoadPreviewBase(
                file,
                decode,
                cancellationToken));

        public BaseImage? LoadFullBase(
            ImageFile file,
            BaseDecodeSettings decode,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
