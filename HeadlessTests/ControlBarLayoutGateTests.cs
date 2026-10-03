using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class ControlBarLayoutGateTests(ITestOutputHelper output)
{
    [AvaloniaTheory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task ClustersShareBarCentre(int run)
    {
        await ControlBarGateScene.WithScene(async (window, vm) =>
        {
            using var theme = new TestUiScope(theme: ThemeVariant.Dark);

            foreach (var width in new[] { 1400, 1000 })
            {
                var lefts = new List<double>();

                foreach (var view in new[] { "browse-grid", "browse-loupe", "develop" })
                {
                    ControlBarGateScene.Mode(window, vm, view);
                    var bar = ControlBarGateScene.Bar(window, view == "develop");
                    ControlBarGateScene.ViewerWidth(window, bar, width);
                    var full = ControlBarGateScene.Full(bar);
                    Assert.True(full.IsEffectivelyVisible);
                    var bounds = ControlBarGateScene.Bounds(full, bar);
                    lefts.Add(bounds.Left);
                    Assert.InRange(ControlBarGateScene.Offset(full, bar), 0, 1);

                    if (view == "develop")
                    {
                        Assert.Equal(width == 1400, ControlBarGateScene.Named<CompactSlider>(bar, "DevelopZoomSlider").IsVisible);
                    }

                    output.WriteLine(FormattableString.Invariant(
                        $"WP12 G1 {view} {width} centreOffset={ControlBarGateScene.Offset(full, bar):F3} left={bounds.Left:F3}"));
                    output.WriteLine($"WP12 G1 run={run} clusterWidth={bounds.Width:F3} barWidth={bar.Bounds.Width:F3}");
                }

                Assert.InRange(lefts.Max() - lefts.Min(), 0, 1);
                output.WriteLine($"WP12 G1 {width} leftSpread={lefts.Max() - lefts.Min():F3} run={run}");
            }

            await Task.CompletedTask;
        });
    }

    [AvaloniaTheory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task BarHeightsStayAtBaseline(int run)
    {
        await ControlBarGateScene.WithScene(async (window, vm) =>
        {
            foreach (var gray in new[] { false, true })
            {
                using var theme = new TestUiScope(theme: gray ? HappyPhotonThemes.MidGray : ThemeVariant.Dark);

                foreach (var width in new[] { 1400, 800 })
                foreach (var develop in new[] { false, true })
                {
                    ControlBarGateScene.Mode(window, vm, develop ? "develop" : "browse-grid");
                    var bar = ControlBarGateScene.Bar(window, develop);
                    ControlBarGateScene.ViewerWidth(window, bar, width);
                    output.WriteLine(FormattableString.Invariant(
                        $"WP12 G3 {(develop ? "Develop" : "Browse")} {width} {(gray ? "MiddleGray" : "Dark")} height={bar.Bounds.Height:F3}"));
                    Assert.Equal(develop ? 33 : 29, bar.Bounds.Height);
                    output.WriteLine($"WP12 G3 run={run}");
                }
            }

            await Task.CompletedTask;
        });
    }

    [AvaloniaFact]
    public async Task CaptureAfterViews()
    {
        await ControlBarGateScene.WithScene(async (window, vm) =>
        {
            using var theme = new TestUiScope(theme: ThemeVariant.Dark);

            foreach (var width in new[] { 1400, 1000, 800 })
            foreach (var view in new[] { "browse-grid", "browse-loupe", "develop" })
            {
                ControlBarGateScene.Mode(window, vm, view);
                var bar = ControlBarGateScene.Bar(window, view == "develop");
                ControlBarGateScene.ViewerWidth(window, bar, width);
                using var frame = window.CaptureRenderedFrame();
                Assert.NotNull(frame);
                Assert.Equal((int)window.Bounds.Width, frame.PixelSize.Width);
                Assert.Equal((int)window.Bounds.Height, frame.PixelSize.Height);
                // Generated capture output requested by WP12; no source or run artifacts are edited.
                var directory = Path.Combine(GoldenTestPaths.RepositoryRoot, "artifacts", "shots");
                Directory.CreateDirectory(directory);
                frame.Save(Path.Combine(directory, $"wp12-{view}-{width}-after.png"));
                output.WriteLine($"WP12 capture {view} viewer={bar.Bounds.Width:F3} window={window.Bounds.Width:F3}x{window.Bounds.Height:F3}");
            }

            await Task.CompletedTask;
        });
    }
}
