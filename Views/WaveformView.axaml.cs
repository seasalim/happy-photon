using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.VisualTree;
using HappyPhoton.Models;

namespace HappyPhoton.Views;

public partial class WaveformView : UserControl
{
    public static readonly StyledProperty<WaveformData?> WaveformProperty =
        AvaloniaProperty.Register<WaveformView, WaveformData?>(nameof(Waveform));

    private Image? _image;
    private WriteableBitmap? _bitmap;

    public WaveformData? Waveform
    {
        get => GetValue(WaveformProperty);
        set => SetValue(WaveformProperty, value);
    }

    internal WriteableBitmap? BitmapForTesting => _bitmap;

    public WaveformView()
    {
        InitializeComponent();
        _image = this.FindControl<Image>("WaveformImage");
        ActualThemeVariantChanged += (_, _) => Repaint();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == WaveformProperty)
        {
            Repaint();
        }
    }

    protected override void OnAttachedToVisualTree(
        VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Repaint();
    }

    protected override void OnDetachedFromVisualTree(
        VisualTreeAttachmentEventArgs e)
    {
        if (_image != null)
        {
            _image.Source = null;
        }
        _bitmap?.Dispose();
        _bitmap = null;
        base.OnDetachedFromVisualTree(e);
    }

    internal unsafe void Repaint()
    {
        if (_image == null)
        {
            return;
        }

        if (_bitmap == null)
        {
            if (Waveform == null)
            {
                return;
            }
            _bitmap = new WriteableBitmap(
                new PixelSize(
                    WaveformData.ColumnCount,
                    WaveformData.LevelCount),
                new Vector(96, 96),
                PixelFormat.Bgra8888,
                AlphaFormat.Opaque);
            _image.Source = _bitmap;
        }

        using var framebuffer = _bitmap.Lock();
        var pixels = new Span<byte>(
            framebuffer.Address.ToPointer(),
            framebuffer.RowBytes * WaveformData.LevelCount);
        WaveformPainter.Paint(
            Waveform,
            pixels,
            framebuffer.RowBytes,
            ResolveColor("WaveformBackdrop", BackdropFallback()),
            ResolveColor(
                "WaveformTrace",
                ColorOf(HappyPhotonColors.WaveformTrace)));
        _image.InvalidateVisual();
    }

    private Color BackdropFallback() =>
        ColorOf(ActualThemeVariant == HappyPhotonThemes.MidGray
            ? HappyPhotonColors.MidGrayWaveformBackdrop
            : HappyPhotonColors.WaveformBackdrop);

    private Color ResolveColor(string key, Color fallback) =>
        this.TryFindResource(key, ActualThemeVariant, out var resource) &&
        resource is ISolidColorBrush brush
            ? brush.Color
            : fallback;

    private static Color ColorOf(IBrush brush) =>
        ((ISolidColorBrush)brush).Color;
}
