# MLSPIKE WP2 harness (unmerged spike only)

This is measurement instrumentation, never production integration. It reads the
owner-confirmed WP1 manifest (SHA-256 pinned in RunContext), checks every image and
label hash, and uses Happy Photon's gated preview decode and default render path.
No catalog is opened. RAW is decoded by Happy Photon's LibRaw bridge.
Input reads reject offline/cloud and linked paths; output files use CreateNew.

## Host preparation

Claude restores Microsoft.ML.OnnxRuntime 1.30.0, then generates and reviews the app
and harness lock files on the appropriate Magick flavor (including AnyCPU on Mac).
Do not use a lock produced by a failed restore. The standalone test project links
the actual shared pixel code and needs no runtime or model.

```powershell
dotnet restore spikes/mlspike/harness/MlSpike.Harness.csproj
dotnet build HappyPhoton.sln -c Release
dotnet build spikes/mlspike/harness/MlSpike.Harness.csproj -c Release
dotnet test spikes/mlspike/harness/tests/MlSpike.Tests.csproj -c Release --filter 'FullyQualifiedName~MlSpike.Tests'
```

Every invocation takes these arguments (all are local paths, no fetches):

```text
--mode quality
--manifest <WP1 canonical manifest.json>
--sample-root <directory containing its relative files>
--model <converted model.onnx>
--config <converted model.json>
--runtime-package <microsoft.ml.onnxruntime.1.30.0.nupkg>
--environment <owner-desktop|windows-2025|ubuntu-24.04|macos-15>
--cpu-model <rig CPU model>
--output <new invocation directory>
```

The host may set MLSPIKE_PHYSICAL_CORES to the rig's measured physical core count;
otherwise topology is read from CIM, /proc/cpuinfo, or sysctl. Logical CPU count
is never substituted. Threads are max(1, physical minus one), inter-op one, CPU only.
Inputs are float32 RGB, bilinearly resized (half-pixel, clamped borders), then
mean/std normalized. Input/output layout is explicitly NCHW or NHWC. Outputs
are batch-one 4D probabilities, sigmoid logits, or softmax logits; class selection
happens before bilinear resize to the rendered base and inclusive 0.5 threshold.
Ground truth uses nearest-neighbor resize. Empty/empty IoU is one.
FP16 conversions preserve float32 input/output. No per-image min/max normalization.

Result identity includes model, config, manifest and runtime-package hashes, CPU,
OS, runner image, provider, thread counts, process ID and timestamp. Each invocation
writes result.json. Keep all mode invocations together as one candidate/environment
evidence directory. Errors are recorded as errors; no pass or owner rating is invented.

## Modes and measurement boundaries

- quality: Q1's 24 subject images or Q2's 16 sky images plus four negatives,
  selected by capability; mask PNGs, per-image timings, IoU and sky area.
- latency: all 36 edges, two warmups, ten fixed-image probes, then 36 timings.
  Reports probe CV, median and max. Timing starts from an already rendered BGRA8
  buffer and includes preprocess/inference/resize; decode and file I/O are outside it.
- cold: --image-id pins one edge. One session and first mask per process.
  Invoke in three fresh processes; host checks spread and median. Hash verification,
  input rendering and topology discovery precede the timed session creation.
- memory: pre-render all edges; run the L1 workload twice, dispose sessions and
  perform full GC after each. Reports resident deltas, repeated-run agreement and
  final cumulative retention. Peak is sampled every 5 ms; this is a sampled lower
  bound, so host qualification must account for missed shorter peaks.
- cancel: --image-id and --latency-result <same model/env L1 result.json>.
  Rechecks a ten-run probe in this session, then 30 cycles at 25/50/75% of the
  cited L1 median. Terminates using RunOptions.Terminate. A request before inference
  or after completion does not count as a successful cancel. Reports all cycles,
  native-return latency, p95/max only when all 30 cancel, and resident growth.
- interaction: --image-id pins one edge base; 30 steps -3 through +3 EV inclusive,
  in ABAB order. B has concurrent inference looping. Reports every render duration,
  each alone block p95, their agreement and pooled with/without inference p95 ratio.
- repeat: --repeat-ids <six comma-separated edge IDs>, three runs each; compares
  runs two/three with run one at the 0.5 mask.
- contact-sheet: --crops <JSON mapping IDs to [x,y,width,height] at the rendered
  base>. Host/owner pins the hardest edge for every rated image; there is no automatic
  easy-crop fallback. Writes fit and native-pixel crops, HTML and blank ratings.csv.
  All 36 masks are generated; only the relevant 28 subject/eight sky rows are rated.
  For X1 add --desktop-result <desktop contact-sheet result.json>. Provenance and
  dimensions must match. Every difference above 0.005 requests re-rating evidence.

Use fresh invocation directories for rig retries. A rig check failing twice is a
stop-and-ask by the orchestrator, not a reason to keep retrying. The orchestrator
owns the active-hours log, deadline, result aggregation, owner ratings, P/S packaging
gates, and LRPARITY verdict. These tools do not automatically dispatch jobs.

## Packaged probe

Publish the branch with the MlSpikePayload MSBuild property (or same-named
environment variable) pointing to a directory containing model.onnx and model.json.
The normal packagers copy the payload to mlspike/ next to app resources.
On macOS the existing packager relocates it to Contents/Resources/mlspike.
Model paths are resolved exclusively inside the installed artifact.

```text
HappyPhoton --mlspike-probe <frozen-public-image> <new-mask.png>
```

For combined subject+sky size/signing measurements stage subject.onnx/subject.json
and sky.onnx/sky.json together. MLSPIKE_PROBE_CAPABILITY=subject or sky selects the
corresponding bundled pair; omit it to use model.onnx/model.json. Run each capability
inside every installed artifact. Include runtime, both models, configs and notices
in P1/P2; compare each probe mask to its same-candidate harness mask (<=0.001).
No app window, catalog or single-instance guard starts for the probe.

## Review boundary

Shared code under Shared/ and PackagedProbe.cs is explicitly included by the app
only on this spike branch; other spikes/** sources are excluded. The console
references the app, so installed and console inference cannot drift.
Source ownership and normal render math remain unchanged. Net production LOC is
zero because this entire evaluation branch is retained as evidence and never merged.
