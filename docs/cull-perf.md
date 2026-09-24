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

## Loading-label dwell measurement

After a Release build, `./scripts/measure-loading-labels.ps1` measures the delayed label the
views bind to; `-LoupeProperty ShowLoadingMessage -DevelopProperty ShowDevelopLoadingMessage`
measures the raw nothing-painted predicates instead. `-Steps 40` is a smoke check;
qualification uses all 100 steps per surface, and a shortened run freezes no coverage floor.
The runner does not build, defaults to a 480-second process-tree timeout, and writes a unique
`Tests/TestResults/loading-label-*` directory unless `-ResultsDirectory` is given.
It reuses the supplemental dwell setup, fixture, cold cache, warming and cadence without a
dispatcher. A signal-held JPEG decode is each surface's positive control: exactly one
activation must precede its release. Intervals follow the raw predicate, excluding step
zero; Develop intervals span selection changes. Incomplete intervals from replaced owners
and uncorrelated activations fail measurement. Qualification requires no activation in
intervals under 300 ms, one activation per interval of at least 320 ms, and short-interval
coverage of at least 80% of the raw-predicate baseline. Late fresh renders stay in
`dwellEvidence` for foreground qualification; ordinary tests skip this opt-in measurement.

## Supplemental dwell and jump workloads

Use `./scripts/cull-perf.ps1 -GateFile Tests/CullPerfGates.dwell.json`, adding
`-Baseline <dwell-attempt>/result.json` to compare within this supplemental series.
Relative gate paths resolve from the repository root. The selected file is copied
and hashed independently; the default frozen table is unchanged.

Dwell measures steady navigation on Loupe and Develop while adjacent warming runs. Jump
moves beyond the warm neighborhood to measure foreground publication and refill.
`Tests/CullPerfGates.dwell.json` owns cadence, metrics, sample floors and baseline
ratios; `scripts/cull-perf.ps1` owns hang watchdogs. The four-workload supplemental file
has a 15-minute runtime budget; results qualify only that gate hash. Event correlation
belongs to the gate file and harness, including handoff, refill, save and
worker-placement evidence; VM spans exclude dispatcher queue delay.

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

The workloads drive the ViewModel without a dispatcher; their spans exclude UI-queue
delay. Publication means assignment at the sink, not a displayed frame. The separate
window-loupe check uses a forced headless render tick and signal-gated loader to verify
dispatcher placement and first-frame correctness, not native decode speed.

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

Fragments retain workload, gate, machine, cache and fixture identity; execution status,
lost events, input outcomes, correctness failures, raw samples and counters. Statistics
are count, median, nearest-rank p95 and maximum. Only report-only metrics may be
`notMeasured` without affecting verdict; required metrics need complete samples.
Event snapshots and frame timestamps are retained as evidence.

CPU, sampled peak private bytes, ordinary idle and forced-GC private bytes are distinct
measurements. Idle means tracked activity is zero, not that future debounced work is
impossible. Poll-derived latencies must lie inside actual false-to-true brackets;
poll delay is never counted as a speedup.
Recorder overhead is gated at ≤2 µs median per event with zero allocations; the
end-to-end paired comparison is report-only because scheduling noise dominates it.
Known gaps live in the gate file's `knownMissingEvidence`.

## Reviewing and comparing results

Comparison requires the same gate hash, fixture hashes, machine identity and
cache condition. Foreground metrics enforce candidate ≤ max(baseline × 1.10,
baseline + `regressionFloorMs`), and a metric with fewer than
`regressionMinimumSamples` samples on either side is reported, not gated: a
percentage alone flagged sub-millisecond spans and single samples as regressions.
A named baseline with missing, null, empty, or unreadable fragments is inconclusive
with the reason "Baseline evidence unreadable".
Missing/skipped cases, missing fragments, lost events, insufficient samples,
unreconciled ledgers or missing required metric evidence cannot pass. Logs, TRX files, pairs,
events and intermediate fragments are retained even for unsuccessful attempts.

Change workload definitions and thresholds only through a reviewed diff to the
gate file. A changed hash starts a new baseline series; never edit an existing
result or quietly drop a slow sample. For later production work, repeat passing
candidates independently and stop optimizing once repeated measurements plateau.
