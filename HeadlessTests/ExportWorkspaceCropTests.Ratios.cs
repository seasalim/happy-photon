using Avalonia;
using Avalonia.Headless.XUnit;
using HappyPhoton.Models;
using HappyPhoton.Services;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

public sealed partial class ExportWorkspaceCropTests
{
    [AvaloniaTheory]
    [InlineData(0, 0)]
    [InlineData(90, 0)]
    [InlineData(0, 2)]
    public async Task PresetBeforeUncroppedPreviewUsesNativeCorrectedFrameAndExports(int rotation, double horizon)
    {
        using var catalog = await _fixture.CreateCatalogAsync($"ratio-{rotation}-{horizon}");
        var loader = new RatioBaseLoader();
        var clock = new TestTimeProvider();
        await using var vm = _fixture.CreateViewModel(catalog, loader, _ => Task.CompletedTask, timeProvider: clock);
        var image = await CreateImageAsync(catalog, "ratio.jpg");
        using (var source = new MagickImage(MagickColors.Gray, 1200, 900)) source.Write(image.FilePath);

        image.EditSettings = new EditSettings
        {
            Rotation = rotation,
            HorizonRotation = horizon,
            Geometry = new GeometrySettings { Aspect = 10 },
            Crop = new CropRegion { Left = .2, Top = .3, Right = .8, Bottom = .7 }
        };
        await catalog.SaveEditSettingsAsync(image.CatalogId, image.EditSettings);
        vm.Browse.SetImages([image]);
        vm.SelectedImage = image;
        vm.SwitchToDevelopCommand.Execute(null);
        await TestWaits.UntilAsync(() => vm.IsHistoryLoaded && vm.PreviewImage != null);
        var croppedPreview = vm.PreviewImage!;
        await vm.ToggleCropModeCommand.ExecuteAsync(null);
        var settings = image.EditSettings.Clone();
        settings.Crop = null;
        var expected = RenderGeometry.CalculateOriginalViewSize(1200, 900, settings);
        Assert.Equal(expected, vm.CropFrameSize);
        Assert.NotEqual(expected, croppedPreview.PixelSize);
        vm.ChooseCropRatio("3:2");
        Assert.Same(croppedPreview, vm.PreviewImage);
        Assert.Equal(1.5, CropGeometry.DraftPixelRatio(vm.CurrentCrop!, expected.Width / (double)expected.Height), 12);
        var apply = vm.ApplyCropCommand.ExecuteAsync(null);
        clock.Advance(TimeSpan.FromMilliseconds(200));
        await apply.WaitAsync(TestWaits.Condition);

        var result = await new ImageExportService(new RenderPipeline(), loader, new ExportMetadataService())
            .ExportBatchAsync([image], new ExportSettings { OutputFolder = _fixture.Path("output"), Format = ExportFormat.Png });
        var outcome = Assert.Single(result.Outcomes);
        Assert.True(outcome.Succeeded, outcome.FailureReason);
        using var exported = new MagickImage(outcome.ResolvedPath);
        Assert.InRange(Math.Abs(exported.Width - exported.Height * 1.5), 0, 2);
    }

    private sealed class RatioBaseLoader : IBaseImageLoader
    {
        public bool CanLoad(ImageFile file) => true;

        public BaseImage LoadPreviewBase(ImageFile file, BaseDecodeSettings decode, CancellationToken cancellationToken) =>
            Create(decode, 80, 60);

        public BaseImageLoadOutcome LoadPreviewBaseWithOutcome(ImageFile file, BaseDecodeSettings decode,
            CancellationToken cancellationToken) => BaseImageLoadOutcome.Loaded(Create(decode, 80, 60));

        public BaseImage LoadFullBase(ImageFile file, BaseDecodeSettings decode, CancellationToken cancellationToken) =>
            Create(decode, 1200, 900);

        private static BaseImage Create(BaseDecodeSettings decode, uint width, uint height) =>
            new(new MagickImage(MagickColors.Gray, width, height) { ColorSpace = ColorSpace.RGB },
                new BaseImageInfo(BaseSourceKind.Standard, false, decode, null, null, 6504, 0, false, null, 1, 1200, 900));
    }
}
