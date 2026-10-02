using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using ThumbMark = Avalonia.Controls.Shapes.Path;

namespace HappyPhoton.Views;

public sealed class DualRangeTrack : UserControl
{
    public static readonly StyledProperty<double> LowerProperty = AvaloniaProperty.Register<DualRangeTrack, double>(
        nameof(Lower), defaultBindingMode: BindingMode.TwoWay);
    public static readonly StyledProperty<double> UpperProperty = AvaloniaProperty.Register<DualRangeTrack, double>(
        nameof(Upper), 100, defaultBindingMode: BindingMode.TwoWay);
    public static readonly StyledProperty<object?> EditIdentityProperty = AvaloniaProperty.Register<DualRangeTrack, object?>(nameof(EditIdentity));
    public object? EditIdentity { get => GetValue(EditIdentityProperty); set => SetValue(EditIdentityProperty, value); }
    public double Lower { get => GetValue(LowerProperty); set => SetValue(LowerProperty, value); }
    public double Upper { get => GetValue(UpperProperty); set => SetValue(UpperProperty, value); }
    private readonly Canvas _track = new() { Height = 24, Background = Brushes.Transparent };
    private readonly Border _line = new() { Height = 8, IsHitTestVisible = false };
    private readonly Border _selection = new() { Height = 2, IsHitTestVisible = false };

    private readonly ThumbMark[] _thumbs = [new(), new()];

    private readonly TextBox[] _entries = new TextBox[2];
    private int? _drag;

    private bool _isDragging;

    private double _pressX;

    private double _startValue;

    public DualRangeTrack()
    {
        var panel = new StackPanel { Spacing = 4 };
        var numbers = new Grid { ColumnDefinitions = new("Auto,*,Auto") };
        numbers.ColumnDefinitions[1].MinWidth = 12;
        _line.Bind(Border.BackgroundProperty, this.GetResourceObservable("LuminanceTrack"));
        _track.Children.Add(_line);
        _selection.Bind(Border.BackgroundProperty, this.GetResourceObservable("ControlActive"));
        _track.Children.Add(_selection);

        for (var i = 0; i < 2; i++)
        {
            var index = i;
            var name = i == 0 ? "Luminance lower limit" : "Luminance upper limit";
            var thumb = _thumbs[i];
            thumb.Focusable = true;
            thumb.Bind(ThemeProperty, this.GetResourceObservable("SliderThumbTheme"));
            AutomationProperties.SetName(thumb, name);
            AutomationProperties.SetControlTypeOverride(thumb, Avalonia.Automation.Peers.AutomationControlType.Slider);
            AutomationProperties.SetIsControlElementOverride(thumb, true);
            thumb.AddHandler(KeyDownEvent, (_, e) => Step(index, e), RoutingStrategies.Tunnel);
            thumb.GotFocus += (_, _) => UpdateThumbOrder();
            thumb.LostFocus += (_, _) => UpdateThumbOrder();
            _track.Children.Add(thumb);
            var entry = _entries[i] = new NumericEntryBox
            {
                Finish = key =>
                {
                    if (key == Key.Enter) Commit(index);
                    else Update();

                    _thumbs[index].Focus(NavigationMethod.Tab);
                }
            };
            entry.Classes.Add("edit-field");
            entry.Bind(FontSizeProperty, this.GetResourceObservable("FontSizeBody"));
            entry.Bind(FontFamilyProperty, this.GetResourceObservable("FontLabel"));
            entry.MinWidth = 0;
            entry.Width = 48;
            entry.MinHeight = 24;
            entry.Height = 24;
            entry.Padding = new Thickness(5, 2); entry.TextAlignment = TextAlignment.Right;
            AutomationProperties.SetName(entry, name + " numeric entry");
            var label = new TextBlock
            {
                Text = i == 0 ? "Lower" : "Upper",
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
            };
            label.Bind(TextBlock.FontSizeProperty, this.GetResourceObservable("FontSizeSmall"));
            label.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("TextMuted"));
            var endpoint = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 5 };
            endpoint.Children.Add(label); endpoint.Children.Add(entry);
            Grid.SetColumn(endpoint, i * 2); numbers.Children.Add(endpoint);
            entry.LostFocus += (_, _) => Commit(index);
        }

        PointerEntered += (_, _) => SetHover(true);
        PointerExited += (_, _) => SetHover(false);
        panel.Children.Add(_track);
        panel.Children.Add(numbers);
        Content = panel;
        _track.SizeChanged += (_, _) => Update();
        _track.PointerPressed += Start;
        _track.PointerMoved += Move;
        _track.PointerReleased += (_, e) => { Complete(); e.Pointer.Capture(null); e.Handled = true; };
        _track.PointerCaptureLost += (_, _) => Complete();
        Update();
    }

    private void SetHover(bool hovered)
    {
        foreach (var thumb in _thumbs)
        {
            thumb.Classes.Set("rowHover", hovered);
        }
    }

    private void Start(object? sender, PointerPressedEventArgs e)
    {
        if (!IsEffectivelyEnabled || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

        // Either entry can own a draft; commit it before picking or snapshotting the range.
        for (var i = 0; i < _entries.Length; i++)
        {
            if (_entries[i].IsFocused) Commit(i);
        }

        _pressX = e.GetPosition(_track).X;
        _drag = Lower == Upper ? null :
            Math.Abs(_pressX - ValueX(Lower)) <= Math.Abs(_pressX - ValueX(Upper)) ? 0 : 1;
        _isDragging = true;
        _startValue = _drag == 1 ? Upper : Lower;

        if (_drag is { } index)
        {
            _thumbs[index].Focus();
            _thumbs[index].Classes.Set("pointer-captured", true);
        }

        e.Pointer.Capture(_track);
        UpdateThumbOrder();
        RaiseEvent(new RoutedEventArgs(CompactSlider.DragStartedEvent));
        e.Handled = true;
    }

    private void Move(object? sender, PointerEventArgs e)
    {
        if (!_isDragging) return;

        var delta = e.GetPosition(_track).X - _pressX;
        if (_drag == null && Math.Abs(delta) < 2) return;

        if (_drag == null)
        {
            _drag = delta < 0 ? 0 : 1;
            _thumbs[_drag.Value].Focus();
            _thumbs[_drag.Value].Classes.Set("pointer-captured", true);
            UpdateThumbOrder();
        }

        Set(_drag.Value, _startValue + delta / Math.Max(1, _line.Width) * 100);
        e.Handled = true;
    }

    private void Complete()
    {
        if (!_isDragging) return;

        _isDragging = false;

        if (_drag is { } index)
        {
            _thumbs[index].Classes.Set("pointer-captured", false);
        }

        _drag = null;
        UpdateThumbOrder();
        RaiseEvent(new RoutedEventArgs(CompactSlider.DragCompletedEvent));
    }

    private void UpdateThumbOrder()
    {
        for (var i = 0; i < _thumbs.Length; i++)
        {
            _thumbs[i].ZIndex = _isDragging && _drag == i ? 2 : _thumbs[i].IsFocused ? 1 : 0;
        }
    }

    private void Step(int index, KeyEventArgs e)
    {
        if (!IsEffectivelyEnabled || e.Key is not (Key.Left or Key.Right or Key.Up or Key.Down)) return;

        RaiseEvent(new RoutedEventArgs(CompactSlider.DragStartedEvent));
        Set(index, (index == 0 ? Lower : Upper) + (e.Key is Key.Right or Key.Up ? 1 : -1) *
            (e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? 10 : 1));
        RaiseEvent(new RoutedEventArgs(CompactSlider.DragCompletedEvent)); e.Handled = true;
    }
    private void Commit(int index)
    {
        if (!IsEffectivelyEnabled || _entries[index].Text ==
            (index == 0 ? Lower : Upper).ToString("0.#", CultureInfo.CurrentCulture)) return;
        if (double.TryParse(_entries[index].Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var value) && double.IsFinite(value) &&
            value != (index == 0 ? Lower : Upper))
        {
            RaiseEvent(new RoutedEventArgs(CompactSlider.DragStartedEvent)); Set(index, value);
            RaiseEvent(new RoutedEventArgs(CompactSlider.DragCompletedEvent));
        }
        Update();
    }
    private void Set(int index, double value) => SetCurrentValue(index == 0 ? LowerProperty : UpperProperty,
        index == 0 ? Math.Clamp(value, 0, Upper) : Math.Clamp(value, Lower, 100));
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == EditIdentityProperty) { Complete(); Update(); }
        if (change.Property == LowerProperty || change.Property == UpperProperty) Update();
    }

    private double ValueX(double value) => 6 + value / 100 * Math.Max(0, _track.Bounds.Width - 12);

    private void Update()
    {
        _line.Width = Math.Max(0, _track.Bounds.Width - 12);
        Canvas.SetLeft(_line, 6);
        Canvas.SetTop(_line, 8);
        _selection.Width = Math.Max(0, (Upper - Lower) / 100 * _line.Width);
        Canvas.SetLeft(_selection, ValueX(Lower));
        Canvas.SetTop(_selection, 17);

        for (var i = 0; i < 2; i++)
        {
            var value = i == 0 ? Lower : Upper;
            Canvas.SetLeft(_thumbs[i], ValueX(value) - 3.5);
            Canvas.SetTop(_thumbs[i], 19);
            _entries[i].Text = value.ToString("0.#", CultureInfo.CurrentCulture);
        }
    }
}
