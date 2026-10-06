using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using HappyPhoton.Views;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

public sealed partial class BrandMarkTests
{
    private static readonly PixelSize CaptureSize = new(1200, 700);

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CaptureWelcome_HeadingInkIsTheBrandColour(bool midgray)
    {
        await using var scene = new WelcomeScene();
        var name = $"brand-welcome-{(midgray ? "midgray" : "dark")}";
        Rect heading = default;
        Color card = default;

        ShowcaseTestHelper.Capture(name, scene.Window, CaptureSize,
            midgray ? HappyPhotonThemes.MidGray : ThemeVariant.Dark, window =>
            {
                var text = scene.Heading();
                heading = new Rect(text.TranslatePoint(default, window)!.Value, text.Bounds.Size);
                card = Assert.IsAssignableFrom<ISolidColorBrush>(text.GetLogicalAncestors().OfType<Border>()
                    .First(border => border.Background != null).Background).Color;
            });

        using var shot = Shot(name);
        var pixels = Rgba(shot);
        var ink = Color.FromRgb(0, 0, 0);
        var farthest = -1.0;

        for (var y = (int)heading.Top; y < (int)heading.Bottom; y++)
        {
            for (var x = (int)heading.Left; x < (int)heading.Right; x++)
            {
                var pixel = Pixel(pixels, (int)shot.Width, x, y);
                var distance = DeltaE(pixel, card);

                if (distance > farthest)
                {
                    farthest = distance;
                    ink = pixel;
                }
            }
        }

        var deltaE = DeltaE(ink, Color.Parse(BrandHex));
        Assert.True(deltaE <= 3.0, $"{name}: the heading's ink {ink} is ΔE {deltaE:F2} from {BrandHex}.");
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CaptureTitleBar_MarkCentreIsTheNeutralDisc(bool midgray)
    {
        await using var scene = new TipsTestScene(CaptureSize.Width, CaptureSize.Height);
        var name = $"brand-titlebar-{(midgray ? "midgray" : "dark")}";
        scene.Capture(name, midgray ? HappyPhotonThemes.MidGray : ThemeVariant.Dark);

        var mark = scene.Window.GetLogicalDescendants().OfType<HappyPhotonTitleBar>().Single()
            .FindControl<Border>("BrandMark")!;
        Assert.Equal(new Size(20, 20), mark.Bounds.Size);

        var centre = mark.TranslatePoint(new Point(10, 10), scene.Window)!.Value;

        using var shot = Shot(name);
        var pixel = Pixel(Rgba(shot), (int)shot.Width, (int)centre.X, (int)centre.Y);
        var disc = midgray ? MidGrayMonoDiscHex : DarkMonoDiscHex;
        var deltaE = DeltaE(pixel, Color.Parse(disc));

        Assert.True(pixel.R == pixel.G && pixel.G == pixel.B, $"{name}: the 20 px mark's centre {pixel} carries colour.");
        Assert.True(deltaE <= 3.0, $"{name}: the 20 px mark's centre {pixel} is ΔE {deltaE:F2} from {disc}.");
    }

    private static MagickImage Shot(string name)
    {
        var path = Path.Combine(GoldenTestPaths.RepositoryRoot, "artifacts", "shots", $"{name}.png");
        var shot = new MagickImage(path);

        Assert.Equal((uint)CaptureSize.Width, shot.Width);
        Assert.Equal((uint)CaptureSize.Height, shot.Height);

        return shot;
    }

    private sealed class WelcomeScene : IAsyncDisposable
    {
        private readonly TemporaryDirectory _root = new();

        private readonly CatalogService _catalog;

        private readonly MainWindowViewModel _vm;

        public Window Window { get; }

        public WelcomeScene()
        {
            _catalog = new CatalogService(Path.Combine(_root.Path, "catalog"));
            _vm = new MainWindowViewModel(_catalog);
            _vm.ShowFirstRunWelcome("C:\\Pictures");
            _vm.FirstRunStep = FirstRunStep.Welcome;

            Window = new Window { Content = new FirstRunView { DataContext = _vm } };
        }

        public TextBlock Heading()
        {
            Dispatcher.UIThread.RunJobs();

            return Window.GetLogicalDescendants().OfType<TextBlock>()
                .Single(text => text.Text == "Welcome to Happy Photon");
        }

        public async ValueTask DisposeAsync()
        {
            Window.Close();
            await _vm.DisposeAsync();
            _catalog.Dispose();
            _root.Dispose();
        }
    }
}
