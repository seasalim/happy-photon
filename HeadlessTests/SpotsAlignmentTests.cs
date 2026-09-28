using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using HappyPhoton.Views;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class SpotsAlignmentTests
{
    [AvaloniaTheory]
    [InlineData(false, 0, 0)]
    [InlineData(true, 0, 0)]
    [InlineData(false, 90, 0)]
    [InlineData(true, 90, 0)]
    [InlineData(false, 90, 7)]
    [InlineData(true, 90, 7)]
    public async Task RenderedRepairAndOverlayAlignAcrossBaseSwaps(bool actualSize, int rotation, double horizon)
    {
        using var fixture = new CatalogVmFixture("spots-alignment");
        using var catalog = await fixture.CreateCatalogAsync();
        var loader = new SpotPairLoader();
        await using var vm = fixture.CreateViewModel(catalog, loader, _ => Task.CompletedTask,
            new TestSourceAvailabilityService(SourceAvailability.AvailableLocally), timeProvider: new TestTimeProvider());
        var spot = new Repair { Type = "clone", U = 7045d / 16384, V = 6717d / 16384, Su = .75, Sv = 9830d / 16384, Radius = .012, Feather = 0 };
        var settings = new EditSettings
        {
            Rotation = rotation, HorizonRotation = horizon,
            Crop = new CropRegion { Left = .107, Top = .143, Right = .907, Bottom = .891 },
            Geometry = new GeometrySettings { Vertical = 12, Horizontal = -8, Distortion = 15 },
            Repairs = [spot]
        };
        var image = new ImageFile(fixture.Path("synthetic.jpg")) { EditSettings = settings };
        image.CatalogId = await catalog.GetOrCreateImageAsync(image.FilePath);
        await catalog.SaveEditSettingsAsync(image.CatalogId, settings);
        vm.IsDevelopMode = true;
        vm.Browse.SetImages([image]);
        vm.SelectedImage = image;
        await TestWaits.UntilAsync(() => vm.IsHistoryLoaded && vm.PreviewImage != null);
        await vm.ToggleSpotsModeCommand.ExecuteAsync(null);
        vm.SelectedSpot = vm.Spots[0];
        var saved = vm.SelectedSpot! with { };
        var originalSize = RenderGeometry.CalculateOriginalViewSize(6000, 4000, settings);
        var viewer = new ZoomPanControl { DataContext = vm, Source = vm.PreviewImage, IsSpotsMode = true,
            OriginalViewPixelSize = originalSize, AutoFit = !actualSize, ZoomLevel = actualSize ? 1 : .1 };
        viewer.AutoFitRequested += (_, zoom) => viewer.ZoomLevel = zoom;
        using var scope = new TestUiScope(new Window { Width = 700, Height = 500, Content = viewer });
        var window = scope.Window!;
        window.SetRenderScaling(1.5);
        Dispatcher.UIThread.RunJobs();
        var overlay = viewer.GetVisualDescendants().OfType<SpotsOverlayControl>().Single();
        Assert.True(overlay.IsVisible);
        VerifyPixels();
        var identity = vm.ImageService.Previews.TryGetPreviewRenderIdentity(vm.PreviewImage!)!;
        using var resting = await vm.ImageService.Previews.RenderRestingPreviewAsync(image, settings, 1800,
            identity, CancellationToken.None);
        Assert.NotNull(resting);
        Assert.True(vm.BeginSpotsGesture(SpotHandle.Destination, new(saved.U, saved.V)));
        vm.ReplacePreviewImage(resting.DetachBitmap(), PreviewPaintSource.RestingRender);
        viewer.Source = vm.PreviewImage;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(2400, vm.SpotDisplayMap!.BaseWidth);
        Assert.True(vm.IsSpotsGestureActive);
        VerifyPixels();
        var projected = vm.SpotDisplayMap.ToDisplay(new(saved.U, saved.V));
        var rebased = vm.SpotDisplayMap.ToBase(projected);
        vm.MoveSpotsGesture(rebased, 0);
        Assert.Equal(saved, vm.SelectedSpot);
        vm.DiscardSpotsGesture();
        Assert.Equal(saved, vm.Spots[0]);
        Assert.Equal(1, loader.LoadCount);

        void VerifyPixels()
        {
            var bitmap = vm.PreviewImage!;
            using var stream = new MemoryStream();
            bitmap.Save(stream);
            stream.Position = 0;
            using var pixels = new MagickImage(stream);
            using var data = pixels.GetPixels();
            var center = vm.SpotDisplayMap!.ToDisplay(new(saved.U, saved.V));
            var cx = center.X * bitmap.PixelSize.Width;
            var cy = center.Y * bitmap.PixelSize.Height;
            double sum = 0, sumX = 0, sumY = 0, sumXX = 0, sumYY = 0;
            var radius = bitmap.PixelSize.Width * .04;
            for (var y = Math.Max(0, (int)(cy - radius)); y < Math.Min(bitmap.PixelSize.Height, cy + radius); y++)
            for (var x = Math.Max(0, (int)(cx - radius)); x < Math.Min(bitmap.PixelSize.Width, cx + radius); x++)
            {
                var encoded = data.GetPixel(x, y)[0] / 65535d;
                // White over black measures coverage in linear light, not in encoded sRGB.
                var weight = encoded <= .04045 ? encoded / 12.92 : Math.Pow((encoded + .055) / 1.055, 2.4);
                sum += weight;
                sumX += (x + .5) * weight;
                sumY += (y + .5) * weight;
                sumXX += (x + .5) * (x + .5) * weight;
                sumYY += (y + .5) * (y + .5) * weight;
            }
            Assert.True(sum > 10);
            var rendered = new Point(sumX / sum / bitmap.PixelSize.Width * overlay.Bounds.Width,
                sumY / sum / bitmap.PixelSize.Height * overlay.Bounds.Height);
            var drawn = overlay.ToCanvas(new(saved.U, saved.V));
            var error = ((Vector)(rendered - drawn)).Length * window.RenderScaling;
            Assert.True(error <= 1, $"{bitmap.PixelSize} actualSize={actualSize} rotation={rotation} horizon={horizon}: {error:F4} device px");
            // A filled disc has variance r²/4; compare both projected extents as well as its center.
            var radiusPixels = RepairGeometry.EffectiveRadius(saved.Radius, vm.SpotDisplayMap.BaseWidth, vm.SpotDisplayMap.BaseHeight);
            var perimeter = Enumerable.Range(0, 64).Select(i => overlay.ToCanvas(new Point(
                saved.U + Math.Cos(i * Math.Tau / 64) * radiusPixels / vm.SpotDisplayMap.BaseWidth,
                saved.V + Math.Sin(i * Math.Tau / 64) * radiusPixels / vm.SpotDisplayMap.BaseHeight))).ToArray();
            var rx = 2 * Math.Sqrt(sumXX / sum - Math.Pow(sumX / sum, 2)) / bitmap.PixelSize.Width * overlay.Bounds.Width;
            var ry = 2 * Math.Sqrt(sumYY / sum - Math.Pow(sumY / sum, 2)) / bitmap.PixelSize.Height * overlay.Bounds.Height;
            Assert.InRange(Math.Abs(rx - (perimeter.Max(p => p.X) - perimeter.Min(p => p.X)) / 2) * window.RenderScaling, 0, 1);
            Assert.InRange(Math.Abs(ry - (perimeter.Max(p => p.Y) - perimeter.Min(p => p.Y)) / 2) * window.RenderScaling, 0, 1);
            if (actualSize)
                Assert.InRange(Math.Max(overlay.Bounds.Width, overlay.Bounds.Height) * window.RenderScaling,
                    Math.Max(originalSize.Width, originalSize.Height) - 1, Math.Max(originalSize.Width, originalSize.Height) + 1);
        }
    }

    private sealed class SpotPairLoader : IBaseImageLoader
    {
        public int LoadCount { get; private set; }

        public bool CanLoad(ImageFile image) => true;

        public BaseImageLoadOutcome LoadPreviewBaseWithOutcome(ImageFile image, BaseDecodeSettings decode, CancellationToken cancellationToken)
        {
            LoadCount++;
            return BaseImageLoadOutcome.Loaded(new PreviewBasePair(Create(1200, 800, decode), Create(2400, 1600, decode)));
        }

        public BaseImage LoadFullBase(ImageFile image, BaseDecodeSettings decode, CancellationToken cancellationToken) => throw new NotSupportedException();

        private static BaseImage Create(int width, int height, BaseDecodeSettings decode)
        {
            var pixels = new MagickImage(MagickColors.Black, (uint)width, (uint)height) { ColorSpace = ColorSpace.RGB };
            using var patch = new MagickImage(MagickColors.White, (uint)(width * .12), (uint)(height * .16));
            pixels.Composite(patch, (int)(width * .69), (int)(height * .52), CompositeOperator.Copy);
            return new BaseImage(pixels, new BaseImageInfo(BaseSourceKind.Standard, false, decode, null, null,
                6504, 0, false, null, 1, 6000, 4000));
        }
    }
}
