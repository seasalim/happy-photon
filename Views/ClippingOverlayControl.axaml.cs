using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.VisualTree;
using HappyPhoton.Services;

namespace HappyPhoton.Views;

public partial class ClippingOverlayControl : UserControl
{
    public static readonly StyledProperty<ClippingMask?> MaskProperty =
        AvaloniaProperty.Register<ClippingOverlayControl, ClippingMask?>(
            nameof(Mask));

    public static readonly StyledProperty<ClippingOverlaySide> VisibleSidesProperty =
        AvaloniaProperty.Register<ClippingOverlayControl, ClippingOverlaySide>(
            nameof(VisibleSides));

    private Image? _image;
    private WriteableBitmap? _bitmap;

    public ClippingMask? Mask
    {
        get => GetValue(MaskProperty);
        set => SetValue(MaskProperty, value);
    }

    public ClippingOverlaySide VisibleSides
    {
        get => GetValue(VisibleSidesProperty);
        set => SetValue(VisibleSidesProperty, value);
    }

    internal WriteableBitmap? BitmapForTesting => _bitmap;

    public ClippingOverlayControl()
    {
        InitializeComponent();
        _image = this.FindControl<Image>("OverlayImage");
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == MaskProperty ||
            change.Property == VisibleSidesProperty)
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
        ClearBitmap();
        base.OnDetachedFromVisualTree(e);
    }

    internal unsafe void Repaint()
    {
        ClearBitmap();
        var mask = Mask;
        var sides = VisibleSides;
        if (_image == null || mask == null || sides == ClippingOverlaySide.None)
        {
            return;
        }

        var bitmap = new WriteableBitmap(
            new PixelSize(mask.Width, mask.Height),
            new Vector(96, 96),
            PixelFormat.Bgra8888,
            AlphaFormat.Unpremul);
        try
        {
            using var framebuffer = bitmap.Lock();
            var destination = new Span<byte>(
                framebuffer.Address.ToPointer(),
                framebuffer.RowBytes * mask.Height);
            var flags = mask.Flags;
            for (var y = 0; y < mask.Height; y++)
            {
                var row = destination.Slice(y * framebuffer.RowBytes);
                for (var x = 0; x < mask.Width; x++)
                {
                    var flag = (ClippingOverlaySide)flags[y * mask.Width + x];
                    var color = flag.HasFlag(
                            ClippingOverlaySide.SceneHighlights) &&
                        sides.HasFlag(ClippingOverlaySide.SceneHighlights)
                            ? HappyPhotonColors.SceneHighlightClipColor
                            : flag.HasFlag(ClippingOverlaySide.DisplayFloor) &&
                              sides.HasFlag(ClippingOverlaySide.DisplayFloor)
                                ? HappyPhotonColors.DisplayFloorClipColor
                                : default;
                    var offset = x * 4;
                    row[offset] = color.B;
                    row[offset + 1] = color.G;
                    row[offset + 2] = color.R;
                    row[offset + 3] = color.A;
                }
            }
            _bitmap = bitmap;
            _image.Source = bitmap;
        }
        catch
        {
            bitmap.Dispose();
            throw;
        }
    }

    private void ClearBitmap()
    {
        if (_image != null)
        {
            _image.Source = null;
        }
        _bitmap?.Dispose();
        _bitmap = null;
    }
}
