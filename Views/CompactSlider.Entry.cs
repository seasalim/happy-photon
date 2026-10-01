using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace HappyPhoton.Views;

public partial class CompactSlider
{
    public static readonly StyledProperty<bool> IsValueEntryEnabledProperty =
        AvaloniaProperty.Register<CompactSlider, bool>(nameof(IsValueEntryEnabled), true);

    public static readonly DirectProperty<CompactSlider, bool> IsEditingValueProperty =
        AvaloniaProperty.RegisterDirect<CompactSlider, bool>(nameof(IsEditingValue), slider => slider.IsEditingValue);

    public static readonly StyledProperty<Func<double, double>?> ValueToDisplayProperty =
        AvaloniaProperty.Register<CompactSlider, Func<double, double>?>(nameof(ValueToDisplay));

    public static readonly StyledProperty<Func<double, double>?> DisplayToValueProperty =
        AvaloniaProperty.Register<CompactSlider, Func<double, double>?>(nameof(DisplayToValue));

    public static readonly StyledProperty<double> EntryStepProperty =
        AvaloniaProperty.Register<CompactSlider, double>(nameof(EntryStep));

    public bool IsValueEntryEnabled
    {
        get => GetValue(IsValueEntryEnabledProperty);
        set => SetValue(IsValueEntryEnabledProperty, value);
    }

    public Func<double, double>? ValueToDisplay
    {
        get => GetValue(ValueToDisplayProperty);
        set => SetValue(ValueToDisplayProperty, value);
    }

    public Func<double, double>? DisplayToValue
    {
        get => GetValue(DisplayToValueProperty);
        set => SetValue(DisplayToValueProperty, value);
    }

    public double EntryStep
    {
        get => GetValue(EntryStepProperty);
        set => SetValue(EntryStepProperty, value);
    }

    public bool IsEditingValue => _isEditingValue;

    internal static readonly RoutedEvent<RoutedEventArgs> ValueEntryStartedEvent =
        RoutedEvent.Register<CompactSlider, RoutedEventArgs>(nameof(ValueEntryStartedEvent), RoutingStrategies.Bubble);

    internal event Action? ValueEntryEnded;

    internal Func<object?>? EditIdentity { get; set; }

    private NumericEntryBox? _entry;

    private bool _isEditingValue;

    private bool _valuePressed;

    private bool _steppingEntry;

    private object? _entryIdentity;

    private Visual[] _entryAncestors = [];

    private TopLevel? _entryTopLevel;

    private void InitializeEntry()
    {
        _entry = this.FindControl<NumericEntryBox>("ValueEntry")!;
        _entry.AddHandler(KeyDownEvent, OnEntryKeyDown, RoutingStrategies.Tunnel);
        _entry.Finish = key => FinishValueEntry(key != Key.Escape, clearFocus: true);
        _entry.LostFocus += (_, _) => FinishValueEntry(commit: true);
        DetachedFromVisualTree += (_, _) => CancelValueEntry();
    }

    private void OnEntryPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        if (change.Property == DataContextProperty || change.Property == ValueProperty && !_steppingEntry ||
            change.Property == IsValueEntryEnabledProperty ||
            change.Property == IsEffectivelyEnabledProperty && !IsEffectivelyEnabled)
        {
            CancelValueEntry();
        }
    }

    private void OpenValueEntry()
    {
        if (_entry == null || !IsValueEntryEnabled || !IsEffectivelyEnabled) return;

        RaiseEvent(new RoutedEventArgs(ValueEntryStartedEvent));
        _entryIdentity = EditIdentity?.Invoke();
        _entryAncestors = this.GetVisualAncestors().Prepend(this).ToArray();

        foreach (var ancestor in _entryAncestors)
        {
            ancestor.PropertyChanged += EntryAncestorChanged;
        }

        _entry.Text = StripUnit(_valueText?.Text ??
            (ValueToDisplay?.Invoke(Value) ?? Value).ToString(CultureInfo.CurrentCulture));
        // Reserve the removed suffix and the readout's fractional layout rounding.
        var suffixLength = (_valueText?.Text?.Length ?? 0) - _entry.Text.Length;
        var suffixWidth = suffixLength > 0
            ? _valueText!.TextLayout.HitTestTextRange(_entry.Text.Length, suffixLength).Sum(rect => rect.Width) : 0;
        var rounding = (_valueText?.Bounds.Width ?? 0) - (_valueText?.TextLayout.Width ?? 0);
        _entry.Padding = new Thickness(2, 0, 2 + suffixWidth + rounding, 0);
        AutomationProperties.SetName(_entry, Label + " value");
        SetAndRaise(IsEditingValueProperty, ref _isEditingValue, true);
        _entry.Focus();
        _entry.SelectAll();
        _entryTopLevel = TopLevel.GetTopLevel(this);
        _entryTopLevel?.AddHandler(PointerPressedEvent, OnEntryOutsidePointerPressed,
            RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    private void OnEntryOutsidePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Source is Visual source &&
            (source == _entry || source.GetVisualAncestors().Contains(_entry!)))
            return;

        // Finish before the target control selects an item or snapshots a gesture.
        FinishValueEntry(commit: true);
    }

    private void EntryAncestorChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == IsVisibleProperty || e.Property == Expander.IsExpandedProperty)
        {
            if (!IsEffectivelyVisible || sender is Expander { IsExpanded: false }) CancelValueEntry();
        }
    }

    internal void CancelValueEntry() => FinishValueEntry(commit: false);

    private void OnEntryKeyDown(object? sender, KeyEventArgs e)
    {
        if (!IsEditingValue || _entry == null || e.Key is not (Key.Up or Key.Down or Key.Tab)) return;

        e.Handled = true;
        var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);

        if (e.Key == Key.Tab)
        {
            MoveToAdjacentValue(shift);

            return;
        }

        if (!TryParseEntry(_entry.Text, out var value)) value = ValueToDisplay?.Invoke(Value) ?? Value;

        var step = EntryStep > 0 ? EntryStep : SmallChange;
        value = NormalizeEntryValue(value + (e.Key == Key.Up ? 1 : -1) * step * (shift ? 10 : 1));
        if (!double.IsFinite(value)) return;

        CompleteWheel();
        RaiseEvent(new RoutedEventArgs(DragStartedEvent));
        _steppingEntry = true;

        try
        {
            SetCurrentValue(ValueProperty, value);
        }
        finally
        {
            _steppingEntry = false;
        }

        RaiseEvent(new RoutedEventArgs(DragCompletedEvent));
        _entry.Text = StripUnit(_valueText?.Text);
        _entry.SelectAll();
    }

    private void MoveToAdjacentValue(bool backwards)
    {
        FinishValueEntry(commit: true, clearFocus: true);
        var sliders = this.GetVisualAncestors().OfType<ScrollViewer>().FirstOrDefault()?
            .GetVisualDescendants().OfType<CompactSlider>().ToArray() ?? [];
        var current = Array.IndexOf(sliders, this);
        if (current < 0) return;

        var direction = backwards ? -1 : 1;

        for (var next = current + direction; next >= 0 && next < sliders.Length; next += direction)
        {
            var slider = sliders[next];
            if (!slider.IsEffectivelyVisible || !slider.IsEffectivelyEnabled || !slider.IsValueEntryEnabled) continue;

            slider.OpenValueEntry();
            slider.UpdateLayout();
            slider.BringIntoView();

            return;
        }
    }

    private static bool TryParseEntry(string? text, out double value) =>
        double.TryParse(StripUnit(text).Replace('−', '-'), NumberStyles.Float,
            CultureInfo.CurrentCulture, out value) && double.IsFinite(value);

    private double NormalizeEntryValue(double value)
    {
        var step = EntryStep > 0 ? EntryStep : SmallChange;
        value = Math.Round(value / step) * step;

        return BoundValue(DisplayToValue?.Invoke(value) ?? value);
    }

    private void FinishValueEntry(bool commit, bool clearFocus = false)
    {
        if (!IsEditingValue || _entry == null) return;

        _entryTopLevel?.RemoveHandler(PointerPressedEvent, OnEntryOutsidePointerPressed);
        _entryTopLevel = null;
        var text = _entry.Text;
        var validTarget = Equals(_entryIdentity, EditIdentity?.Invoke());
        SetAndRaise(IsEditingValueProperty, ref _isEditingValue, false);

        foreach (var ancestor in _entryAncestors)
        {
            ancestor.PropertyChanged -= EntryAncestorChanged;
        }

        _entryAncestors = [];
        ValueEntryEnded?.Invoke();

        if (commit && validTarget && IsEffectivelyEnabled && IsEffectivelyVisible &&
            TryParseEntry(text, out var value))
        {
            value = NormalizeEntryValue(value);

            if (double.IsFinite(value) && value != Value &&
                (ValueToDisplay == null || ValueToDisplay(value) != ValueToDisplay(Value)))
            {
                RaiseEvent(new RoutedEventArgs(DragStartedEvent));
                SetCurrentValue(ValueProperty, value);
                RaiseEvent(new ImmediateCompletedEventArgs());
            }
        }

        if (clearFocus) TopLevel.GetTopLevel(this)?.FocusManager?.Focus(null);
    }

    private static string StripUnit(string? text)
    {
        var result = text?.Trim() ?? "";

        foreach (var suffix in new[] { "EV", "K", "%", "°" })
        {
            if (result.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                return result[..^suffix.Length].TrimEnd();
        }

        return result;
    }

    private void StartValueDrag()
    {
        if (_valuePressed && !_hasDragStarted) RaiseEvent(new RoutedEventArgs(DragStartedEvent));
    }

    private void OnTrackPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_layoutGrid == null || _trackGrid == null || !IsEffectivelyEnabled) return;
        if (IsEditingValue && e.Source is Visual source &&
            (source == _entry || source.GetVisualAncestors().Contains(_entry!)))
            return;
        if (e.Pointer.Type == PointerType.Mouse &&
            !e.GetCurrentPoint(_layoutGrid).Properties.IsLeftButtonPressed)
            return;

        FinishValueEntry(commit: true);
        Focus();
        CompleteWheel();
        _valuePressed = IsValueEntryEnabled && e.GetPosition(_layoutGrid).X >=
            _layoutGrid.Bounds.Width - _layoutGrid.ColumnDefinitions[2].ActualWidth - 6;

        if (!_valuePressed && EnableDoubleClickReset && e.ClickCount == 2)
        {
            _isDragging = false;
            _hasDragStarted = false;
            _thumbMark?.Classes.Set("pointer-captured", false);
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
        _thumbMark?.Classes.Set(
            "pointer-captured",
            e.Pointer.Captured == _layoutGrid);
        if (!_valuePressed) RaiseEvent(new RoutedEventArgs(DragStartedEvent));
        e.Handled = true;
    }

    private void OnTrackPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_isDragging || _trackGrid == null) return;

        var pointerX = e.GetPosition(_trackGrid).X;
        if (!_hasDragStarted && Math.Abs(pointerX - _dragStartX) < DragThreshold) return;

        StartValueDrag();
        _hasDragStarted = true;
        UpdateValueFromDrag(pointerX);
        e.Handled = true;
    }

    private void OnTrackPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_isDragging) return;

        if (_trackGrid != null)
        {
            var pointerX = e.GetPosition(_trackGrid).X;

            if (_hasDragStarted || Math.Abs(pointerX - _dragStartX) >= DragThreshold)
            {
                StartValueDrag();
                _hasDragStarted = true;
                UpdateValueFromDrag(pointerX);
            }
        }

        var openEntry = _valuePressed && !_hasDragStarted;
        CompleteDrag();
        e.Pointer.Capture(null);
        if (openEntry) OpenValueEntry();
        e.Handled = true;
    }

    private void OnTrackPointerCaptureLost(
        object? sender,
        PointerCaptureLostEventArgs e)
    {
        CompleteDrag();
    }

    private void CompleteDrag()
    {
        if (!_isDragging) return;

        var wasDragged = _hasDragStarted;
        _isDragging = false;
        _hasDragStarted = false;
        _thumbMark?.Classes.Set("pointer-captured", false);
        if (!_valuePressed || wasDragged) RaiseEvent(new RoutedEventArgs(DragCompletedEvent));
        _valuePressed = false;

        if (wasDragged && IsFocused)
        {
            TopLevel.GetTopLevel(this)?.FocusManager?.Focus(null);
        }
    }

    private void UpdateValueFromDrag(double pointerX)
    {
        if (_trackGrid == null) return;

        var trackWidth = _trackGrid.Bounds.Width;
        if (trackWidth <= 0) return;

        var range = Maximum - Minimum;
        var newValue = _dragStartValue + ((pointerX - _dragStartX) / trackWidth * range);

        newValue = Math.Round(newValue / SmallChange) * SmallChange;
        newValue = BoundValue(newValue);

        Value = newValue;
    }
}
