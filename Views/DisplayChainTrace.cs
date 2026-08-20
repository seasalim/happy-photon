using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HappyPhoton.Services;

namespace HappyPhoton.Views;

internal readonly record struct DisplayChainMapping(
    PixelSize BitmapPixels,
    Size ImageLogicalSize,
    Size ViewportLogicalSize,
    double RenderScaling,
    Rect DeviceRectangle,
    double NetScaleX,
    double NetScaleY)
{
    private const double OneToOneTolerance = 0.005;

    public bool IsOneToOne =>
        NetScaleX >= 1 - OneToOneTolerance &&
        NetScaleX <= 1 + OneToOneTolerance &&
        NetScaleY >= 1 - OneToOneTolerance &&
        NetScaleY <= 1 + OneToOneTolerance;
}

internal static class DisplayChainMappingCalculator
{
    public static DisplayChainMapping Calculate(
        PixelSize bitmapPixels,
        Size imageLogicalSize,
        Size viewportLogicalSize,
        double renderScaling)
    {
        if (bitmapPixels.Width <= 0 || bitmapPixels.Height <= 0)
            throw new ArgumentOutOfRangeException(nameof(bitmapPixels));
        if (imageLogicalSize.Width < 0 || imageLogicalSize.Height < 0)
            throw new ArgumentOutOfRangeException(nameof(imageLogicalSize));
        if (viewportLogicalSize.Width < 0 || viewportLogicalSize.Height < 0)
            throw new ArgumentOutOfRangeException(nameof(viewportLogicalSize));
        if (!double.IsFinite(renderScaling) || renderScaling <= 0)
            throw new ArgumentOutOfRangeException(nameof(renderScaling));

        var deviceWidth = imageLogicalSize.Width * renderScaling;
        var deviceHeight = imageLogicalSize.Height * renderScaling;
        return new DisplayChainMapping(
            bitmapPixels,
            imageLogicalSize,
            viewportLogicalSize,
            renderScaling,
            new Rect(0, 0, deviceWidth, deviceHeight),
            deviceWidth / bitmapPixels.Width,
            deviceHeight / bitmapPixels.Height);
    }
}

internal sealed class DisplayChainTrace
{
    private static Action? _calculationObserverForTesting;

    private readonly ZoomPanControl _owner;
    private readonly Image _image;
    private readonly ScrollViewer _scrollViewer;
    private TopLevel? _topLevel;
    private bool _emissionPending;
    private MappingTuple? _lastMapping;

    public DisplayChainTrace(
        ZoomPanControl owner,
        Image image,
        ScrollViewer scrollViewer)
    {
        _owner = owner;
        _image = image;
        _scrollViewer = scrollViewer;
        _owner.LayoutUpdated += OnLayoutUpdated;
        _owner.AttachedToVisualTree += OnAttachedToVisualTree;
        _owner.DetachedFromVisualTree += OnDetachedFromVisualTree;
        if (_owner.IsAttachedToVisualTree())
        {
            AttachTopLevel();
            ScheduleEmission();
        }
    }

    public void OnInputChanged()
    {
        if (!_owner.IsDisplayTraceActive)
        {
            _lastMapping = null;
        }
        ScheduleEmission();
    }

    internal static IDisposable OverrideCalculationObserverForTesting(
        Action observer)
    {
        ArgumentNullException.ThrowIfNull(observer);
        var previous = _calculationObserverForTesting;
        _calculationObserverForTesting = observer;
        return new DelegateDisposable(() =>
            _calculationObserverForTesting = previous);
    }

    private void OnAttachedToVisualTree(
        object? sender,
        VisualTreeAttachmentEventArgs e)
    {
        AttachTopLevel();
        ScheduleEmission();
    }

    private void OnDetachedFromVisualTree(
        object? sender,
        VisualTreeAttachmentEventArgs e)
    {
        DetachTopLevel();
        _lastMapping = null;
        _emissionPending = false;
    }

    private void AttachTopLevel()
    {
        var topLevel = TopLevel.GetTopLevel(_owner);
        if (ReferenceEquals(topLevel, _topLevel)) return;
        DetachTopLevel();
        _topLevel = topLevel;
        if (_topLevel != null)
        {
            _topLevel.ScalingChanged += OnTopLevelScalingChanged;
        }
    }

    private void DetachTopLevel()
    {
        if (_topLevel != null)
        {
            _topLevel.ScalingChanged -= OnTopLevelScalingChanged;
            _topLevel = null;
        }
    }

    private void OnTopLevelScalingChanged(object? sender, EventArgs e) =>
        ScheduleEmission();

    private void OnLayoutUpdated(object? sender, EventArgs e) =>
        ScheduleEmission();

    private void ScheduleEmission()
    {
        if (_emissionPending || !_owner.IsAttachedToVisualTree()) return;
        _emissionPending = true;
        Dispatcher.UIThread.Post(EmitIfChanged, DispatcherPriority.Render);
    }

    private void EmitIfChanged()
    {
        _emissionPending = false;
        var source = _owner.Source;
        var topLevel = _topLevel;
        if (!_owner.IsAttachedToVisualTree() ||
            !_owner.IsDisplayTraceActive ||
            !_owner.IsEffectivelyVisible ||
            source == null ||
            topLevel == null ||
            _image.Bounds.Width <= 0 ||
            _image.Bounds.Height <= 0 ||
            _scrollViewer.Viewport.Width <= 0 ||
            _scrollViewer.Viewport.Height <= 0)
        {
            if (!_owner.IsDisplayTraceActive) _lastMapping = null;
            return;
        }

        _calculationObserverForTesting?.Invoke();
        var mapping = DisplayChainMappingCalculator.Calculate(
            source.PixelSize,
            _image.Bounds.Size,
            _scrollViewer.Viewport,
            topLevel.RenderScaling);
        var tuple = new MappingTuple(source, mapping);
        if (_lastMapping is { } previous && previous.Equals(tuple)) return;
        _lastMapping = tuple;

        ImageServiceHelpers.LogDisplayTrace(
            $"mapping bitmap={mapping.BitmapPixels.Width}x{mapping.BitmapPixels.Height} " +
            $"logical={F(mapping.ImageLogicalSize.Width)}x{F(mapping.ImageLogicalSize.Height)} " +
            $"viewport={F(mapping.ViewportLogicalSize.Width)}x{F(mapping.ViewportLogicalSize.Height)} " +
            $"renderScaling={F(mapping.RenderScaling)} " +
            $"deviceRect={F(mapping.DeviceRectangle.X)},{F(mapping.DeviceRectangle.Y)}," +
            $"{F(mapping.DeviceRectangle.Width)},{F(mapping.DeviceRectangle.Height)} " +
            $"netScale={F(mapping.NetScaleX)}x{F(mapping.NetScaleY)} " +
            $"oneToOne={mapping.IsOneToOne.ToString().ToLowerInvariant()}");
    }

    private static string F(double value) =>
        value.ToString("0.######", CultureInfo.InvariantCulture);

    private sealed class MappingTuple(Bitmap bitmap, DisplayChainMapping mapping)
    {
        private Bitmap Bitmap { get; } = bitmap;
        private DisplayChainMapping Mapping { get; } = mapping;

        public bool Equals(MappingTuple other) =>
            ReferenceEquals(Bitmap, other.Bitmap) && Mapping == other.Mapping;
    }

    private sealed class DelegateDisposable(Action dispose) : IDisposable
    {
        private Action? _dispose = dispose;
        public void Dispose() => Interlocked.Exchange(ref _dispose, null)?.Invoke();
    }
}
