using Avalonia;
using Avalonia.Controls;
using Ellipse = Avalonia.Controls.Shapes.Ellipse;
using ShapePath = Avalonia.Controls.Shapes.Path;
using Avalonia.Headless.XUnit;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using HappyPhoton.Views;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class RawHistogramUiTests : IDisposable
{
    private readonly string _root = Directory.CreateDirectory(Path.Combine(
        Path.GetTempPath(), $"happy-photon-rawhist-ui-{Guid.NewGuid():N}"))
        .FullName;

    [AvaloniaFact]
    public async Task PreferenceFallsBackForStandardLibraryAndCloudSources()
    {
        using var catalog = new CatalogService(Path.Combine(_root, "catalog"));
        await catalog.InitializeAsync();
        var vm = new MainWindowViewModel(
            catalog,
            new DomainLoader(),
            loadMetadataAsync: _ => Task.CompletedTask,
            availabilityService: new TestSourceAvailabilityService(
                SourceAvailability.AvailableLocally))
        {
            IsDevelopMode = true
        };
        vm.SelectedImage = new ImageFile(Path.Combine(_root, "sensor.dng"));
        await TestWaits.UntilAsync(() => vm.IsRawHistogramAvailable);

        vm.ToggleRawHistogramCommand.Execute(null);
        Assert.True(vm.IsRawHistogramPreferred);
        Assert.True(vm.IsRawHistogramEffective);
        Assert.Equal("RAW HISTOGRAM", vm.HistogramTitle);

        vm.SelectedImage = new ImageFile(Path.Combine(_root, "display.jpg"));
        await TestWaits.UntilAsync(() => !vm.IsRawHistogramAvailable);
        Assert.True(vm.IsRawHistogramPreferred);
        Assert.False(vm.IsRawHistogramEffective);
        Assert.Equal("HISTOGRAM", vm.HistogramTitle);
        Assert.Contains("Display-referred", vm.RawHistogramHint);

        vm.IsDevelopMode = false;
        Assert.False(vm.IsRawHistogramAvailable);
        Assert.Contains("Develop", vm.RawHistogramHint);

        vm.IsDevelopMode = true;
        var cloud = new ImageFile(Path.Combine(_root, "cloud.dng"));
        vm.SelectedImage = cloud;
        cloud.SourceRequiresHydration = true;
        Assert.False(vm.IsRawHistogramAvailable);
        Assert.Contains("online-only", vm.RawHistogramHint);
        Assert.True(vm.IsRawHistogramPreferred);

        await vm.DisposeAsync();
    }

    [AvaloniaFact]
    public void RawPresentation_HasNoLuminanceAndUsesSixteenDotBoundary()
    {
        var view = new HistogramView();
        var window = Show(view);
        view.Histogram = RawHistogram(15);
        var canvas = view.FindControl<Canvas>("HistogramCanvas")!;
        var panel = view.FindControl<StackPanel>("RawClippingPanel")!;
        var dot = view.FindControl<Ellipse>("RawRedClippingDot")!;
        var text = view.FindControl<TextBlock>("RawRedClippingText")!;

        Assert.Equal(3, canvas.Children.OfType<ShapePath>().Count());
        Assert.True(panel.IsVisible);
        Assert.Equal(0.25, dot.Opacity);
        Assert.Equal("15.00%", text.Text);

        view.Histogram = RawHistogram(16);
        Assert.Equal(1, dot.Opacity);
        Assert.Equal("16.00%", text.Text);
        window.Close();
    }

    [AvaloniaFact]
    public void LitChannelWithTinyFraction_ShowsSubThresholdInsteadOfZero()
    {
        var view = new HistogramView();
        var window = Show(view);
        var histogram = new HistogramData
        {
            Domain = HistogramDomain.RawSensor,
            // 16 lit photosites out of a million rounds to 0.00% — must read <0.01%.
            Clipping = new RawClipping(16, 0, 0, 1_000_000, 4095)
        };
        histogram.Red[128] = 1;
        histogram.Normalize();

        view.Histogram = histogram;
        var dot = view.FindControl<Ellipse>("RawRedClippingDot")!;
        var text = view.FindControl<TextBlock>("RawRedClippingText")!;

        Assert.Equal(1, dot.Opacity);
        Assert.Equal("<0.01%", text.Text);
        window.Close();
    }

    [AvaloniaFact]
    public void DisplayPresentation_KeepsLuminanceAndNeverShowsRawDots()
    {
        var view = new HistogramView();
        var window = Show(view);
        var display = new HistogramData();
        display.Red[128] = display.Green[128] = display.Blue[128] = 1;
        display.Luminance[128] = 1;
        display.Clipping = new RawClipping(100, 100, 100, 100, 4095);
        display.Normalize();

        view.Histogram = display;

        Assert.Equal(4, view.FindControl<Canvas>("HistogramCanvas")!
            .Children.OfType<ShapePath>().Count());
        Assert.False(view.FindControl<StackPanel>("RawClippingPanel")!.IsVisible);
        window.Close();
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private static Window Show(HistogramView view)
    {
        var window = new Window
        {
            Width = 250,
            Height = 140,
            Content = view
        };
        window.Show();
        return window;
    }

    private static HistogramData RawHistogram(long redClipped)
    {
        var histogram = new HistogramData
        {
            Domain = HistogramDomain.RawSensor,
            Clipping = new RawClipping(redClipped, 0, 0, 100, 4095)
        };
        histogram.Red[128] = histogram.Green[128] = histogram.Blue[128] = 1;
        histogram.Normalize();
        return histogram;
    }

    private sealed class DomainLoader : IBaseImageLoader
    {
        public bool CanLoad(ImageFile file) => true;

        public BaseImage? LoadPreviewBase(ImageFile file, BaseDecodeSettings decode,
            CancellationToken cancellationToken)
        {
            HistogramData? raw = null;
            if (file.IsRaw)
            {
                raw = RawHistogram(16);
            }
            return new BaseImage(
                new MagickImage(MagickColors.Gray, 32, 24),
                new BaseImageInfo(
                    file.IsRaw ? BaseSourceKind.RawLibRaw : BaseSourceKind.Standard,
                    file.IsRaw, decode, null, null, 5500, 0, false, null,
                    1, 32, 24, RawHistogram: raw));
        }

        BaseImageLoadOutcome IBaseImageLoader.LoadPreviewBaseWithOutcome(
            ImageFile file, BaseDecodeSettings decode,
            CancellationToken cancellationToken) =>
            BaseImageLoadOutcome.FromImage(
                LoadPreviewBase(file, decode, cancellationToken),
                BaseImageLoadFailure.DecodeFailed);

        public BaseImage? LoadFullBase(ImageFile file, BaseDecodeSettings decode,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
