using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class PlaceholderImageTests
{
    [AvaloniaTheory]
    [InlineData(256, 256, 256, 256)]
    [InlineData(0, 0, 600, 600)]
    [InlineData(6000, 4000, 800, 533.3333)]
    public void Thumbnail_NeverDrawsLargerThanTheOriginalAtOneToOne(
        int originalWidth,
        int originalHeight,
        double expectedWidth,
        double expectedHeight)
    {
        using var thumbnail = new WriteableBitmap(
            new PixelSize(96, originalHeight == 4000 ? 64 : 96),
            new Vector(96, 96),
            PixelFormat.Bgra8888,
            AlphaFormat.Opaque);
        var placeholder = new PlaceholderImage
        {
            Source = thumbnail,
            OriginalPixelWidth = originalWidth,
            OriginalPixelHeight = originalHeight
        };
        var host = new Panel { Width = 800, Height = 600, Children = { placeholder } };
        var window = new Window { Width = 800, Height = 600, Content = host };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        try
        {
            Assert.Equal(expectedWidth, placeholder.Bounds.Width, precision: 3);
            Assert.Equal(expectedHeight, placeholder.Bounds.Height, precision: 3);
            Assert.InRange(Math.Abs((800 - expectedWidth) / 2 - placeholder.Bounds.X), 0, 1);
            Assert.InRange(Math.Abs((600 - expectedHeight) / 2 - placeholder.Bounds.Y), 0, 1);
        }
        finally
        {
            window.Close();
        }
    }
}
