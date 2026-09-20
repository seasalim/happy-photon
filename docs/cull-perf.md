# Culling performance evidence

Run from the repository root with PowerShell 7 and the .NET 10 SDK:

```powershell
./scripts/cull-perf.ps1
./scripts/cull-perf.ps1 -Baseline artifacts/cull-perf/<attempt>/result.json
```

Each attempt retains an exact `gates.json` copy. The script builds Release once and runs each required case in a fresh, full-CPU
process using `HappyPhoton.FullCpu.runsettings`. It retains every attempt under
`artifacts/cull-perf/<UTC>-<commit>-<unique-id>/`. Exit codes are 0 pass, 1 fail,
and 2 inconclusive. A diagnostic `-Case <workload-id>` runs a subset and remains
inconclusive; `-NoBuild` is diagnostic-only: it records `buildVerified: false`,
a build check explaining that binaries may differ from HEAD, and always returns
inconclusive (exit 2). Full qualification always builds.
The full invocation's elapsed seconds are recorded for the 30-minute FINALIZE
budget. The implementor's targeted runs do not certify that full-run budget.

## Supplemental dwell workloads

Use `./scripts/cull-perf.ps1 -GateFile Tests/CullPerfGates.dwell.json`, adding
`-Baseline <dwell-attempt>/result.json` to compare within this supplemental series.
Relative gate paths resolve from the repository root. The selected file is copied
and hashed independently; the default frozen table is unchanged.

Dwell takes 100 steady steps at 1500 ms intervals on each of Loupe and Develop,
using the Canon fixture, default settings, and an empty application cache.
Adjacent warming stays on. Develop requires an initial fresh preview and a fresh
publication per step, so a rendered preview departs each time. Loupe accepts a
matched warm paint per step. A missed completion fails the workload. Cadence
remains independent of completion; the final step must also publish.
`dwell-fresh-before-next-input` reports the number of validated steps.
Each dwell case has a four-minute hang watchdog.

The three foreground/publication metric definitions are copied from the frozen
table unchanged. `ui-enqueue-ms` adds caller-thread `CacheEnqueueStart/End` spans,
including snapshot preparation, correlated by operation id and restricted to the
thread that received that input. Caller `BaseRetireStart/End` spans bracket base detachment and worker scheduling;
`BaseDisposeStart/End` spans cover retirement on the worker. Worker `CacheDecodeStart/End` spans bracket the
cached/warm reads and promotion. `events-CacheConvert`, `events-CacheWarmDecode`,
`events-CacheJoinDecode`, and `events-CacheThumbnailResize` report exercised paths.
The dispatcher-backed `CacheOffThreadTests` proves placement with positive path
counts; workload thread ids alone do not prove dispatcher placement. Adding this
attribution metric changes only the supplemental file's hash; compare the earlier
instrumented-base feedback samples directly, preserving their original gate file.
In the existing ledger their spans start at submission immediately before command
invocation, not at the recorder's Receipt event. These view-model measurements
exclude dispatcher queue delay. This supplemental run does not qualify the frozen
workloads or repeat their required correctness checks.

## Frozen workloads

`Tests/CullPerfGates.json` is the authoritative, SHA-256 identified table. It
specifies every input count, interval, burst, reversal, exposure, cache condition,
minimum sample count, statistic, threshold, and foreground comparison metric.
The table is stratified: every applicable surface/pattern/fixture combination
and every surface/fixture/cache combination occurs. Both settings variants occur
for each surface/fixture; it is not a Cartesian expansion of all dimensions.
Fullscreen only exercises navigation, consistent with its production guards.

Preparation generates a 6000×4000 JPEG and hashes it and the repository RAW and
legacy fixtures. A persistent hash-keyed root contains 627 JPEG/RAW pairs,
alternating Canon and Fuji RAW members. Private source anchors avoid exhausting
NTFS's hard-link limit across fixture revisions. Hard-link failures produce
inconclusive evidence, never a smaller workload. Sources are never written.
A pristine catalog is built once beside the fixture root; each workload copies it
and creates its own rendered cache. Thumbnail-only cases use
resident synthetic thumbnails; matched/stale cases copy an actual rendered
entry across identical hard-linked sources. Stale cases then apply the frozen
edit delta. Cold means an empty application cache; **OS cache warmth is
uncontrolled**. Ahead cases explicitly admit the normal warm worker two images
ahead and require the first foreground input to overlap its actual decode.

The workloads in `Tests/` drive the view model without a UI dispatcher, following
the same harness shape as `AdjacentPreviewPerformanceTests`. Their timing spans
exclude UI-queue delay. The window-loupe scene is the dispatcher-level check;
moving all workloads onto a dedicated dispatcher is a follow-up candidate.

Input due times are independent of preview completion. A ledger records actual
submission before command invocation. Receipt and selection/assessment feedback
are separate samples. Cached/fresh bitmap publication is recorded at the sink;
it is not a displayed frame. The window check captures a blocked-source Loupe
resident-thumbnail placeholder and then a preview after a headless render tick, and probes a posted
background-priority dispatcher callback. It uses a signal-gated synthetic loader
and a stopped test clock for scheduling correctness, not native decode timing.

## Result schema, version 1

`result.json` contains:

| Field | Meaning |
|---|---|
| `gateId`, `gateHash` | Frozen table identity and exact file SHA-256 |
| `candidateCommit`, `candidateDirty` | HEAD and whether the source tree differs |
| `buildVerified` | True only after a successful Release build in this attempt; false cannot qualify |
| `baselineCommit`, `baselineResult` | Named baseline commit, path and result hash |
| `machine`, `runtime`, `configuration` | Host/CPU/OS architecture, `dotnet --info`, Release |
| `stopwatchFrequency`, `processEnvironment` | Tick conversion and inherited CPU/OpenMP settings |
| `osCacheWarmth` | Always `uncontrolled`; no OS-cache eviction is attempted |
| `fragments` | Individual workload evidence, including fixture hashes |
| `coverageGaps` | Informational copy of the gate file’s `knownMissingEvidence` |
| `gates` | Per-metric verdict, reason, statistics, threshold, before and after |
| `checks` | Expected TRX case counts, filters and execution verdicts |
| `verdict`, `exitCode`, `error` | Overall result and any orchestration error |
| `startedUtc`, `elapsedSeconds` | Attempt start and complete elapsed duration |

Each fragment contains the workload/gate/machine/cache identities, actual fixture
hashes, executed/skipped flags, lost-event count, submitted/completed/cancelled/
superseded/no-op counts, correctness failures, `notMeasured` metric IDs, raw samples and
counters. Samples are `{ operationId, value }`; units are in metric names. Workload
samples use receipt operation IDs; `events.json.operations` maps submission-ledger
IDs to those IDs. Only report-only metrics (no threshold and not in the foreground
comparison set) may be `notMeasured` without affecting the verdict. Required
metrics still need their full sample counts, including when marked `notMeasured`.
Statistics report count, median, nearest-rank p95, and maximum. Each workload's
`events.json` retains its submission ledger and bounded recorder snapshot:
monotonic timestamp, kind, operation ID, image catalog ID, generation, managed
worker thread ID, and value. Publication values are bitmap bytes. Native events
bracket synchronous Magick decode and LibRaw Unpack/Process, including returns
after cancellation. `frames.json` separately records captured-frame timestamps.

CPU is process CPU time. Peak private bytes are sampled every 10 ms; ordinary
idle and aggressive forced-GC private bytes are distinct fields. Thumbnail bytes, peak cache backlog,
retained pair count, publication bitmap bytes, visible-collection replacements,
native overlap and event counts
are explanatory observations, not interchangeable memory measures. Idle here
means the existing activity/cache/loading counters are zero; future debounced
work can still be scheduled.

The legacy measurement retains its 100 ms warm first-paint, 0.30 warm/control,
1.10 priority/control, JPEG 95/20 MiB and RAW 300/50 MiB peak/forced-GC budgets.
Its sample pairs include actual polling-bracket widths. Every recorded latency
must fall within its actual false-to-true polling bracket; polling delay has no
maximum and is not counted as a speedup. Recorder overhead interleaves nine
on/off pairs using the same `PreviewImage` property-change observer on both
sides. The median of paired first-publication differences is report-only: the
recording-off arm alone spans about 15 ms on the dev host, so an end-to-end
threshold below that would gate on noise. Negative differences are clamped to
zero for nonnegative samples; all signed differences and both original sample
sets are retained. The binding overhead gates are the 10,000-event cost (≤2 µs
median) and zero allocated bytes across those enabled calls, measured as the
smaller of two passes so one-off runtime work such as tiering is not counted,
with a 100-call null-path allocation sample retained as a companion. The nine-pair case
has a five-minute hang watchdog; other isolated cases retain 90 seconds.

## Instrumentation coverage and qualification limits

The production diff is deliberately limited to instrumentation. The following
approved-plan events/counters are not implemented in this iteration: complete
queue/handoff and Loupe render/conversion stage correlation, and aggregate owned
base bytes. Cache-writer completion/drop and resting publication are recorded.
Native overlap counts duplicate concurrent decodes by image ID; superseded native
calls are joined to recorded rejection or WarmCancelRequested events by image ID while their workers
remain active, and cease counting only when NativeEnd arrives. Collection
replacements are counted from Browse.VisibleImages property changes. The harness connects loader and sidecar-writer recorders explicitly;
production router and view-model writer setup do not propagate them. The ledger
uses the first matching publication
or rejection as an operation outcome; there is no separate, deduplicated
production terminal event. Assessment feedback joins by image and time rather
than a propagated assessment operation ID.

These gaps are copied from the frozen gate file into result-level `coverageGaps`
and do not change the verdict. Unavailable report-only metrics are explicitly
`notMeasured` in fragments. A full run can pass or fail its measured gates;
missing required evidence still makes it inconclusive. Reversals and bursts can additionally expose
unreconciled operations or insufficient publication samples. Required correctness
checks separately cover surviving/cancelled warm workers and pick persistence
for both pair members, successful sidecar contents, and failed writes retaining
pending axes despite immediate feedback and a drained writer.

## Reviewing and comparing results

Comparison requires the same gate hash, fixture hashes, machine identity and
cache condition. Foreground metrics also enforce candidate ≤ baseline × 1.10.
A named baseline with missing, null, empty, or unreadable fragments is inconclusive
with the reason "Baseline evidence unreadable".
Missing/skipped cases, missing fragments, lost events, insufficient samples,
unreconciled ledgers or missing required metric evidence cannot pass. Logs, TRX files, pairs,
events and intermediate fragments are retained even for unsuccessful attempts.

Change workload definitions and thresholds only through a reviewed diff to the
gate file. A changed hash starts a new baseline series; never edit an existing
result or quietly drop a slow sample. For later production work, repeat passing
candidates independently and stop optimizing once repeated measurements plateau.
The current-main verdict is a baseline record, not this instrumentation change's
merge condition. The orchestrator owns full-suite validation and FINALIZE.

## Deviations

`RawBaseLoader.CanLoad` resides in the existing PreviewPair partial to keep the
root source file below 500 lines; its behavior and ownership are unchanged.
The production instrumentation diff is +100 net lines against `84f3905`. The
shared flag command guard and receipt replace the five repeated command guards
and receipts; the recorder remains bounded and opt-in.
