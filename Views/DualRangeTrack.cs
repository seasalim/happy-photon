using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Input;

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
    private readonly Border _line = new() { Height = 8, CornerRadius = new(2), IsHitTestVisible = false };
    private readonly Border _selection = new() { Height = 2, IsHitTestVisible = false };
    private readonly Button[] _thumbs = [new(), new()];
    private readonly TextBox[] _entries = new TextBox[2];
    private int? _drag;

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
            thumb.Width = 12; thumb.Height = 18; thumb.Padding = default; thumb.MinWidth = 0; thumb.MinHeight = 0;
            AutomationProperties.SetName(thumb, name);
            thumb.Bind(BackgroundProperty, this.GetResourceObservable("TextMuted"));
            thumb.AddHandler(PointerPressedEvent, (_, e) => Start(index, e), RoutingStrategies.Tunnel);
            thumb.AddHandler(KeyDownEvent, (_, e) => Step(index, e), RoutingStrategies.Tunnel);
            _track.Children.Add(thumb);
            var entry = _entries[i] = new EndpointEntry(key =>
            {
                if (key == Key.Enter) Commit(index);
                else Update();
                _thumbs[index].Focus();
            });
            entry.Classes.Add("edit-field");
            entry.FontSize = 11; entry.MinWidth = 0; entry.Width = 48; entry.MinHeight = 24; entry.Height = 24;
            entry.Padding = new Thickness(5, 2); entry.TextAlignment = TextAlignment.Right;
            AutomationProperties.SetName(entry, name + " numeric entry");
            var label = new TextBlock { Text = i == 0 ? "Lower" : "Upper", FontSize = 10,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
            label.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("TextMuted"));
            var endpoint = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 5 };
            endpoint.Children.Add(label); endpoint.Children.Add(entry);
            Grid.SetColumn(endpoint, i * 2); numbers.Children.Add(endpoint);
            entry.LostFocus += (_, _) => Commit(index);
        }
        panel.Children.Add(_track); panel.Children.Add(numbers); Content = panel;
        _track.SizeChanged += (_, _) => Update();
        _track.PointerMoved += (_, e) => { if (_drag is { } i) Set(i, (e.GetPosition(_track).X - 6) / Math.Max(1, _track.Bounds.Width - 12) * 100); };
        _track.PointerReleased += (_, e) => { Complete(); e.Pointer.Capture(null); e.Handled = true; };
        _track.PointerCaptureLost += (_, _) => Complete();
        Update();
    }

    private void Start(int index, PointerPressedEventArgs e)
    {
        if (!IsEffectivelyEnabled || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        _thumbs[index].Focus(); _drag = index; e.Pointer.Capture(_track);
        RaiseEvent(new RoutedEventArgs(CompactSlider.DragStartedEvent)); e.Handled = true;
    }
    private void Complete()
    {
        if (_drag == null) return;
        _drag = null; RaiseEvent(new RoutedEventArgs(CompactSlider.DragCompletedEvent));
    }
    private void Step(int index, KeyEventArgs e)
    {
        if (e.Key is not (Key.Left or Key.Right or Key.Up or Key.Down)) return;
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
    private void Update()
    {
        _line.Width = Math.Max(0, _track.Bounds.Width - 12); Canvas.SetLeft(_line, 6); Canvas.SetTop(_line, 8);
        _selection.Width = Math.Max(0, (Upper - Lower) / 100 * _line.Width);
        Canvas.SetLeft(_selection, 6 + Lower / 100 * _line.Width); Canvas.SetTop(_selection, 17);
        for (var i = 0; i < 2; i++)
        {
            var value = i == 0 ? Lower : Upper;
            Canvas.SetLeft(_thumbs[i], value / 100 * Math.Max(0, _track.Bounds.Width - 12)); Canvas.SetTop(_thumbs[i], 3);
            _entries[i].Text = value.ToString("0.#", CultureInfo.CurrentCulture);
        }
    }

    // Avalonia checks bindings from the focused element outward before routed KeyDown.
    // Shadow ancestor gestures only in this entry and send them through native text editing.
    private sealed class EndpointEntry(Action<Key> finish) : TextBox
    {
        protected override Type StyleKeyOverride => typeof(TextBox);

        protected override void OnGotFocus(FocusChangedEventArgs e)
        {
            base.OnGotFocus(e);
            KeyBindings.Clear();
            var gestures = this.GetVisualAncestors().OfType<InputElement>()
                .SelectMany(element => element.KeyBindings).Select(binding => binding.Gesture)
                .OfType<KeyGesture>().Concat([new(Key.Enter), new(Key.Escape)]).Distinct();
            foreach (var gesture in gestures)
                KeyBindings.Add(new KeyBinding
                {
                    Gesture = gesture,
                    Command = new RelayCommand(() => OnKeyDown(new KeyEventArgs
                    {
                        RoutedEvent = KeyDownEvent, Source = this,
                        Key = gesture.Key, KeyModifiers = gesture.KeyModifiers
                    }))
                });
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.Key is Key.Enter or Key.Escape) finish(e.Key);
            else base.OnKeyDown(e);
            if (e.Key != Key.Tab) e.Handled = true;
        }
    }
}
