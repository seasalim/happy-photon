using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;

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

    public static readonly StyledProperty<double> SmallChangeProperty =
        AvaloniaProperty.Register<CompactSlider, double>(nameof(SmallChange), 1.0);

    public static readonly StyledProperty<double> DefaultValueProperty =
        AvaloniaProperty.Register<CompactSlider, double>(nameof(DefaultValue), 0.0);

    public static readonly StyledProperty<bool> EnableDoubleClickResetProperty =
        AvaloniaProperty.Register<CompactSlider, bool>(nameof(EnableDoubleClickReset), false);

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

    private Border? _trackArea;
    private Grid? _trackGrid;
    private Border? _fillBar;
    private Border? _centerMark;
    private TextBlock? _labelText;
    private TextBlock? _valueText;
    private Avalonia.Controls.Shapes.Ellipse? _thumbDot;
    private bool _isDragging;

    public CompactSlider()
    {
        InitializeComponent();

        _trackArea = this.FindControl<Border>("TrackArea");
        _trackGrid = this.FindControl<Grid>("TrackGrid");
        _fillBar = this.FindControl<Border>("FillBar");
        _centerMark = this.FindControl<Border>("CenterMark");
        _labelText = this.FindControl<TextBlock>("LabelText");
        _valueText = this.FindControl<TextBlock>("ValueText");
        _thumbDot = this.FindControl<Avalonia.Controls.Shapes.Ellipse>("ThumbDot");

        if (_trackArea != null)
        {
            _trackArea.PointerPressed += OnTrackPointerPressed;
            _trackArea.PointerMoved += OnTrackPointerMoved;
            _trackArea.PointerReleased += OnTrackPointerReleased;
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
            _valueText.Text = string.Format(StringFormat, Value);
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
            _centerMark.IsVisible = true;

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

        if (_thumbDot != null)
        {
            // Thumb center sits at the value position (NOT at fill-width: for a
            // negative bipolar value the fill's left edge is the value side).
            var thumbLeft = Math.Clamp(valueX - 6, 0, Math.Max(0, trackWidth - 12));
            _thumbDot.Margin = new Thickness(thumbLeft, 0, 0, 0);
        }
    }

    private void OnTrackPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_trackArea == null) return;

        if (EnableDoubleClickReset && e.ClickCount == 2)
        {
            _isDragging = false;
            e.Pointer.Capture(null);
            Value = DefaultValue;
            e.Handled = true;
            return;
        }

        _isDragging = true;
        e.Pointer.Capture(_trackArea);
        UpdateValueFromPointer(e.GetPosition(_trackArea));
    }

    private void OnTrackPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_isDragging || _trackArea == null) return;
        UpdateValueFromPointer(e.GetPosition(_trackArea));
    }

    private void OnTrackPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _isDragging = false;
        e.Pointer.Capture(null);
    }

    private void UpdateValueFromPointer(Point position)
    {
        if (_trackGrid == null) return;

        var trackWidth = _trackGrid.Bounds.Width;
        if (trackWidth <= 0) return;

        // Position is relative to TrackArea, convert to TrackGrid coordinates
        var gridPosition = _trackArea?.TranslatePoint(position, _trackGrid) ?? position;
        var normalized = Math.Clamp(gridPosition.X / trackWidth, 0, 1);
        var range = Maximum - Minimum;
        var newValue = Minimum + (normalized * range);

        newValue = Math.Round(newValue / SmallChange) * SmallChange;
        newValue = Math.Clamp(newValue, Minimum, Maximum);

        Value = newValue;
    }
}
