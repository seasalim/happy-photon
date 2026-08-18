# Waiting in tests

Four CI failures have now come from the same defect: a test that slept a fixed
number of milliseconds and then asserted on state that a production timer was
about to change. On a dev machine the sleep lands where the author expected; on
a loaded runner it lands past the timer and the assertion sees the next state.
Reruns pass, so the failure reads as noise and costs a diagnosis every time.

## The rule

A timeout bounds a hang. It never asserts latency, and it is never the thing
that makes an assertion true.

## The three shapes that are allowed

**Inject the clock.** When the behavior under test *is* a duration — a hold, a
fade, a debounce — the view model takes a `TimeProvider` (defaulting to
`TimeProvider.System`) and the test passes `TestTimeProvider`. Nothing fires
until the test advances it, so "still visible 100 ms before the hold ends" is
true no matter how long the machine takes to reach the assertion, and the test
finishes in milliseconds instead of seconds.

**Wait on a signal.** A `TaskCompletionSource`, `ManualResetEventSlim`, or an
event the production code raises. Give it `TestWaits.Condition` as its ceiling.

**Poll an observed condition.** `TestWaits.UntilAsync` / `TestWaits.Until` when
no signal exists. Same ceiling; the wait ends on the state change itself.

`TestWaits.Condition` is 30 s — generous enough for a stalled runner, and well
under the 90 s `--blame-hang-timeout` so a real hang still reports as a hang.
Tune it in one place; do not add a per-call-site value.

## The shape that is not allowed

Sleeping less than a production timeout and asserting the state has not changed
yet. Widening the sleep does not fix it — it moves the cliff. Inject the clock
instead.

## The residual exception

Settling before asserting that something did *not* happen — no event raised, no
second load started, a superseded timer dropped — still uses a short
`Task.Delay`. There is no signal for an absence, and the failure mode runs the
safe way: a slow runner only widens the window in which the unwanted event would
have had to appear. Each such site carries a comment saying so. If one is ever
converted, the deterministic form is to wait for a *later* signal that must
arrive and assert the earlier one never did.

`CatalogImportServiceTests` bounds a 50k-row import at 30 s. That one is a
deliberate performance guard, not a wait, and stays as it is.
