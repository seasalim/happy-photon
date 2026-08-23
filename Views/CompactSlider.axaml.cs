using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace HappyPhoton.Views;

public partial class CompactSlider : UserControl
{
    public static readonly StyledProperty<string> LabelProperty =
        AvaloniaProperty.Register<CompactSlider, string>(nameof(Label), "Label");

    public static readonly StyledProperty<double> ValueProperty =
        AvaloniaProperty.Register<CompactSlider, double>(nameof(Value), 0.0,
            defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    public static readonly StyledProperty<double> MinimumProperty =
        AvaloniaProperty.Register<CompactSlider, double>(nameof(Minimum), -100.0);

    public static readonly StyledProperty<double> MaximumProperty =
        AvaloniaProperty.Register<CompactSlider, double>(nameof(Maximum), 100.0);

    public static readonly StyledProperty<string> StringFormatProperty =
        AvaloniaProperty.Register<CompactSlider, string>(nameof(StringFormat), "{0:0}");

    public static readonly StyledProperty<string?> DisplayTextProperty =
        AvaloniaProperty.Register<CompactSlider, string?>(nameof(DisplayText));

    public static readonly StyledProperty<double> SmallChangeProperty =
        AvaloniaProperty.Register<CompactSlider, double>(nameof(SmallChange), 1.0);

    public static readonly StyledProperty<double> DefaultValueProperty =
        AvaloniaProperty.Register<CompactSlider, double>(nameof(DefaultValue), 0.0);

    public static readonly StyledProperty<bool> EnableDoubleClickResetProperty =
        AvaloniaProperty.Register<CompactSlider, bool>(nameof(EnableDoubleClickReset), false);

    public static readonly StyledProperty<IBrush?> TrackBrushProperty =
        AvaloniaProperty.Register<CompactSlider, IBrush?>(nameof(TrackBrush));

    public static readonly StyledProperty<bool> ShowValueFillProperty =
        AvaloniaProperty.Register<CompactSlider, bool>(nameof(ShowValueFill), true);

    public string Label
    {
        get => GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public double Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public double Minimum
    {
        get => GetValue(MinimumProperty);
        set => SetValue(MinimumProperty, value);
    }

    public double Maximum
    {
        get => GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    public string StringFormat
    {
        get => GetValue(StringFormatProperty);
        set => SetValue(StringFormatProperty, value);
    }

    public string? DisplayText
    {
        get => GetValue(DisplayTextProperty);
        set => SetValue(DisplayTextProperty, value);
    }

    public double SmallChange
    {
        get => GetValue(SmallChangeProperty);
        set => SetValue(SmallChangeProperty, value);
    }

    public double DefaultValue
    {
        get => GetValue(DefaultValueProperty);
        set => SetValue(DefaultValueProperty, value);
    }

    public bool EnableDoubleClickReset
    {
        get => GetValue(EnableDoubleClickResetProperty);
        set => SetValue(EnableDoubleClickResetProperty, value);
    }

    public IBrush? TrackBrush
    {
        get => GetValue(TrackBrushProperty);
        set => SetValue(TrackBrushProperty, value);
    }

    public bool ShowValueFill
    {
        get => GetValue(ShowValueFillProperty);
        set => SetValue(ShowValueFillProperty, value);
    }

    private const double DragThreshold = 2;

    private Grid? _layoutGrid;
    private Grid? _trackGrid;
    private Border? _fillBar;
    private Border? _centerMark;
    private TextBlock? _labelText;
    private TextBlock? _valueText;
    private Border? _thumbDot;
    private bool _isDragging;
    private bool _hasDragStarted;
    private double _dragStartX;
    private double _dragStartValue;

    public CompactSlider()
    {
        InitializeComponent();

        _layoutGrid = this.FindControl<Grid>("LayoutGrid");
        _trackGrid = this.FindControl<Grid>("TrackGrid");
        _fillBar = this.FindControl<Border>("FillBar");
        _centerMark = this.FindControl<Border>("CenterMark");
        _labelText = this.FindControl<TextBlock>("LabelText");
        _valueText = this.FindControl<TextBlock>("ValueText");
        _thumbDot = this.FindControl<Border>("ThumbDot");

        if (_layoutGrid != null)
        {
            _layoutGrid.AddHandler(
                InputElement.PointerPressedEvent,
                OnTrackPointerPressed,
                RoutingStrategies.Tunnel,
                handledEventsToo: true);
            _layoutGrid.AddHandler(
                InputElement.PointerMovedEvent,
                OnTrackPointerMoved,
                RoutingStrategies.Tunnel,
                handledEventsToo: true);
            _layoutGrid.AddHandler(
                InputElement.PointerReleasedEvent,
                OnTrackPointerReleased,
                RoutingStrategies.Tunnel,
                handledEventsToo: true);
            _layoutGrid.PointerCaptureLost += OnTrackPointerCaptureLost;
        }

        UpdateDisplay();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == ValueProperty ||
            change.Property == MinimumProperty ||
            change.Property == MaximumProperty ||
            change.Property == StringFormatProperty ||
            change.Property == DisplayTextProperty ||
            change.Property == ShowValueFillProperty ||
            change.Property == LabelProperty)
        {
            UpdateDisplay();
        }
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        UpdateFillBar();
    }

    private void UpdateDisplay()
    {
        if (_labelText != null)
        {
            _labelText.Text = Label;
        }

        if (_valueText != null)
        {
            _valueText.Text = DisplayText ?? string.Format(StringFormat, Value);
        }

        UpdateFillBar();
    }

    private void UpdateFillBar()
    {
        if (_trackGrid == null || _fillBar == null || _centerMark == null) return;

        var trackWidth = _trackGrid.Bounds.Width;
        if (trackWidth <= 0) return;

        var range = Maximum - Minimum;
        if (range <= 0) return;

        var normalizedValue = (Value - Minimum) / range;
        var normalizedZero = (0 - Minimum) / range;

        bool isBipolar = Minimum < 0 && Maximum > 0;

        double valueX;
        if (isBipolar)
        {
            _centerMark.IsVisible = ShowValueFill;

            var centerX = normalizedZero * trackWidth;
            valueX = normalizedValue * trackWidth;

            if (Value >= 0)
            {
                _fillBar.Margin = new Thickness(centerX, 0, 0, 0);
                _fillBar.Width = Math.Max(0, valueX - centerX);
            }
            else
            {
                _fillBar.Margin = new Thickness(valueX, 0, 0, 0);
                _fillBar.Width = Math.Max(0, centerX - valueX);
            }
        }
        else
        {
            _centerMark.IsVisible = false;
            _fillBar.Margin = new Thickness(0);
            valueX = normalizedValue * trackWidth;
            _fillBar.Width = valueX;
        }
        _fillBar.IsVisible = ShowValueFill;

        if (_thumbDot != null)
        {
            // Thumb center sits at the value position (NOT at fill-width: for a
            // negative bipolar value the fill's left edge is the value side).
            var thumbWidth = double.IsNaN(_thumbDot.Width)
                ? 12
                : _thumbDot.Width;
            var thumbLeft = Math.Clamp(
                valueX - thumbWidth / 2,
                0,
                Math.Max(0, trackWidth - thumbWidth));
            _thumbDot.Margin = new Thickness(thumbLeft, 0, 0, 0);
        }
    }

    private void OnTrackPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_layoutGrid == null || _trackGrid == null) return;
        if (e.Pointer.Type == PointerType.Mouse &&
            !e.GetCurrentPoint(_layoutGrid).Properties.IsLeftButtonPressed) return;

        if (EnableDoubleClickReset && e.ClickCount == 2)
        {
            _isDragging = false;
            _hasDragStarted = false;
            _thumbDot?.Classes.Set("pointer-captured", false);
            e.Pointer.Capture(null);
            Value = DefaultValue;
            e.Handled = true;
            return;
        }

        _isDragging = true;
        _hasDragStarted = false;
        _dragStartX = e.GetPosition(_trackGrid).X;
        _dragStartValue = Value;
        e.Pointer.Capture(_layoutGrid);
        _thumbDot?.Classes.Set(
            "pointer-captured",
            e.Pointer.Captured == _layoutGrid);
        e.Handled = true;
    }

    private void OnTrackPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_isDragging || _trackGrid == null) return;

        var pointerX = e.GetPosition(_trackGrid).X;
        if (!_hasDragStarted && Math.Abs(pointerX - _dragStartX) < DragThreshold) return;

        _hasDragStarted = true;
        UpdateValueFromDrag(pointerX);
        e.Handled = true;
    }

    private void OnTrackPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_isDragging && _trackGrid != null)
        {
            var pointerX = e.GetPosition(_trackGrid).X;
            if (_hasDragStarted || Math.Abs(pointerX - _dragStartX) >= DragThreshold)
            {
                UpdateValueFromDrag(pointerX);
            }
        }

        _isDragging = false;
        _hasDragStarted = false;
        _thumbDot?.Classes.Set("pointer-captured", false);
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    private void OnTrackPointerCaptureLost(
        object? sender,
        PointerCaptureLostEventArgs e)
    {
        _isDragging = false;
        _hasDragStarted = false;
        _thumbDot?.Classes.Set("pointer-captured", false);
    }

    private void UpdateValueFromDrag(double pointerX)
    {
        if (_trackGrid == null) return;

        var trackWidth = _trackGrid.Bounds.Width;
        if (trackWidth <= 0) return;

        var range = Maximum - Minimum;
        var newValue = _dragStartValue + ((pointerX - _dragStartX) / trackWidth * range);

        newValue = Math.Round(newValue / SmallChange) * SmallChange;
        newValue = Math.Clamp(newValue, Minimum, Maximum);

        Value = newValue;
    }
}
