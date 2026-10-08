using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using HappyPhoton.Models;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class WaveformViewTests
{
    private readonly ITestOutputHelper _output;

    public WaveformViewTests(ITestOutputHelper output) => _output = output;

    [AvaloniaFact]
    public void Bitmap_IsReusedRepaintedForThemeAndDisposedOnDetach()
    {
        using var themeScope = new TestUiScope(
            theme: Avalonia.Styling.ThemeVariant.Dark);
        var view = new WaveformView { Waveform = FilledWaveform(level: 64) };
        var window = new Window { Width = 256, Height = 80, Content = view };
        using var windowScope = new TestUiScope(window);
        Dispatcher.UIThread.RunJobs();
        var bitmap = Assert.IsType<Avalonia.Media.Imaging.WriteableBitmap>(
            view.BitmapForTesting);

        Assert.Equal(
            ColorOf(HappyPhotonColors.WaveformBackdrop),
            ReadPixel(bitmap, 0, 0));
        Assert.Equal(
            ColorOf(HappyPhotonColors.WaveformTrace),
            ReadPixel(bitmap, 0, WaveformData.LevelCount - 1 - 64));

        view.Waveform = FilledWaveform(level: 32);
        Assert.Same(bitmap, view.BitmapForTesting);
        view.Waveform = null;
        Assert.Equal(
            ColorOf(HappyPhotonColors.WaveformBackdrop),
            ReadPixel(bitmap, 0, WaveformData.LevelCount - 1 - 32));
        view.Waveform = FilledWaveform(level: 32);

        view.Repaint();
        var allocationStart = GC.GetAllocatedBytesForCurrentThread();
        view.Repaint();
        var repaintAllocation =
            GC.GetAllocatedBytesForCurrentThread() - allocationStart;
        _output.WriteLine(
            $"Warmed WaveformView repaint allocated {repaintAllocation} bytes " +
            "including Avalonia's framebuffer lock wrapper.");
        Assert.True(repaintAllocation < 4096);
        Assert.Same(bitmap, view.BitmapForTesting);

        // test-teardown-policy: allow - themeScope restores the prior variant.
        Application.Current!.RequestedThemeVariant = HappyPhotonThemes.MidGray;
        Dispatcher.UIThread.RunJobs();
        Assert.Same(bitmap, view.BitmapForTesting);
        Assert.Equal(
            ColorOf(HappyPhotonColors.MidGrayWaveformBackdrop),
            ReadPixel(bitmap, 0, 0));

        window.Close();
        Assert.Null(view.BitmapForTesting);
        Assert.Throws<ObjectDisposedException>(() => _ = bitmap.PixelSize);
    }

    // FIXES-DEVELOP-WP12 G4: a taller scope stretches the same 256 x 128 bitmap; resizing never repaints it.
    [AvaloniaFact]
    public void Resize_StretchesTheSameBitmapWithoutRepainting()
    {
        using var themeScope = new TestUiScope(
            theme: Avalonia.Styling.ThemeVariant.Dark);
        var view = new WaveformView
        {
            Waveform = FilledWaveform(level: 64),
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top
        };
        var window = new Window { Width = 222, Height = 300, Content = view };
        using var windowScope = new TestUiScope(window);
        Dispatcher.UIThread.RunJobs();
        var image = view.FindControl<Image>("WaveformImage")!;
        var bitmap = Assert.IsType<Avalonia.Media.Imaging.WriteableBitmap>(
            view.BitmapForTesting);
        var marker = Color.FromRgb(1, 2, 3);
        WritePixel(bitmap, 0, 0, marker);

        Assert.Equal(80, image.Bounds.Height);

        window.Width = 422;
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();

        Assert.Equal(152, image.Bounds.Height);
        Assert.Same(bitmap, view.BitmapForTesting);
        Assert.Equal(
            new PixelSize(WaveformData.ColumnCount, WaveformData.LevelCount),
            bitmap.PixelSize);
        Assert.Equal(marker, ReadPixel(bitmap, 0, 0));

        view.Repaint();
        var allocationStart = GC.GetAllocatedBytesForCurrentThread();
        view.Repaint();
        var repaintAllocation =
            GC.GetAllocatedBytesForCurrentThread() - allocationStart;
        _output.WriteLine(
            $"Warmed WaveformView repaint at 152 px allocated {repaintAllocation} bytes.");

        Assert.True(repaintAllocation < 4096);
        Assert.Same(bitmap, view.BitmapForTesting);
    }

    [AvaloniaTheory]
    [MemberData(nameof(ThemeResourceTests.Variants),
        MemberType = typeof(ThemeResourceTests))]
    public void ThemeTokens_MatchCodeDrawnTwins(
        Avalonia.Styling.ThemeVariant variant)
    {
        var trace = ThemeResourceTests.Brush("WaveformTrace", variant).Color;
        var backdrop = ThemeResourceTests.Brush(
            "WaveformBackdrop",
            variant).Color;

        Assert.Equal(ColorOf(HappyPhotonColors.WaveformTrace), trace);
        Assert.Equal(
            ColorOf(variant == HappyPhotonThemes.MidGray
                ? HappyPhotonColors.MidGrayWaveformBackdrop
                : HappyPhotonColors.WaveformBackdrop),
            backdrop);
        Assert.Equal(
            ThemeResourceTests.Brush("SurfaceLow", variant).Color,
            backdrop);
    }

    private static WaveformData FilledWaveform(int level)
    {
        var waveform = new WaveformData();
        for (var column = 0; column < WaveformData.ColumnCount; column++)
        {
            waveform.ColumnSampleCounts[column] = 1;
            waveform.Luminance[
                level * WaveformData.ColumnCount + column] = 1;
        }
        return waveform;
    }

    private static Color ReadPixel(
        Avalonia.Media.Imaging.WriteableBitmap bitmap,
        int x,
        int y)
    {
        using var framebuffer = bitmap.Lock();
        var bgra = new byte[4];
        Marshal.Copy(
            IntPtr.Add(framebuffer.Address, y * framebuffer.RowBytes + x * 4),
            bgra,
            0,
            bgra.Length);
        return Color.FromArgb(bgra[3], bgra[2], bgra[1], bgra[0]);
    }

    private static void WritePixel(
        Avalonia.Media.Imaging.WriteableBitmap bitmap,
        int x,
        int y,
        Color color)
    {
        using var framebuffer = bitmap.Lock();
        var bgra = new[] { color.B, color.G, color.R, color.A };

        Marshal.Copy(
            bgra,
            0,
            IntPtr.Add(framebuffer.Address, y * framebuffer.RowBytes + x * 4),
            bgra.Length);
    }

    private static Color ColorOf(IBrush brush) =>
        Assert.IsAssignableFrom<ISolidColorBrush>(brush).Color;
}
