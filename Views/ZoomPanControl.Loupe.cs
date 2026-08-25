using Avalonia;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace HappyPhoton.Views;

public partial class ZoomPanControl
{
    public static readonly StyledProperty<object?> SourceIdentityProperty =
        AvaloniaProperty.Register<ZoomPanControl, object?>(nameof(SourceIdentity));

    public static readonly DirectProperty<ZoomPanControl, bool>
        IsLoupePeekActiveProperty =
            AvaloniaProperty.RegisterDirect<ZoomPanControl, bool>(
                nameof(IsLoupePeekActive),
                control => control.IsLoupePeekActive);

    private static readonly TimeSpan LoupeHoldDuration =
        TimeSpan.FromMilliseconds(150);
    private static readonly Lazy<Cursor> LoupeCursor = new(CreateLoupeCursor);

    private TimeProvider _timeProvider;
    private LoupeHold? _loupeHold;
    private LoupePeek? _loupePeek;
    private ITimer? _loupeHoldTimer;
    private IPointer? _loupePointer;
    private long _loupeHoldGeneration;

    private readonly record struct LoupeHold(
        Point PressPoint,
        Point CurrentPoint,
        object? SourceIdentity);

    private readonly record struct LoupePeek(
        ViewportAnchor? RestoreAnchor,
        object? SourceIdentity);

    public bool IsLoupePeekActive => _loupePeek != null;

    public object? SourceIdentity
    {
        get => GetValue(SourceIdentityProperty);
        set => SetValue(SourceIdentityProperty, value);
    }

    private bool EffectiveAutoFit => AutoFit && _loupePeek == null;

    private double EffectiveZoomLevel =>
        _loupePeek == null ? ZoomLevel : 1;

    private void InitializeLoupePeek()
    {
        PointerCaptureLost += OnLoupePointerCaptureLost;
        DetachedFromVisualTree += OnLoupeDetachedFromVisualTree;
    }

    internal void SetLoupeTimeProvider(TimeProvider timeProvider) =>
        _timeProvider = timeProvider;

    private bool TryBeginLoupePeek(PointerPressedEventArgs e)
    {
        if (_loupeHold != null || _loupePeek != null ||
            !CanStartLoupePeek() || _scrollViewer == null)
        {
            return false;
        }

        _loupePointer = e.Pointer;
        _pressPoint = e.GetPosition(this);
        _lastPanPoint = _pressPoint;
        _isPanning = false;
        _loupeHold = new LoupeHold(
            _pressPoint,
            _pressPoint,
            SourceIdentity);
        var generation = ++_loupeHoldGeneration;
        _loupeHoldTimer = _timeProvider.CreateTimer(
            _ => Dispatcher.UIThread.Post(() => EngageLoupePeek(generation)),
            null,
            LoupeHoldDuration,
            Timeout.InfiniteTimeSpan);
        e.Pointer.Capture(this);
        return true;
    }

    private bool CanStartLoupePeek()
    {
        return Source != null &&
            !IsCropMode &&
            !IsWhiteBalancePicking &&
            ZoomLevel < 1;
    }

    private void EngageLoupePeek(long generation)
    {
        if (generation != _loupeHoldGeneration ||
            _loupeHold is not { } hold ||
            !CanStartLoupePeek() ||
            _scrollViewer == null ||
            _imageControl == null ||
            _imageControl.Bounds.Width <= 0 ||
            _imageControl.Bounds.Height <= 0)
        {
            return;
        }

        var imageOrigin = _imageControl.TranslatePoint(default, _scrollViewer);
        var pressPoint = this.TranslatePoint(hold.PressPoint, _scrollViewer);
        if (imageOrigin == null || pressPoint == null)
        {
            return;
        }

        var restoreAnchor = CaptureViewportCenterAnchor();
        _loupeHold = null;
        DisposeLoupeHoldTimer();
        var anchor = CreateViewportAnchor(
            pressPoint.Value - imageOrigin.Value,
            pressPoint.Value);
        SetLoupePeek(new LoupePeek(restoreAnchor, hold.SourceIdentity));
        ScheduleAnchorRestoreAfterLayout(anchor);
        UpdateImageSize();
        RequestRequiredBoundPublication();
        _isPanning = true;
        _lastPanPoint = hold.CurrentPoint;
        UpdatePointerCursor();
    }

    private bool UpdatePendingLoupeHold(PointerEventArgs e)
    {
        if (_loupeHold is not { } hold)
        {
            return false;
        }

        var pointerPoint = e.GetPosition(this);
        var delta = hold.PressPoint - pointerPoint;
        if (delta.X * delta.X + delta.Y * delta.Y >
            PointerMovementThresholdSquared)
        {
            CancelPendingLoupeHoldForPan();
            _isPanning = true;
            UpdatePointerCursor();
            return false;
        }

        _loupeHold = hold with { CurrentPoint = pointerPoint };
        return true;
    }

    private void CancelPendingLoupeHoldForPan()
    {
        ++_loupeHoldGeneration;
        _loupeHold = null;
        _loupePointer = null;
        DisposeLoupeHoldTimer();
    }

    internal bool CancelLoupePeek() => EndLoupePeek(releaseCapture: true);

    private bool EndLoupePeek(bool releaseCapture)
    {
        var hadGesture = _loupeHold != null || _loupePeek != null;
        if (!hadGesture)
        {
            return false;
        }

        ++_loupeHoldGeneration;
        _loupeHold = null;
        DisposeLoupeHoldTimer();
        var peek = _loupePeek;
        _isPanning = false;
        if (peek != null)
        {
            SetLoupePeek(null);
            if (AutoFit)
            {
                RequestAutoFit();
            }
            ScheduleAnchorRestoreAfterLayout(peek.Value.RestoreAnchor);
            UpdateImageSize();
            RequestRequiredBoundPublication();
        }

        var pointer = _loupePointer;
        _loupePointer = null;
        if (releaseCapture)
        {
            pointer?.Capture(null);
        }
        UpdatePointerCursor();
        return true;
    }

    private void SetLoupePeek(LoupePeek? value)
    {
        var wasActive = IsLoupePeekActive;
        _loupePeek = value;
        RaisePropertyChanged(
            IsLoupePeekActiveProperty,
            wasActive,
            IsLoupePeekActive);
    }

    private void DisposeLoupeHoldTimer()
    {
        _loupeHoldTimer?.Dispose();
        _loupeHoldTimer = null;
    }

    private void OnLoupePointerCaptureLost(
        object? sender,
        PointerCaptureLostEventArgs e) =>
        EndLoupePeek(releaseCapture: false);

    private void OnLoupeDetachedFromVisualTree(
        object? sender,
        VisualTreeAttachmentEventArgs e) =>
        EndLoupePeek(releaseCapture: false);

    private void OnSourceIdentityChanged(object? newIdentity)
    {
        var gestureIdentity = _loupePeek?.SourceIdentity ??
            _loupeHold?.SourceIdentity;
        if (gestureIdentity != null && newIdentity != null &&
            !ReferenceEquals(gestureIdentity, newIdentity))
        {
            EndLoupePeek(releaseCapture: true);
        }
    }

    private void UpdatePointerCursor()
    {
        Cursor = _isPanning
            ? new Cursor(StandardCursorType.Hand)
            : IsWhiteBalancePicking
                ? new Cursor(StandardCursorType.Cross)
                : CanStartLoupePeek()
                    ? LoupeCursor.Value
                    : Cursor.Default;
    }

    private static Cursor CreateLoupeCursor()
    {
        using var stream = AssetLoader.Open(
            new Uri("avares://HappyPhoton/Assets/loupe-cursor.png"));
        using var bitmap = new Bitmap(stream);
        return new Cursor(bitmap, new PixelPoint(12, 12));
    }
}
