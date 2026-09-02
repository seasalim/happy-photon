using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Xunit;

namespace HappyPhoton.Tests;

public sealed partial class LoupePeekTests
{
    [AvaloniaFact]
    public void LoupePeek_RendersShowcase()
    {
        var clock = new TestTimeProvider();
        using var bitmap = CreateBitmap(800, 600, structured: true);
        var viewer = CreateViewer(clock, bitmap, new object());
        viewer.OriginalViewPixelSize = new PixelSize(1600, 1200);
        var window = new Window { Content = viewer };

        ShowcaseTestHelper.Capture(
            "loupe-peek",
            window,
            new PixelSize(700, 500),
            ThemeVariant.Dark,
            stagedWindow =>
            {
                Engage(stagedWindow, clock, Center(viewer, stagedWindow));
                Assert.True(viewer.IsLoupePeekActive);
                Assert.True(viewer.FindControl<TextBlock>("LoupeStatus")!.IsVisible);
            });
    }
}
