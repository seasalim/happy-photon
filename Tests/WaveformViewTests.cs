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
        Application.Current!.RequestedThemeVariant =
            Avalonia.Styling.ThemeVariant.Dark;
        var view = new WaveformView { Waveform = FilledWaveform(level: 64) };
        var window = new Window { Width = 256, Height = 80, Content = view };
        window.Show();
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

        Application.Current.RequestedThemeVariant = HappyPhotonThemes.MidGray;
        Dispatcher.UIThread.RunJobs();
        Assert.Same(bitmap, view.BitmapForTesting);
        Assert.Equal(
            ColorOf(HappyPhotonColors.MidGrayWaveformBackdrop),
            ReadPixel(bitmap, 0, 0));

        window.Close();
        Assert.Null(view.BitmapForTesting);
        Assert.Throws<ObjectDisposedException>(() => _ = bitmap.PixelSize);
        Application.Current.RequestedThemeVariant =
            Avalonia.Styling.ThemeVariant.Dark;
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

    private static Color ColorOf(IBrush brush) =>
        Assert.IsAssignableFrom<ISolidColorBrush>(brush).Color;
}
