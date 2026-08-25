using Avalonia.Headless.XUnit;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class BeforeAfterOriginalSettingsTests : IDisposable
{
    private readonly CatalogVmFixture _fx = new("before-after-original");

    // The before view reverts tone and color only: the whole geometry family
    // survives, and it survives identically whether the original is entered by
    // toggling it on or by a workspace transition that reloads the preview
    // while the requested original intent still stands.
    [AvaloniaFact]
    public async Task OriginalKeepsTheGeometryFamilyOnBothEntryPaths()
    {
        using var catalog = await _fx.CreateCatalogAsync("geometry-family");
        var vm = _fx.CreateViewModel(
            catalog,
            new GrayRawLoader(),
            loadMetadataAsync: _ => Task.CompletedTask,
            availabilityService: new TestSourceAvailabilityService(
                SourceAvailability.AvailableLocally));
        vm.IsDevelopMode = true;
        // Every member outside the preserved set carries a sentinel, so the
        // hash below would move if the builder started preserving any of them.
        var image = new ImageFile(_fx.Path("geometry-family.dng"))
        {
            HasEdits = true,
            EditSettings = new EditSettings
            {
                Rotation = 90,
                HorizonRotation = 1.5,
                Crop = new CropRegion
                {
                    Left = 0.1,
                    Top = 0.2,
                    Right = 0.8,
                    Bottom = 0.9
                },
                Geometry = new GeometrySettings { Vertical = 12 },
                Lens = LensSettings.Legacy(),
                Exposure = 0.5,
                Brightness = 15,
                Contrast = 40,
                Saturation = -20,
                Vibrance = 25,
                Highlights = -30,
                Shadows = 35,
                BaseLook = true,
                HlReconstruction = HlReconstructionMode.Blend,
                Wb = new WhiteBalanceSettings
                {
                    Mode = WbMode.Custom,
                    Kelvin = 7200,
                    Tint = 12
                },
                Detail = new DetailSettings
                {
                    CaptureSharpen = 60,
                    LuminanceNr = 55,
                    ChromaNr = 40
                },
                Effects = new EffectsSettings
                {
                    Vignette = -35,
                    Midpoint = 70,
                    Grain = 20,
                    GrainSize = GrainSize.Coarse
                },
                Mixer = new ColorMixerSettings
                {
                    Aqua = new ColorMixerBandSettings
                    {
                        Hue = 10,
                        Saturation = -15,
                        Luminance = 20
                    }
                },
                CurveRed = Bent(),
                CurveGreen = Bent(),
                CurveBlue = Bent(),
                AppliedPresetId = "sentinel-preset"
            }
        };
        image.EditSettings.Curve.AddPointAndReturnIndex(0.5, 0.7);
        var expected = RenderSettingsHash.Compute(new EditSettings
        {
            Rotation = 90,
            HorizonRotation = 1.5,
            Crop = image.EditSettings.Crop!.Clone(),
            Geometry = image.EditSettings.Geometry!.Clone(),
            Lens = LensSettings.Legacy()
        });

        try
        {
            vm.SelectedImage = image;
            await TestWaits.UntilAsync(() => vm.PreviewImage != null);

            // The last sentinel, added once the edited render is done with it:
            // a profile in the initial settings would send that render off to
            // resolve a DCP that does not exist.
            image.EditSettings.RawProfile = new RawProfileSelection
            {
                Source = RawProfileSource.Embedded,
                ContentHash = "deadbeef"
            };

            await vm.ToggleBeforeAfterCommand.ExecuteAsync(null);
            await TestWaits.UntilAsync(() => vm.IsShowingOriginal);
            Assert.Equal(expected, PaintedIdentity(vm)!.SettingsHash);

            // Entering full screen reloads the preview without reserving a new
            // intent, so the reload must rebuild the very same original.
            var painted = vm.PreviewImage;
            vm.IsFullScreenMode = true;
            await TestWaits.UntilAsync(() =>
                vm.IsShowingOriginal &&
                !ReferenceEquals(vm.PreviewImage, painted));
            Assert.Equal(expected, PaintedIdentity(vm)!.SettingsHash);
        }
        finally
        {
            await vm.DisposeAsync();
        }
    }

    public void Dispose() => _fx.Dispose();

    private static CurveData Bent()
    {
        var curve = new CurveData();
        curve.AddPointAndReturnIndex(0.5, 0.7);
        return curve;
    }

    private static PreviewRenderIdentity? PaintedIdentity(
        MainWindowViewModel vm) =>
        vm.PreviewImage is { } preview
            ? vm.ImageService.Previews.TryGetPreviewRenderIdentity(preview)
            : null;

    private sealed class GrayRawLoader : IBaseImageLoader
    {
        public bool CanLoad(ImageFile file) => true;

        public BaseImage LoadPreviewBase(
            ImageFile file,
            BaseDecodeSettings decode,
            CancellationToken cancellationToken) =>
            new(
                new MagickImage(MagickColors.Gray, 64, 48),
                new BaseImageInfo(
                    BaseSourceKind.RawLibRaw,
                    true,
                    decode,
                    null,
                    null,
                    5500,
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
