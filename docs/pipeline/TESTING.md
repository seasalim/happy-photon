# Pipeline Spec — Testing: Goldens, Assets, Tolerances

The rework is only safe because of this harness. WP0.1 builds it; every later work
package extends it. Unit tests follow existing conventions (`Tests/*Tests.cs`, xUnit,
`dotnet test HappyPhoton.sln`).

## 1. Sample assets (`Tests/assets/`)

Committed directly (no LFS), current total **91.37 MiB** within a budget of
**≤ 100 MiB, ≤ 30 MiB per file**. The budget covers six distinct raws plus the
byte-identical burst copy; prefer the
oldest/smallest CC0 body per mosaic type. Provenance is recorded in
`Tests/assets/README.md` with per-file source URL + license.

| Asset | Purpose | Source |
|-------|---------|--------|
| Small Bayer raw ×2 (e.g. Canon CR2 + Nikon NEF, ≤ 15 MB ea. — prefer small-sensor bodies) | decode, WB, goldens | raw.pixls.us, CC0 only |
| X-Trans RAF | Fuji path | raw.pixls.us CC0 |
| DNG | Adobe container path | raw.pixls.us CC0 |
| High-ISO Bayer raw | FBDD quality/runtime evaluation | CC0 research dataset |
| Nikon D300 ColorChecker NEF | physical colorimetric ground truth | author capture, CC0 exception |
| sRGB JPEG with EXIF+GPS+orientation 6 | metadata policy, orientation | author with exiftool from a CC0 photo |
| Display-P3 JPEG of the same picture as an sRGB JPEG | ICC normalize sentinel — sRGB-derived content, so it cannot show gamut preservation | generate via Magick from a CC0 source |
| Display P3 ICC profile (`DisplayP3-v4.icc`) | independent source profile for the wide-gamut normalization test | Compact ICC Profiles, CC0 |
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
- Comparison: per-pixel CIE76 ΔE with an explicit domain. Render comparisons decode
  display sRGB; base comparisons interpret samples as linear Rec.2020 before XYZ/Lab.
  Report mean and p99.
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

The Rec.2020 working-space change re-baselined the same 39-case matrix once
as v8 (against frozen v7, all 39 changed: mean ΔE 0.074–0.617, p99
0.718–1.021). The superseded generation is pruned after the report is
captured.

The AgX rework re-baselines once as v9 and prunes v8. Its attribution has two
measured parts: the numeric-path change (exact tables, fused finalization,
luma basis, convert relocation) moved goldens by at most mean ΔE00 7.56
(Fuji WB), and the AgX default changed RAW goldens by mean ΔE00 12.150 on
average (range 6.047–20.502). Crossing-off output stayed byte-identical.
The cache markers are `RenderPipeline.Version = 9` and `BaseImage.Version =
8`; the latter attributes the re-derived `SourceExposureBiasEv` fact.

R5a keeps render v9, bumps `BaseImage.Version` to 9, and re-baselines the v9
directory once. Standard and HEIC outputs stayed byte-identical; all 19 RAW cases
changed by mean ΔE76 0.003–0.015 and p99 0.000–0.677. The per-image report is
[`Tests/goldens/v9/R5A_ATTRIBUTION.md`](../../Tests/goldens/v9/R5A_ATTRIBUTION.md).

## 3. Tolerances (normative)

| Comparison | Bound |
|------------|-------|
| Same base, repeated render, same platform | bit-identical |
| Golden vs current, same platform | mean ΔE ≤ 1.0, p99 ≤ 3.0 |
| Actual preview-base render vs full-base export aligned to the preview size | mean ΔE ≤ 2.0, p99 ≤ 8.0 |
| Edited sRGB vs Display P3 at the Q16 pre-encode boundary | synthetic mean ΔE00 ≤ 0.034; real RAW ≤ 0.053; sharpening off and on |
| Full-decode base vs half-decode base (raw, at common preview size up to 1600px) | mean ΔE ≤ 2.8 (documented gap) |
| P3-tagged vs sRGB-tagged same-picture bases | mean ΔE ≤ 1.5 |
| Cross-platform (M6): win/linux/mac renders of same case | mean ΔE ≤ 2.0 |
| R5a built-in matrix vs LibRaw Rec.2020 comparator, Bayer/X-Trans Clip/Blend/FBDD | mean ΔE76 ≤ 1.1, p99 ≤ 9.5 |

WYSIWYG is calibrated over every v9 settings case using the actual preview
base and a full-base export, aligning the occasional one-pixel aspect
difference to the preview dimensions. Post-R5a crossing-on measured worst mean
1.87233444906093 and p99 7.97110639082136; crossing-off was bit-identical.
Each worst observation rounds up to the next 0.5, producing 2.0/8.0. The separate
half/full base bound
covers the decoded sampling gap before tone.

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

1. **Tone suites:** `AgxToneEngineTests`, `AgxCrossingTests`, and
   `AgxCrossingDerivationTests` pin the crossing properties, source-derived constants,
   exact-table interpolation, and Blender oracle. `ToneLutTests` pins the retained
   crossing-off formulas, channel-before-master composition in both regimes,
   identity-array sharing, and monotonicity for identity/monotone user curves.
2. **`WhiteBalanceModelTests`**: WHITE_BALANCE.md §9 list.
3. **`RenderDeterminismTests`**: repeated render bit-identical; burst pair identical;
   settings hash stable across process runs (canonical JSON ordering).
4. **`GoldenRenderTests`**: §2 matrix.
5. **`WysiwygTests`**: actual preview-base vs full-export bound (§3 row 3) for both
   regimes and every golden settings case; `WysiwygCalibrationTests` emits the
   opt-in calibration payload.
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
10. **Working-space suites:** `RawWorkingSpaceTests` proves LibRaw output color 8
    numerically, pins the camera-fact semantics, and preserves near-clip meaning;
    `StandardWorkingSpaceTests` checks the external sRGB-profile target, native P3
    gamut vectors, the thumbnail sRGB-proxy limit, and the one-code JPEG identity gate.
11. **RAW histogram suites:** synthetic Bayer/X-Trans geometry, black-level, sRGB-bin,
    clipping, lookup-cap, and cancellation cases; six-fixture typed-frame oracle parity;
    loader fault/invariance checks; exact held-base accessor and refresh identity; and
    headless preferred/effective plus 16-photosite presentation boundaries.
12. **Waveform suites:** pure accumulator tests pin the 256×128 grid, column mapping,
    level boundaries, Rec.601 parity, narrow-source back-fill, histogram-bin invariance,
    and the production overflow bound. Painter/view tests pin square-root normalization,
    opaque premultiplied BGRA, theme-token colors, bitmap reuse/disposal and live-theme
    repaint. Headless tests cover the stable three-entry selector, RAW fallback,
    Library's fixed chrome, cloud-only no-load behavior, and same-image supersession.
13. **Wide-gamut output suites:** `WideGamutExportTests` checks the Display P3 profile,
    independently derived native-P3 codes in every format, gamut survival, and common-space
    agreement. `WideGamutColorimetryTests` gates the Q16 finalization boundary at the
    §3 limits with output sharpening off and on, and records the expected 8-bit
    quantization floor as informational. The former frozen per-RID export
    byte hashes are retired; v9 goldens own regression.
14. **DCP suites:** `DcpProfileReaderTests` is the §7.2 conformance and hostile-input
    suite; `DcpMatrixAndHueSatTests` covers balanced-seam composition, as-shot dual
    interpolation, 2.5D/single/dual tables, V-only encoding, and seeded direct/LUT
    agreement. `DcpProfileDiscoveryTests` covers all local sources, precedence,
    normalization, refresh, corrupt/oversized/sparse input, and no-read availability
    gates. `DcpAtomicityTests` drives an A→B→C newest-wins replacement through cache
    persistence. Settings, preset, catalog, export-warning, ViewModel, hash, and
    determinism suites cover the integrated boundaries. `DcpColorCheckerAnchorTests`
    applies a generated synthetic profile to the D300 ground-truth fixture without a
    skip path. No Adobe profile is committed.

### 4.1 Pipeline validation anchors

Every stage that changes pixels answers to four anchors:

1. **Seeded properties.** `RenderPropertyTests` fixes its seed and draw count.
   Achromatic linear input remains achromatic through the full RAW crossing. The
   post-gain value `a = v·2^(EVuser+EVsource) = 0.18` maps to the pinned display grey
   across the full Contrast range and multiple nonzero `EVsource` values. The old
   display-pivot analogue is retired. Brightness/BaseLook dormancy and crossing-off
   sensitivity are bit-gated separately. DCP adds seeded balanced-neutral matrix
   round trips and 1,000 seeded RGB comparisons between the direct HueSat sequence and
   its compiled lattice.
2. **Source-cited constants.** RGB→XYZ matrices are independently derived in C# from
   each space's published primaries and white point, then compared with both the
   published matrix and the committed oracle. The authorities are IEC 61966-2-1 for
   sRGB, ITU-R BT.2020-2 for Rec.2020, and ISO 22028-2 for ROMM; the W3C CSS Color 4
   conversion appendix provides the cited published values. The exact sRGB matrix in
   `RgbColorSpaceMatrices` is the single authority and explicitly exposes the exact
   primary-derived and published-rounded sRGB variants. `PrecisionColorCases` uses the
   exact variant; `PrecisionDeltaE`, `GoldenImageComparer`, and production's legacy
   sRGB camera-fact conversion use the published-rounded variant, with no value changes.
   Camera characterization composes the exact primary-derived sRGB→Rec.2020 factor
   from the same authority with each copied camera→sRGB fact. DCP tag numbers,
   illuminants, defaults, table ordering, and V encoding cite Adobe DNG 1.7.1 in the
   parser and are exercised through independently built TIFF-IFD fixtures.
   Production sRGB and Rec.2020 forward/inverse basis vectors are checked directly.
3. **Independent oracle.** `Tests/assets/color-science-oracle.json` contains linear
   sRGB/D65, linear Rec.2020/D65, and linear ROMM/D50 RGB↔XYZ matrices and round trips,
   Bradford D50↔D65 adaptation, a synthetic camera→sRGB characterization matrix and
   transformed RGB vectors, DCP ProPhoto crossing vectors, sRGB EOTF vectors, and the pre-November-2014
   ColorChecker values for the CIE 1931 2° observer. It is emitted by the dev-only
   BSD-licensed `colour-science` generator. Tests and CI read only JSON and never run
   Python. Regeneration is intentionally version-locked:

   ```powershell
   python -m venv .venv-color-oracle
   ./.venv-color-oracle/Scripts/python -m pip install `
     colour-science==0.4.7 numpy==2.4.4
   ./.venv-color-oracle/Scripts/python scripts/generate-color-science-oracle.py
   git diff --exit-code -- Tests/assets/color-science-oracle.json
   ```

4. **Physical ground truth.** `nikon-d300-colorchecker.nef` is the author-captured
   CC0 Nikon D300 fixture described in `Tests/assets/README.md`. Its manifest pins the
   byte length and SHA-256, default full-resolution decode, export intent with no
   resize (oriented 4320×2868), `OMP_NUM_THREADS=1`, projective corners and central
   patch ROIs, the 90° chart mapping, frozen neutral-patch XYZ, and the measurement
   normalization. The characterization anchor samples after analytic picked WB at the
   pre-crossing scene-linear Rec.2020 seam, applies the frozen least-squares exposure
   scalar, then adapts to the chart's declared ICC D50 white. A separate observation
   renders those same fixed gains through the default RAW AgX crossing and samples the
   finalized sRGB output with no post-look exposure scalar. Fresh base samples are
   checked for XYZ drift but never feed back into gains or bounds.

The manifest uses the precommitted “worst supported-RID observation rounded up to the
next 0.5” rule for both anchors. Post-R5a win-x64 measures pre-crossing mean/max ΔE00
2.6384472924778217/6.245169268388049, retaining bounds 3.0/6.5. The integrated AgX
look measures 5.9938993948792065/13.762419710624835, retaining bounds 6.0/14.0.
Linux-x64 measures 2.638447292477815/6.2451692683880236 and osx-arm64
2.638449030560668/6.245169268388058 (look 5.993899394879203/13.762419710624826
and 5.993900252729108/13.76241971062483), harvested from the PR CI test
results; all three RIDs agree at the 1e-5 level and the retained bounds hold.
These budgets pin characterization and look drift; they are not claims that an aged
physical chart should be rendered colorimetrically exact.

R5b constructs an independent, non-copyrighted DCP whose ForwardMatrix reproduces the
established D300 built-in seam, parses the actual generated bytes, and runs the same
non-skipping physical measurement with frozen mean/max ΔE00 bounds 3.0/6.5. The
balanced-neutral CC/AB → D50 white test remains the matrix correctness anchor. Dev-only
`scripts/SyntheticDcpGenerator.csproj` generates valid, malformed, and dual-table
fixtures without checking binaries into the repository.

## 5. Performance

Opt-in `HAPPY_PHOTON_PERF=1` diagnostics remain outside normal CI. The tone
gate is `AgxPerformanceGateTests`: one warm-up, median of five, a separate
process per output target, and a required JSON report. It covers 1600px
Contrast +25 slider ticks on the Canon 6D, Fuji X30, sRGB JPEG, and HEIC; the
three-size RAW export per target; the full-resolution standard export; and unique-key
cold all-channel-curve ticks for one RAW and one standard fixture,
sampling process private memory every 10 ms. Reports compare against the same
harness run on the pre-AgX baseline (`878903f`); budgets are slider ≤ 150 ms,
export wall ≤ +5%, and private peak ≤ +16 MiB.

`DcpPerformanceGateTests` runs separately with `HAPPY_PHOTON_R5B_PERF=1` against the
20 MP Canon 6D fixture. It records cold external, cold embedded, and warm selected-
profile decode deltas (≤ 50/30/15 ms), incremental matrix-kernel delta (≤ 10 ms),
HueSat-only preview/full deltas (≤ 80/250 ms and exactly zero inactive), the standing
150 ms slider gate, deterministic managed and retained deltas (each ≤ 8 MiB), active
export +5%/+16 MiB, and a ≤ 2 s cold scan over a fixed 200-profile synthetic tree.
Run it in a fresh Release process:

```powershell
$env:HAPPY_PHOTON_R5B_PERF='1'
dotnet test Tests/HappyPhoton.Tests.csproj -c Release --filter FullyQualifiedName~DcpPerformanceGateTests --logger "console;verbosity=detailed"
```

The waveform diagnostic reports setup separately and gates the warmed
accumulator-only median over a pre-materialized 1024×1024 Q16 RGB span at
5 ms. Additional opt-in diagnostics cover preview base decode and edited
standard-thumbnail generation at 512px; track results in PR descriptions when
a work package touches the hot path.

`PreviewPipelinePerformanceTests` carries those preview diagnostics and their
≤ 150 ms slider budgets. In-class execution order is a runner implementation
detail that reshuffles as the assembly changes, and heap and thread-pool
residue from the class's own cache and render tests can inflate a later test's
medians past budget. The canonical run therefore executes each test in its own
Release process; apply the same one-test-per-process rule to any new
`HAPPY_PHOTON_PERF` class with latency budgets, and never loosen a budget to
make a single-process class run pass:

Effects extend the opt-in Release gate with frozen active-minus-off budgets: preview
delta ≤25 ms while total tick remains ≤150 ms; full export delta ≤max(5%, 500 ms) for
sRGB and Display P3; incremental private-memory peak ≤ one Q16 RGB frame at the
processed dimensions. Resting tests also pin bit identity at worker caps 1 and 2 and
cancellation at effects stage entry. Inactive effects are covered separately by exact
pixel and canonical-hash equality, so the skip is deterministic rather than inferred
from timing.

```powershell
$env:HAPPY_PHOTON_PERF='1'
foreach ($test in 'DevelopEntryLatencyAndMemory',
                  'RawCandidateLatency',
                  'RenderedThumbnailCacheLatency',
                  'LibraryHistogramLatency') {
  dotnet test Tests/HappyPhoton.Tests.csproj -c Release --no-build --no-restore `
    --filter "FullyQualifiedName~PreviewPipelinePerformanceTests.$test" `
    --logger "console;verbosity=detailed"
}
```

```powershell
$env:HAPPY_PHOTON_PERF='1'
$env:HAPPY_PHOTON_AGX_PERF_TARGET='srgb' # then display-p3
$env:HAPPY_PHOTON_AGX_PERF_REPORT='artifacts/agx-perf-srgb.json'
dotnet test Tests/HappyPhoton.Tests.csproj -c Release --no-build --no-restore `
  --filter FullyQualifiedName~AgxPerformanceGateTests

$env:HAPPY_PHOTON_WYSIWYG='1'
$env:HAPPY_PHOTON_WYSIWYG_REPORT='artifacts/agx-wysiwyg.json'
dotnet test Tests/HappyPhoton.Tests.csproj -c Release --no-build --no-restore `
  --filter FullyQualifiedName~WysiwygCalibrationTests
```

Recorded win-x64 numbers: slider 29.4/35.2/17.2/16.7 ms in the fixture order
above; sRGB variants +2.0% with +1.0 MiB private peak; P3 variants −1.5% with
+0.3 MiB; standard export −7.6%. All budgets pass.
RAW base decode performance output includes a `RawHistogram` step for the sensor pass;
measure at least the 20 MP Canon EOS 6D fixture when this gate is enabled.

R5a's isolated import gate uses that 20 MP Canon in camera-native output mode, one
warm-up plus five samples. It compares direct Q16 import with fused
characterization of the same native span. The budgets are ≤ 45 ms preview,
≤ 150 ms full (recalibrated from the checkpoint-A 30/100 ms freeze with user
approval 2026-08-19 — the measured floor under the 4 MiB transient constraint
left no variance margin; CHARACTERIZATION.md §4 records the evidence), and a
≤ 4 MiB deterministic retained private-memory delta; async-sampled peaks are
informational because native-allocator private bytes are not reproducible.
Measured deltas on the review machine: preview 22–31 ms, full 86–136 ms,
retained 0.0/2.0 MiB:

```powershell
$env:HAPPY_PHOTON_R5A_PERF='1'
dotnet test Tests/HappyPhoton.Tests.csproj -c Release --filter `
  FullyQualifiedName~CameraRgbCharacterizationPerformanceTests
```

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

### 5.1 Display-reference comparison

`ReferenceComparisonTests` is a report-only comparison of the two committed diagnostic
RAWs (`fujifilm-x30.raf` and `canon-eos-6d-iso-6400.cr2`) with externally rendered
references. It is excluded from normal execution by `HAPPY_PHOTON_COMPARE=1`. Reference
files use `Tests/assets/references/<fixture-stem>.<tool>.<ext>`; an explicitly set
`HAPPY_PHOTON_COMPARE_REFERENCE_DIR` replaces that directory rather than supplementing
it. Every matching tool is reported. A missing reference is a named skip. References
must be lossless and have at least the 1600px measurement edge; the harness only
downscales, never upscales. Untagged references are treated as sRGB and that assumption
is included in the report.

The protocol is fixed as follows. The reference is EXIF-auto-oriented and its embedded
or declared color is normalized to display sRGB before any resize or measurement. Both
arms are reduced to a 1600px long edge in linear light with the pipeline's shared resize
filter. For every display-sRGB RGB8 pixel:

```text
Y  = 0.2126 R + 0.7152 G + 0.0722 B
Cb = B - Y
Cr = R - Y
```

The full-frame median is the middle value, or the mean of the two middle values for an
even population. Exposure is bisected over the closed product range [−3,+3] for at most
12 iterations until the candidate median is within 0.25 RGB8 code
(0.25/255 normalized) of the once-resized reference median. Every candidate passes
through the same 1600px operation, and the
converged candidate is reused for metrics. An unreachable target or iteration limit is
reported explicitly.

The reference selects the 256px window with lowest population standard deviation in Y
whose mean is in [40,200]; an exact tie chooses the topmost, then leftmost window. The
same coordinates measure both arms. Acutance is the mean central-difference gradient
magnitude `sqrt(((Y[x+1]-Y[x-1])/2)^2 + ((Y[y+1]-Y[y-1])/2)^2)` over interior pixels
only. For Y, Cb, and Cr the report gives population standard deviation, the standard
deviation surviving a radius-4 box blur, and their ratio. The blur clamps samples to the
window edge. A total deviation below `1e-12` makes the ratio `undefined`. These values
are descriptive; assertions cover only geometry, ROI discovery, bisection convergence,
and finite metrics.

The opt-in self-reference cases render a temporary reference from each RAW and drive
the same decode, canonicalization, bisection, ROI, and metric composition even when no
external references are committed. LibRaw's OpenMP pin is process-start-sensitive, so
run the filtered suite in a fresh process:

```powershell
$env:OMP_NUM_THREADS='1'
$env:HAPPY_PHOTON_COMPARE='1'
# Optional: $env:HAPPY_PHOTON_COMPARE_REFERENCE_DIR='D:\references'
dotnet test Tests/HappyPhoton.Tests.csproj -c Release --filter FullyQualifiedName~ReferenceComparisonTests --logger "console;verbosity=detailed"
```

The PRECISION investigation's diagnostic harness (Phase 0 ramps and the Phase 1
Slice A boundary census) was retired after the investigation closed with the
Q16-storage decision; the harness and its manifest live in git history before the
sweep that removed them. The live color-science suites (`PrecisionDeltaE`,
`PrecisionColorCases`, and the oracle/anchor tests) are unaffected.

The LibRaw resolver's single-file extraction path has a committed publish smoke. Run
the matching command on Windows or Linux; it restores in locked mode, publishes a
self-contained single-file console, decodes the Canon fixture, and asserts that both
the bridge and LibRaw companion loaded from the runtime extraction directory:

```powershell
./scripts/verify-libraw-single-file.ps1 -RuntimeIdentifier win-x64
./scripts/verify-libraw-single-file.ps1 -RuntimeIdentifier linux-x64
```

The manual working-space comparison renders a focused fixture/settings set against a frozen baseline,
reports normalized RMSE, and writes three side-by-side crop rows per case:

```powershell
dotnet run --file scripts/evaluate-wide-working-space.cs -- `
  <frozen-baseline-directory> artifacts/wide-working-space
```

Review `report.tsv` and `crop-sheets/`, then record the user's look sign-off outside the
automated test gate.

## 6. CI

The three-platform workflow runs both xUnit v3 test hosts. Ordinary and native bitmap
integration tests live in `Tests/HappyPhoton.Tests.csproj`; UI and dispatcher tests run
through the supported Avalonia headless integration in
`HeadlessTests/HappyPhoton.Headless.Tests.csproj`. Keep Windows WIC coverage in the
ordinary host so the native and headless Avalonia platforms never share a process.

Platform and codec gaps use xUnit v3 native runtime skips (`Assert.Skip` or
`Assert.SkipWhen`) with an explicit reason so they remain visible in logs. CI gates on
discovery before execution: 1,294 ordinary listed cases plus 132 headless listed cases.
The full run currently expands dynamic theories to 1,359 ordinary and 137 headless
execution cases. Run tests with a 90-second blame
hang timeout while changing either host.

Golden assets and baselines must keep the repo clone under control — if the goldens
directory exceeds ~20 MB, shrink render size before reaching for LFS.
