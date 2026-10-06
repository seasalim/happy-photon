using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace HappyPhoton.Views;

public partial class CompactSlider : UserControl
{
    public static readonly RoutedEvent<RoutedEventArgs> DragStartedEvent =
        RoutedEvent.Register<CompactSlider, RoutedEventArgs>(
            nameof(DragStarted), RoutingStrategies.Bubble);

    public static readonly RoutedEvent<RoutedEventArgs> DragCompletedEvent =
        RoutedEvent.Register<CompactSlider, RoutedEventArgs>(
            nameof(DragCompleted), RoutingStrategies.Bubble);

    public event EventHandler<RoutedEventArgs> DragStarted
    {
        add => AddHandler(DragStartedEvent, value);
        remove => RemoveHandler(DragStartedEvent, value);
    }

    public event EventHandler<RoutedEventArgs> DragCompleted
    {
        add => AddHandler(DragCompletedEvent, value);
        remove => RemoveHandler(DragCompletedEvent, value);
    }

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

    public static readonly StyledProperty<bool> WrapValueProperty =
        AvaloniaProperty.Register<CompactSlider, bool>(nameof(WrapValue));

    public bool WrapValue
    {
        get => GetValue(WrapValueProperty);
        set => SetValue(WrapValueProperty, value);
    }

    public static readonly StyledProperty<double> DefaultValueProperty =
        AvaloniaProperty.Register<CompactSlider, double>(nameof(DefaultValue), 0.0);

    public static readonly StyledProperty<bool> EnableDoubleClickResetProperty =
        AvaloniaProperty.Register<CompactSlider, bool>(nameof(EnableDoubleClickReset), false);

    public static readonly StyledProperty<IBrush?> TrackBrushProperty =
        AvaloniaProperty.Register<CompactSlider, IBrush?>(nameof(TrackBrush));

    public static readonly StyledProperty<bool> ShowValueFillProperty =
        AvaloniaProperty.Register<CompactSlider, bool>(nameof(ShowValueFill), false);

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

    private Avalonia.Controls.Shapes.Path? _thumbMark;

    private bool _isDragging;
    private bool _hasDragStarted;
    private double _dragStartX;
    private double _dragStartValue;

    public CompactSlider()
    {
        InitializeComponent();
        Focusable = true;
        PointerExited += (_, _) => CompleteWheel();
        DetachedFromVisualTree += (_, _) => CompleteWheel();
        AttachedToVisualTree += (_, _) =>
        {
            if (IsInline) UpdateInlineColumns();
        };

        _layoutGrid = this.FindControl<Grid>("LayoutGrid");
        _trackGrid = this.FindControl<Grid>("TrackGrid");
        _fillBar = this.FindControl<Border>("FillBar");
        _centerMark = this.FindControl<Border>("CenterMark");
        _labelText = this.FindControl<TextBlock>("LabelText");
        _valueText = this.FindControl<TextBlock>("ValueText");
        _thumbMark = this.FindControl<Avalonia.Controls.Shapes.Path>("ThumbMark");
        GotFocus += (_, e) => _thumbMark?.Classes.Set("keyboardFocused",
            e.Source == this && e.NavigationMethod is NavigationMethod.Tab or NavigationMethod.Directional);
        LostFocus += (_, _) => _thumbMark?.Classes.Set("keyboardFocused", false);

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

        InitializeEntry();
        UpdateDisplay();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (IsEffectivelyEnabled && e.Key is Key.Left or Key.Right or Key.Down or Key.Up)
        {
            CompleteWheel();
            var direction = e.Key is Key.Right or Key.Up ? 1 : -1;
            var step = SmallChange * (e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? 10 : 1);
            RaiseEvent(new RoutedEventArgs(DragStartedEvent));
            SetCurrentValue(ValueProperty, BoundValue(Value + direction * step));
            RaiseEvent(new RoutedEventArgs(DragCompletedEvent));
            e.Handled = true;
        }
        base.OnKeyDown(e);
    }

    private double BoundValue(double value) => WrapValue && Maximum > Minimum
        ? Minimum + ((value - Minimum) % (Maximum - Minimum) + Maximum - Minimum) % (Maximum - Minimum)
        : Math.Clamp(value, Minimum, Maximum);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        OnEntryPropertyChanged(change);
        OnInlinePropertyChanged(change);

        if (change.Property == IsEffectivelyEnabledProperty && !IsEffectivelyEnabled)
            CompleteWheel();

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

        var normalizedValue = Math.Clamp((Value - Minimum) / range, 0, 1);
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

        if (_thumbMark != null)
        {
            _thumbMark.Margin = new Thickness(valueX - _thumbMark.Width / 2, 6, 0, 0);
        }
    }
}
