using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using HappyPhoton.ViewModels;

namespace HappyPhoton.Tests;

// Observe synchronous notifications, including the initial exposed state of each new pane.
// Never split a continuous Develop interval merely because selection changes.
internal sealed class LoadingLabelObserver(string surface, string observedProperty) : IDisposable
{
    private readonly object _sync = new();
    private readonly long _origin = Stopwatch.GetTimestamp();
    private readonly List<Interval> _intervals = [];
    private readonly List<Activation> _activations = [];
    private readonly List<Step> _steps = [];
    private MainWindowViewModel? _vm;
    private INotifyPropertyChanged? _owner;
    private PropertyInfo? _property;
    private Interval? _current;
    private bool _visible;
    private int _step;
    private bool _disposed;
    private double Now => Stopwatch.GetElapsedTime(_origin).TotalMilliseconds;

    internal void Attach(MainWindowViewModel vm)
    {
        lock (_sync)
        {
            _vm = vm;
            vm.PropertyChanged += MainChanged;
            FollowOwner();
        }
    }

    internal void BeginStep(int step)
    {
        lock (_sync)
        {
            _step = step;
            _steps.Add(new(step, Now));
        }
    }

    private void MainChanged(object? sender, PropertyChangedEventArgs args)
    {
        lock (_sync)
        {
            if (_disposed) return;
            FollowOwner();
            Sample();
        }
    }

    private void FollowOwner()
    {
        INotifyPropertyChanged? next = surface == "loupe" ? _vm!.LoupePane : _vm;
        if (ReferenceEquals(next, _owner)) return;
        if (_owner != null && !ReferenceEquals(_owner, _vm)) _owner.PropertyChanged -= OwnerChanged;
        Close("owner-replaced");
        _owner = next;
        _visible = false;
        _property = next?.GetType().GetProperty(observedProperty)
            ?? (next == null ? null : throw new InvalidOperationException($"Missing label property {observedProperty}."));
        if (_property != null && _property.PropertyType != typeof(bool))
            throw new InvalidOperationException("The observed label property must be Boolean.");
        if (_owner != null && !ReferenceEquals(_owner, _vm)) _owner.PropertyChanged += OwnerChanged;
        Sample();
    }

    private void OwnerChanged(object? sender, PropertyChangedEventArgs args)
    {
        lock (_sync) if (!_disposed && ReferenceEquals(sender, _owner)) Sample();
    }

    private void Sample()
    {
        if (_owner == null) return;
        var condition = _owner is ComparePaneViewModel pane
            ? pane.ShowLoadingMessage : _vm!.ShowDevelopLoadingMessage;
        var now = Now;
        if (condition && _current == null)
        {
            _current = new(_intervals.Count + 1, _step, now);
            _intervals.Add(_current);
        }
        var visible = (bool)_property!.GetValue(_owner)!;
        if (visible && !_visible)
            _activations.Add(new(_step, now, _current?.Id));
        _visible = visible;
        if (!condition) Close("condition-false");
    }

    private void Close(string reason)
    {
        if (_current == null) return;
        _current.EndMs = Now;
        _current.EndStep = _step;
        _current.EndReason = reason;
        _current = null;
    }

    internal int ActivationCount { get { lock (_sync) return _activations.Count(a => a.Step > 0); } }
    internal bool HasOpenInterval { get { lock (_sync) return _current != null; } }

    internal Report Snapshot()
    {
        lock (_sync)
        {
            var intervals = _intervals.Where(i => i.StartStep > 0).ToArray();
            var complete = intervals.Where(i => i.EndReason == "condition-false").ToArray();
            var durations = complete.Select(i => i.DurationMs!.Value).Order().ToArray();
            var activations = _activations.Where(a => a.Step > 0).ToArray();
            int Count(Func<Interval, bool> predicate) => complete.Where(predicate)
                .Sum(i => activations.Count(a => a.IntervalId == i.Id));
            return new(observedProperty, _steps.ToArray(), intervals, activations,
                new(durations.Length, durations.FirstOrDefault(), Median(durations),
                    durations.Length == 0 ? 0 : durations[(int)Math.Ceiling(durations.Length * .95) - 1],
                    durations.LastOrDefault(), durations.Count(d => d < 300), durations.Count(d => d >= 320)),
                Count(i => i.DurationMs < 300), Count(i => i.DurationMs >= 320),
                intervals.Count(i => i.EndReason != "condition-false"),
                activations.Count(a => a.IntervalId == null));
        }
    }

    private static double Median(double[] values) => values.Length == 0 ? 0 :
        (values[(values.Length - 1) / 2] + values[values.Length / 2]) / 2;

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            Sample();
            Close("observation-ended");
            if (_vm != null) _vm.PropertyChanged -= MainChanged;
            if (_owner != null && !ReferenceEquals(_owner, _vm)) _owner.PropertyChanged -= OwnerChanged;
            _disposed = true;
        }
    }

    internal sealed record Step(int Number, double StartMs);
    internal sealed record Activation(int Step, double AtMs, int? IntervalId);
    internal sealed record Distribution(int Count, double MinMs, double MedianMs, double P95Ms,
        double MaxMs, int CountUnder300Ms, int CountAtLeast320Ms);
    internal sealed record Report(string ObservedProperty, Step[] Steps, Interval[] Intervals,
        Activation[] Activations, Distribution Distribution, int ActivationsUnder300Ms,
        int ActivationsAtLeast320Ms, int IncompleteIntervals, int UncorrelatedActivations);
    internal sealed record Interval(int Id, int StartStep, double StartMs)
    {
        public int? EndStep { get; set; }
        public double? EndMs { get; set; }
        public double? DurationMs => EndMs - StartMs;
        public string? EndReason { get; set; }
    }
}
