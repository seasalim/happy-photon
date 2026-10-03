using Avalonia.Headless.XUnit;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

public sealed partial class AutoStraightenTests : IDisposable
{
    private readonly CatalogVmFixture _fixture = new("auto-straighten");

    [AvaloniaTheory]
    [InlineData(false, 2)]
    [InlineData(true, 2)]
    [InlineData(false, 5)]
    [InlineData(false, -5)]
    public async Task AutoSetsOnlyRoundedClampedDraftAndCancelRestores(bool geometry, double tilt)
    {
        using var catalog = await _fixture.CreateCatalogAsync();
        var loader = new TiltedLoader(tilt);
        await using var vm = CreateViewModel(catalog, loader);
        var settings = new EditSettings
        {
            Rotation = 90, HorizonRotation = .5,
            Crop = new CropRegion { Left = .1, Right = .9 },
            Geometry = geometry ? new GeometrySettings { Aspect = 100, Vertical = 20 } : null
        };
        var image = await OpenCropAsync(vm, catalog, settings);
        var before = image.EditSettings.Clone();
        var crop = vm.CurrentCrop;
        var preview = vm.PreviewImage;
        var expected = loader.Expected;
        Assert.InRange(Math.Abs(expected + tilt), 0, .05);

        await vm.AutoStraightenCommand.ExecuteAsync(null);
        await SettleAsync(vm);

        Assert.Equal(expected, vm.HorizonRotation);
        Assert.InRange(vm.HorizonRotation, -5, 5);
        Assert.Same(crop, vm.CurrentCrop);
        Assert.Equal(90, vm.Rotation);
        Assert.Equal(geometry ? 100 : 0, vm.GeometryAspect);
        Assert.True(before.HasSameEdits(image.EditSettings));
        Assert.Empty(vm.HistoryEntries);
        Assert.NotSame(preview, vm.PreviewImage);

        await vm.CancelCropCommand.ExecuteAsync(null);

        Assert.Equal(.5, vm.HorizonRotation);
        Assert.Equal(.1, vm.CurrentCrop!.Left);
        Assert.True(before.HasSameEdits(image.EditSettings));
        Assert.Empty(vm.HistoryEntries);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ApplyCommitsOneHistoryStep(bool moveCrop)
    {
        using var catalog = await _fixture.CreateCatalogAsync();
        var loader = new TiltedLoader();
        await using var vm = CreateViewModel(catalog, loader);
        var image = await OpenCropAsync(vm, catalog);
        await vm.AutoStraightenCommand.ExecuteAsync(null);

        if (moveCrop)
        {
            vm.CurrentCrop = new CropRegion { Left = .2, Right = .8 };
        }

        await vm.ApplyCropCommand.ExecuteAsync(null);
        await SettleAsync(vm);

        Assert.False(vm.IsCropMode);
        Assert.Equal(loader.Expected, image.EditSettings.HorizonRotation);
        Assert.Equal(2, vm.HistoryEntries.Count);
        Assert.Equal(moveCrop ? "Crop" : "Horizon -2.00° (-2.00°)",
            vm.HistoryEntries[0].Label);
        var persisted = (await catalog.LoadImageStatesAsync([image.FilePath]))[image.FilePath].Single();
        Assert.Equal(loader.Expected, persisted.EditSettings.HorizonRotation);
    }

    [AvaloniaFact]
    public async Task FailedApplyKeepsAutoDraftAndMidModeCommit()
    {
        using var catalog = await _fixture.CreateCatalogAsync();
        var loader = new TiltedLoader();
        await using var vm = CreateViewModel(catalog, loader);
        var image = await OpenCropAsync(vm, catalog, new EditSettings
        {
            Rotation = 90, HorizonRotation = .5, Crop = new CropRegion { Left = .1, Right = .9 }
        });
        vm.Exposure = .3;
        await SettleAsync(vm);
        await vm.AutoStraightenCommand.ExecuteAsync(null);
        await SettleAsync(vm);
        var draft = new CropRegion { Left = .25, Right = .75 };
        vm.CurrentCrop = draft;
        var history = vm.HistoryEntries.ToArray();
        vm.ImageService.Previews.RenderGateAsync = () =>
            Task.FromException(new InvalidOperationException("render failed"));

        await vm.ApplyCropCommand.ExecuteAsync(null);

        Assert.True(vm.IsCropMode);
        Assert.Equal(loader.Expected, vm.HorizonRotation);
        Assert.Same(draft, vm.CurrentCrop);
        Assert.Equal(history, vm.HistoryEntries.ToArray());
        Assert.Equal(90, vm.Rotation);
        Assert.Equal(.5, image.EditSettings.HorizonRotation);
        Assert.Equal(.1, image.EditSettings.Crop!.Left);
        var persisted = (await catalog.LoadImageStatesAsync([image.FilePath]))[image.FilePath].Single();
        Assert.Equal(.3, persisted.EditSettings.Exposure);
        Assert.Equal(.5, persisted.EditSettings.HorizonRotation);
        Assert.Equal(.1, persisted.EditSettings.Crop!.Left);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StructurelessSetsDraftAndEqualResultShowsAlreadyLevel(bool featureless)
    {
        using var catalog = await _fixture.CreateCatalogAsync();
        var loader = new TiltedLoader(featureless: featureless);
        await using var vm = CreateViewModel(catalog, loader);
        await OpenCropAsync(vm, catalog);
        vm.HorizonRotation = featureless ? .75 : loader.Expected;
        var before = vm.HorizonRotation;

        await vm.AutoStraightenCommand.ExecuteAsync(null);

        if (featureless)
        {
            Assert.Equal(0, vm.HorizonRotation);
            Assert.Null(vm.TransientStatus);
        }
        else
        {
            Assert.Equal(before, vm.HorizonRotation);
            Assert.Equal("Already level", vm.StatusMessage);
            await TestWaits.UntilAsync(() => vm.StatusMessage != "Already level");
        }

        Assert.Empty(vm.HistoryEntries);
    }

    private MainWindowViewModel CreateViewModel(CatalogService catalog, TiltedLoader loader,
        TimeProvider? clock = null) => _fixture.CreateViewModel(catalog, loader,
            _ => Task.CompletedTask, availabilityService: new TestSourceAvailabilityService(
                SourceAvailability.AvailableLocally), timeProvider: clock);

    private async Task<ImageFile> OpenCropAsync(MainWindowViewModel vm, CatalogService catalog,
        EditSettings? settings = null)
    {
        vm.ShowWorkspaceReady(MainWindowViewModel.CurrentFirstRunExperienceVersion);
        vm.IsDevelopMode = true;
        var image = new ImageFile(_fixture.Path("tilted.jpg")) { EditSettings = settings ?? new() };
        image.CatalogId = await catalog.GetOrCreateImageAsync(image.FilePath);
        await catalog.SaveEditSettingsAsync(image.CatalogId, image.EditSettings);
        vm.Browse.SetImages([image]);
        vm.SelectedImage = image;
        await TestWaits.UntilAsync(() => vm.IsHistoryLoaded && vm.PreviewImage != null);
        await vm.ToggleCropModeCommand.ExecuteAsync(null);

        return image;
    }

    private static async Task SettleAsync(MainWindowViewModel vm)
    {
        if (vm.PendingPreviewDebounceTask is { } pending) await pending.WaitAsync(TestWaits.Condition);
        if (vm.PendingHistoryCommitTask is { } history) await history.WaitAsync(TestWaits.Condition);
    }

    public void Dispose() => _fixture.Dispose();

    private sealed class TiltedLoader(double tilt = 2, bool featureless = false) : IBaseImageLoader
    {
        public double Expected { get; private set; }

        public Func<CancellationToken, BaseImageLoadOutcome>? PreviewLoadOverride { get; set; }

        public bool CanLoad(ImageFile file) => true;

        public BaseImage LoadPreviewBase(ImageFile file, BaseDecodeSettings decode,
            CancellationToken cancellationToken)
        {
            const int width = 768;
            const int height = 512;
            var samples = new ushort[width * height * 3];
            var slope = Math.Tan(tilt * Math.PI / 180);

            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var edge = height / 2d + slope * (x - width / 2d);
                    var light = featureless ? .4 : .15 + .55 * Math.Clamp(edge - y + .5, 0, 1);
                    var value = (ushort)Math.Round(light * ushort.MaxValue);
                    var index = (y * width + x) * 3;
                    samples[index] = value;
                    samples[index + 1] = value;
                    samples[index + 2] = value;
                }
            }

            var pixels = RawBaseLoader.ImportRgb16(
                System.Runtime.InteropServices.MemoryMarshal.AsBytes(samples.AsSpan()), width, height);

            if (!featureless)
            {
                var result = HorizonDetection.Detect(pixels);
                Expected = Math.Clamp(Math.Round(result.HorizonRotation, 2), -5, 5);
            }

            return new BaseImage(pixels, new BaseImageInfo(BaseSourceKind.Standard,
                false, decode, null, null, 6504, 0, false, null, 1, width, height));
        }

        public BaseImageLoadOutcome LoadPreviewBaseWithOutcome(ImageFile file,
            BaseDecodeSettings decode, CancellationToken cancellationToken) =>
            PreviewLoadOverride?.Invoke(cancellationToken) ??
                BaseImageLoadOutcome.Loaded(LoadPreviewBase(file, decode, cancellationToken));

        public BaseImage? LoadFullBase(ImageFile file, BaseDecodeSettings decode,
            CancellationToken cancellationToken) => throw new InvalidOperationException("No full decode permitted");
    }
}
