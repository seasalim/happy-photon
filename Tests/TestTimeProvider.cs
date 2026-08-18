namespace HappyPhoton.Tests;

/// <summary>
/// A clock that only moves when a test moves it. Production delays scheduled
/// against it fire on <see cref="Advance"/> and never on wall-clock time, so a
/// stalled CI runner can neither shorten nor lengthen a hold under test.
/// </summary>
internal sealed class TestTimeProvider : TimeProvider
{
    private readonly object _sync = new();
    private readonly List<ScheduledTimer> _timers = [];
    private DateTimeOffset _now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow()
    {
        lock (_sync) return _now;
    }

    public override ITimer CreateTimer(
        TimerCallback callback,
        object? state,
        TimeSpan dueTime,
        TimeSpan period)
    {
        var timer = new ScheduledTimer(this, callback, state);
        lock (_sync) _timers.Add(timer);
        timer.Change(dueTime, period);
        return timer;
    }

    /// <summary>
    /// Moves the clock forward, firing every timer that comes due on the way in
    /// chronological order. Timers created by a callback are picked up in the
    /// same sweep, so a chained delay behaves as it would in real time.
    /// </summary>
    public void Advance(TimeSpan amount)
    {
        var target = GetUtcNow() + amount;
        while (true)
        {
            ScheduledTimer due;
            lock (_sync)
            {
                var next = NextDue(target);
                if (next == null)
                {
                    _now = target;
                    return;
                }

                due = next;
                _now = due.DueAt!.Value;
                due.DueAt = due.Period > TimeSpan.Zero
                    ? _now + due.Period
                    : null;
            }

            due.Fire();
        }
    }

    private ScheduledTimer? NextDue(DateTimeOffset target)
    {
        ScheduledTimer? next = null;
        foreach (var timer in _timers)
        {
            if (timer.DueAt is not { } due || due > target) continue;
            if (next == null || due < next.DueAt) next = timer;
        }
        return next;
    }

    private void Forget(ScheduledTimer timer)
    {
        lock (_sync) _timers.Remove(timer);
    }

    private sealed class ScheduledTimer(
        TestTimeProvider owner,
        TimerCallback callback,
        object? state) : ITimer
    {
        public DateTimeOffset? DueAt { get; set; }

        public TimeSpan Period { get; private set; }

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            lock (owner._sync)
            {
                DueAt = dueTime == Timeout.InfiniteTimeSpan
                    ? null
                    : owner._now + dueTime;
                Period = period == Timeout.InfiniteTimeSpan
                    ? TimeSpan.Zero
                    : period;
            }
            return true;
        }

        public void Fire() => callback(state);

        public void Dispose() => owner.Forget(this);

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
