using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Xunit;

namespace HappyPhoton.Tests;

public sealed partial class LoupePeekTests
{
    [AvaloniaFact]
    public void LoupePeek_GeneratesMockupReviewScreenshot()
    {
        Assert.SkipWhen(
            Environment.GetEnvironmentVariable("HAPPY_PHOTON_LOUPE_LOOKGATE") != "1",
            "Set HAPPY_PHOTON_LOUPE_LOOKGATE=1 and " +
            "HAPPY_PHOTON_LOUPE_LOOKGATE_DIR to generate the screenshot.");
        var outputDirectory = Environment.GetEnvironmentVariable(
            "HAPPY_PHOTON_LOUPE_LOOKGATE_DIR");
        Assert.False(string.IsNullOrWhiteSpace(outputDirectory));
        outputDirectory = Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(outputDirectory);
        var clock = new TestTimeProvider();
        using var bitmap = CreateBitmap(800, 600, structured: true);
        var viewer = CreateViewer(clock, bitmap, new object());
        viewer.OriginalViewPixelSize = new PixelSize(1600, 1200);
        var window = Show(viewer, 700, 500);
        try
        {
            Engage(window, clock, Center(viewer, window));
            Assert.True(viewer.IsLoupePeekActive);
            Assert.True(viewer.FindControl<TextBlock>("LoupeStatus")!.IsVisible);
            Assert.Equal(1600, Image(viewer).Bounds.Width, precision: 8);
            Assert.Equal(1200, Image(viewer).Bounds.Height, precision: 8);
            using var frame = window.CaptureRenderedFrame() ??
                throw new InvalidOperationException("Loupe screenshot was empty.");
            frame.Save(Path.Combine(outputDirectory, "loupe-peek.png"));
        }
        finally
        {
            window.Close();
        }
    }
}
