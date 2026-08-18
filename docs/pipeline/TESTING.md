# Pipeline Spec — Testing: Goldens, Assets, Tolerances

The rework is only safe because of this harness. WP0.1 builds it; every later work
package extends it. Unit tests follow existing conventions (`Tests/*Tests.cs`, xUnit,
`dotnet test HappyPhoton.sln`).

## 1. Sample assets (`Tests/assets/`)

Committed directly (no LFS), total budget **≤ 81 MiB, ≤ 30 MiB per file**. The budget
covers five distinct raws plus the byte-identical burst copy; prefer the
oldest/smallest CC0 body per mosaic type. Provenance is recorded in
`Tests/assets/README.md` with per-file source URL + license.

| Asset | Purpose | Source |
|-------|---------|--------|
| Small Bayer raw ×2 (e.g. Canon CR2 + Nikon NEF, ≤ 15 MB ea. — prefer small-sensor bodies) | decode, WB, goldens | raw.pixls.us, CC0 only |
| X-Trans RAF | Fuji path | raw.pixls.us CC0 |
| DNG | Adobe container path | raw.pixls.us CC0 |
| High-ISO Bayer raw | FBDD quality/runtime evaluation | CC0 research dataset |
| sRGB JPEG with EXIF+GPS+orientation 6 | metadata policy, orientation | author with exiftool from a CC0 photo |
| Display-P3 JPEG of the same picture as an sRGB JPEG | ICC normalize sentinel | generate via Magick from a CC0 source |
| AdobeRGB JPEG | second ICC case | generate |
| 16-bit TIFF | depth preservation | generate |
| HEIC | platform codec path (skip test when codec absent) | generate/CC0 |
| Synthetic gradient PNG (0→1 ramp, generated in-test) | LUT banding, monotonicity | code |

Raw-file burst pair: two consecutive CC0 frames of a similar scene if obtainable; else
duplicate one raw byte-for-byte under two names (sufficient for determinism testing).

### 1.1 Opt-in modern-camera compatibility fixtures

`Tests/compatibility-fixtures.json` is the authority for downloaded compatibility
fixture provenance, CC0 license, exact byte length, SHA-256, selection lifecycle, and
reviewed behavior. The reciprocal committed-asset authority is
`Tests/assets/README.md`. Compatibility RAWs are never committed: the file-based .NET
fetcher verifies or downloads them into the disposable, gitignored
`artifacts/compatibility-fixtures/` cache.

```powershell
dotnet run --file scripts/fetch-compatibility-fixtures.cs
dotnet run --file scripts/fetch-compatibility-fixtures.cs -- sony-a9m3-lossy
```

The provenance URL is also the download endpoint in this P0 slice. An existing cached
length or hash mismatch fails closed, names the fixture and observed/expected values,
and is never replaced automatically. A project-controlled mirror is deferred until a
scheduled release gate makes upstream availability release-critical. Selected fixtures
are capped at 30 MiB. Tests never access the network.

The manifest uses strict JSON. `selectionStatus` is `pending` or `selected`; selected
entries use `expectationStatus` `candidate` or `reviewed`. Candidates omit `expected`
and run only in discovery. Reviewed entries require capability, metadata, sensor,
camera-WB/matrix, tolerance, and review expectations appropriate to their outcome.
Camera matrices are 3-by-the-native-multiplier-count in row-major order and each row
sums to 1. All six P0 entries use `testLevel: smoke`; this slice creates no compatibility
goldens or P1 placeholders.

`HAPPY_PHOTON_COMPAT` has four states:

| Value | Behavior |
|-------|----------|
| unset | One compatibility fact skips before loading the manifest or touching a fixture. |
| `1` | Runs reviewed fixtures; missing files produce named skip terminals and a fetch instruction. |
| `discovery` | Requires every selected fixture and valid hash, records candidate and reviewed application-path observations, and never changes expectations. |
| `strict` | Rejects pending/candidate entries and fails for a missing, invalid, or behaviorally different reviewed fixture. |

Any other non-empty value fails so a misspelled gate cannot silently skip. The P0
harness is intentionally Windows x64 only and lives wholly in the ordinary test host,
whose assembly fixture initializes Avalonia/WIC. It owns each fixture's metadata,
browse-thumbnail, preview base, full base, camera facts, default and edited renders,
JPEG export, orientation, and disposal checks. It emits exactly one `COMPAT TERMINAL`
line per selected fixture followed by `COMPAT COMPLETE observed=N selected=N`, and writes
the ignored structured report to `artifacts/compatibility-results/`. Discovery also
writes 500 px default-render review images there.

The Windows x64 review on 2026-08-16 ran six selected fixtures on the packaged LibRaw
0.22.2.7 runtime: the Canon R5 Mark II RAW/C-RAW, Sony A9 III lossy ARW, Fujifilm X-T50
compressed RAF, and Panasonic S9 RW2 completed every application path; the X-T50
reported X-Trans filters `0x00000009`. Nikon Z8 HE metadata and its embedded JPEG browse
thumbnail succeeded, while `Unpack` returned LibRaw `-2`,
`Unsupported file format or not RAW file`; preview/full/export returned no developed
base, and the existing Nikon-HE user status was set. Because `RawBaseLoader` catches the
native exception and maps any null result to `UnsupportedRaw`, the causal link between
that exact `-2` and the production outcome is inferred by the end-to-end fixture test,
not proven through a production diagnostic seam.

## 2. Golden mechanism

- Goldens are rendered PNGs stored under `Tests/goldens/v<RenderPipeline.Version>/`,
  named `<asset>__<settings-case>.png`, rendered at **long edge 500** (keeps the golden
  directory ≤ 25 MB without LFS).
- **Active baseline marker:** `Tests/goldens/ACTIVE_VERSION` (plain text: `v0`, `pending`,
  `v1`, …). Golden and WYSIWYG suites read it; the literal value `pending` makes them
  report **skipped-with-reason** ("awaiting re-baseline") instead of failing. This is
  how the integration branch stays green mid-rework (roadmap: integration strategy).
- Comparison: per-pixel CIE76 ΔE on sRGB→Lab conversion. Report mean and p99.
- Re-baselining: `HAPPY_PHOTON_UPDATE_GOLDENS=1 dotnet test` regenerates; CI never sets
  it. A golden diff in review must be justified by a `RenderPipeline.Version` bump or a
  spec change in the PR.
- Settings cases, phased with the pipeline (10 total):
  - **Tonal set** (baselined as v1 at WP2.5, when the chromatic stage is still
    identity): identity; +2 EV; −2 EV; highlights −100; shadows +80; contrast +50;
    full-combo tonal preset — 7 cases.
  - **WB set** (added at WP3.2, which bumps `RenderPipeline.Version` → 2 and
    regenerates all baselines as v2): WB 3000 K; WB 9000 K tint +50; WB 9000 K
    tint −50 — 3 cases. Never baseline WB cases while the chromatic stage is a stub —
    that would golden "WB ignored" and immediately invalidate itself.
  - v2's tonal-case renders were required to be **byte-identical** to their v1
  counterparts when the v2 baselines were introduced (as-shot skips the chromatic
  stage exactly). Keep only the active baseline generation; prune obsolete versions
  whenever a new one is activated.

The full-combo tonal case is pinned to exposure +1 EV, brightness +10, contrast +25,
shadows +35, highlights −50, and a monotone curve through `(0,0)`, `(0.25,0.20)`,
`(0.75,0.82)`, `(1,1)`. Chroma, white balance, geometry, and preset identity remain
at defaults.

WP0.1's v0 baseline uses the v1 asset × tonal-case matrix below. It captures the
current export-quality path as a calibration reference: full decode, current edit
application, one resize to long edge 600, then color-profile normalization to sRGB
for the golden PNG. It does not use the half-size preview decode or preview JPEG cache.

### 2.1 Asset × case matrix (keeps golden count and runtime bounded)

| Asset | v1 cases (WP2.5) | added at v2 (WP3.2) |
|-------|------------------|---------------------|
| Reference Bayer raw (the CR2) | all 7 tonal | all 3 WB |
| Display-P3 JPEG | all 7 tonal | all 3 WB |
| NEF, RAF, DNG, AdobeRGB JPEG, sRGB JPEG, 16-bit TIFF | identity, +2 EV | WB 3000 K |
| HEIC | identity (skippable per §6) | — |

27 golden files at v1, 39 at v2. The clipped-highlight case uses the bright water
reflection in the reference CR2, as verified during WP4.1
([DECODE.md §2.2](DECODE.md#22-highlight-reconstruction-evaluation)).

## 3. Tolerances (normative)

| Comparison | Bound |
|------------|-------|
| Same base, repeated render, same platform | bit-identical |
| Golden vs current, same platform | mean ΔE ≤ 1.0, p99 ≤ 3.0 |
| Preview render vs export render downscaled to same size | mean ΔE ≤ 1.5, p99 ≤ 4.0 |
| Full-decode base vs half-decode base (raw, at common preview size up to 1600px) | mean ΔE ≤ 2.1 (documented gap) |
| P3-tagged vs sRGB-tagged same-picture bases | mean ΔE ≤ 1.5 |
| Cross-platform (M6): win/linux/mac renders of same case | mean ΔE ≤ 2.0 |

The RAW half/full bound pins the Canon EOS 350D fixture's measured LibRaw
half-size demosaic gap of mean ΔE 2.087 at the common preview size. A resize-filter
audit ranged from 2.086 to 2.205, confirming the residual is decode sampling rather
than a render or resize regression.

**OS gating policy at M6:** goldens are generated on Linux CI (canonical). Linux uses
the same-platform mean/p99 bounds above. Windows and Apple Silicon macOS compare every
RAW and non-RAW case to that canonical baseline with the cross-platform mean ΔE ≤ 2.0
bound. HEIC remains the only skippable golden when the platform codec reports no read
support; codec skips always carry an explicit reason.

**X-Trans decodes are not byte-comparable across processes.** With OpenMP threading
uncontrolled, two census runs differed on `fujifilm-x30` alone, by one sample in
36,433,152, while four Bayer assets reproduced byte-exactly (DECODE.md §2.6). The
bit-identical row above covers repeated renders of one base, not X-Trans decodes in
separate processes; compare those with a tolerance. The X30 golden's mean ΔE ≤ 1.0 is
orders above the observed difference.

## 4. Required suites

1. **`ToneLutTests`**: monotonicity property (`lut[i+1] ≥ lut[i]`, non-strict — clamp
   plateaus are legal) over randomized valid settings with **identity or monotone user
   curves** (seeded, ≥ 1000 draws; non-monotone user curves are allowed by the product
   and exempt from this property — RENDER.md §5.5); identity case reproduces `E(x)`
   within 1 LSB@8bit; shoulder C1 continuity numeric check; every §5 formula pinned at
   3 sample points, including the clamp boundaries (e.g. Brightness+Contrast at +100
   must plateau, not fold back).
2. **`WhiteBalanceModelTests`**: WHITE_BALANCE.md §9 list.
3. **`RenderDeterminismTests`**: repeated render bit-identical; burst pair identical;
   settings hash stable across process runs (canonical JSON ordering).
4. **`GoldenRenderTests`**: §2 matrix.
5. **`WysiwygTests`**: preview vs export bound (§3 row 3) for the settings cases.
6. **Current-format boundary tests**: `EditSettingsJsonTests` pins canonical ordering,
   clone-before-clamp behavior, range validation, removed WB modes, and rejection of
   every non-v2 write. `CatalogSchemaTests` pins the clean new schema, acceptance of
   harmless extra columns, and actionable startup rejection for missing columns;
   `CatalogPersistenceTests` pins neutral no-write recovery for null, malformed, or
   non-v2 rows. Preset and MCP tests require explicit/current versions and reject v1.
7. **`ExportMetadataTests`**: OUTPUT.md §6 items (EXIF copy, orientation, GPS strip,
   no stale thumbnail, ICC present, subsampling switch).
8. **`BaseLoaderTests`**: DECODE.md §7 items; PPM16 byte-order unit test with a
   synthetic buffer; HEIC-does-not-hit-LibRaw via log capture.
9. **`RenderDetailTests`**: chroma NR preserves luma and alpha; a seeded noise image
   rendered as one band and as forced non-divisible bands is bit-identical at box
   radii 1 and 3.

## 5. Performance (informational, not gating)

Opt-in local `HAPPY_PHOTON_PERF=1` diagnostics cover preview base decode, slider-tick
render at 1600px, and full export of one raw; normal CI does not enable them. Track
results in PR descriptions when a work package touches the hot path; hard budget only
for slider tick (≤ 150 ms dev baseline, RENDER.md §10).

The direct full-resolution detail diagnostic warms the optimized kernel, then uses a
5472×3648 synthetic image and reports elapsed time and peak private-memory delta:

```bash
HAPPY_PHOTON_PERF=1 dotnet test Tests/HappyPhoton.Tests.csproj -c Release --filter RenderDetailPerformanceTests
```

Run the following to decode the ISO 6400 fixture at Off, Light, and Full and report
full-decode time, center-crop chroma variation, and pixel deltas:

```bash
HAPPY_PHOTON_FBDD_EVAL=1 dotnet test Tests/HappyPhoton.Tests.csproj --filter RawFbddEvaluationTests
```

It is an explicit opt-in diagnostic because all three modes require full RAW decodes.

The Phase 0 precision diagnostic generates its three ramps in-test, captures the eight
real-pipeline attribution checkpoints, and emits a deterministic metric payload plus
separate environment/timing details. Run it in a fresh Release process with
`$env:HAPPY_PHOTON_PRECISION='1'; dotnet test Tests/HappyPhoton.Tests.csproj -c Release --filter FullyQualifiedName~PipelinePrecisionInvestigationTests --logger "console;verbosity=detailed"`;
repeat the command and byte-compare the two payload sections. TIFF source-code and
stage-reconstruction precondition failures abort before report emission; the reported
gate rows cover the non-aborting base and preview/export comparisons.

The Phase 1 Slice A boundary census uses the separate
`HAPPY_PHOTON_PRECISION_CENSUS=1` gate. Slice A2 runs case 5 first, then the wide-space,
exposure, stacked-edit, and A1 synthetic cases. Its reviewed
`Tests/assets/precision-census-manifest.json` fixes populations, the distinct full-frame
RAW roster and duplicate exclusion, the equal settings cross-product, focused
measurement ROIs, and the bounded record policy. Full RAW bases are never cropped;
focused ROIs constrain measurement only, preserving detail scale from the production
render dimensions.

Declare a different artifact path for each fresh Release process and run the census
twice. Each completed case is flushed before the next starts, and the metric artifact
contains the expected-case inventory and manifest SHA-256. Environment identity and
elapsed time remain outside the deterministic payload. The harness pins OpenMP to one
thread so RAW decodes repeat across processes (DECODE.md §2.6); the pin is recorded in
the payload and does not change production decode. Set
`HAPPY_PHOTON_PRECISION_CENSUS_OPENMP=uncontrolled` only to reproduce the unpinned
diagnostic; leave it unset for the controlled census protocol.

```powershell
$env:HAPPY_PHOTON_PRECISION_CENSUS='1'
$env:HAPPY_PHOTON_PRECISION_CENSUS_ARTIFACT='artifacts/precision-census/run-1.metrics'
dotnet test Tests/HappyPhoton.Tests.csproj -c Release --filter FullyQualifiedName~PrecisionBoundaryCensusTests --logger "console;verbosity=detailed"
$env:HAPPY_PHOTON_PRECISION_CENSUS_ARTIFACT='artifacts/precision-census/run-2.metrics'
dotnet test Tests/HappyPhoton.Tests.csproj -c Release --filter FullyQualifiedName~PrecisionBoundaryCensusTests --logger "console;verbosity=detailed"
```

The quality verdict uses exact streamed `N` and `countBelow1`; nearest-rank p99 is
material exactly when `countBelow1 < ceil(0.99*N)`. A bounded systematic p99 is emitted
only as a descriptive estimate and never selects an outcome. Synthetic ramps retain
their derivative eligibility rule; RAW and wide-space rows use oracle-present, useful,
non-clamped pixels. Native and detail boundaries report clip/recovery/quality as
inapplicable and require exact stored-change evidence, while an unavailable analytic
metric forces P1A-X.

Combine the byte-identical artifacts in a separate process. The combiner verifies the
inventory and required case/population/boundary evidence and emits exactly one of
P1A-CLEAN, P1A-LOSS, or P1A-X. The statement is scoped to working-storage boundaries
and selects none of P1-Q16, P1-FP, or P1-X.

```powershell
$env:HAPPY_PHOTON_PRECISION_CENSUS_COMBINE='1'
$env:HAPPY_PHOTON_PRECISION_CENSUS_RUN_1='artifacts/precision-census/run-1.metrics'
$env:HAPPY_PHOTON_PRECISION_CENSUS_RUN_2='artifacts/precision-census/run-2.metrics'
$env:HAPPY_PHOTON_PRECISION_CENSUS_TERMINAL='artifacts/precision-census/terminal.txt'
dotnet test Tests/HappyPhoton.Tests.csproj -c Release --filter FullyQualifiedName~PrecisionCensusCombineTests --logger "console;verbosity=detailed"
```

The LibRaw resolver's single-file extraction path has a committed publish smoke. Run
the matching command on Windows or Linux; it restores in locked mode, publishes a
self-contained single-file console, decodes the Canon fixture, and asserts that both
the bridge and LibRaw companion loaded from the runtime extraction directory:

```powershell
./scripts/verify-libraw-single-file.ps1 -RuntimeIdentifier win-x64
./scripts/verify-libraw-single-file.ps1 -RuntimeIdentifier linux-x64
```

## 6. CI

The three-platform workflow runs both xUnit v3 test hosts. Ordinary and native bitmap
integration tests live in `Tests/HappyPhoton.Tests.csproj`; UI and dispatcher tests run
through the supported Avalonia headless integration in
`HeadlessTests/HappyPhoton.Headless.Tests.csproj`. Keep Windows WIC coverage in the
ordinary host so the native and headless Avalonia platforms never share a process.

Platform and codec gaps use xUnit v3 native runtime skips (`Assert.Skip` or
`Assert.SkipWhen`) with an explicit reason so they remain visible in logs. CI gates on
discovery before execution: 1,075 ordinary listed cases plus 113 headless listed cases.
The full run currently expands dynamic theories to 1,099 ordinary and 116 headless
execution cases. Run tests with a 90-second blame
hang timeout while changing either host.

Golden assets and baselines must keep the repo clone under control — if the goldens
directory exceeds ~20 MB, shrink render size before reaching for LFS.
