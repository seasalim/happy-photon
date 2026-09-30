using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace HappyPhoton.Views;

public partial class CompactSlider
{
    private ITimer? _wheelTimer;

    private TopLevel? _wheelRoot;

    private double _wheelRemainder;

    private int _wheelGeneration;

    private bool _wheelStarted;

    internal static readonly RoutedEvent<RoutedEventArgs> WheelInputStartedEvent =
        RoutedEvent.Register<CompactSlider, RoutedEventArgs>(nameof(WheelInputStartedEvent), RoutingStrategies.Bubble);

    internal event Action? WheelInputEnded;

    internal bool IsWheelEditing => _wheelStarted;

    internal TimeProvider WheelTimeProvider { get; set; } = TimeProvider.System;

    internal sealed class ImmediateCompletedEventArgs() : RoutedEventArgs(DragCompletedEvent);

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (!IsEffectivelyEnabled || !e.KeyModifiers.HasFlag(KeyModifiers.Shift)) return;

        FinishValueEntry(commit: true, clearFocus: true);
        var delta = e.Delta.Y != 0 ? e.Delta.Y : e.Delta.X;
        if (delta == 0 || _isDragging) return;

        if (_wheelTimer == null)
        {
            RaiseEvent(new RoutedEventArgs(WheelInputStartedEvent));
            _wheelRoot = TopLevel.GetTopLevel(this);
            _wheelRoot?.AddHandler(KeyUpEvent, OnWheelKeyUp, RoutingStrategies.Bubble, handledEventsToo: true);
        }

        _wheelRemainder += delta;
        // Ignore floating-point accumulation noise at whole-notch boundaries.
        var steps = Math.Truncate(Math.Round(_wheelRemainder, 10));
        _wheelRemainder -= steps;

        if (steps != 0)
        {
            if (!_wheelStarted)
            {
                _wheelStarted = true;
                RaiseEvent(new RoutedEventArgs(DragStartedEvent));
            }

            var maximum = WrapValue ? Math.Max(Minimum, Maximum - SmallChange) : Maximum;
            SetCurrentValue(ValueProperty, Math.Clamp(Value + steps * SmallChange, Minimum, maximum));
        }

        var generation = ++_wheelGeneration;
        _wheelTimer?.Dispose();
        _wheelTimer = WheelTimeProvider.CreateTimer(_ => Dispatcher.UIThread.Post(() =>
        {
            if (generation == _wheelGeneration) CompleteWheel();
        }), null, TimeSpan.FromMilliseconds(300), Timeout.InfiniteTimeSpan);
        e.Handled = true;
    }

    private void OnWheelKeyUp(object? sender, KeyEventArgs e)
    {
        if (e.Key is Key.LeftShift or Key.RightShift) CompleteWheel();
    }

    internal void CompleteWheel()
    {
        ++_wheelGeneration;
        _wheelTimer?.Dispose();
        _wheelTimer = null;
        _wheelRoot?.RemoveHandler(KeyUpEvent, OnWheelKeyUp);
        _wheelRoot = null;
        _wheelRemainder = 0;

        var started = _wheelStarted;
        _wheelStarted = false;
        if (started) RaiseEvent(new ImmediateCompletedEventArgs());
        WheelInputEnded?.Invoke();
    }
}
