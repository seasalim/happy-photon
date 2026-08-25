# Pipeline Spec — Testing: Goldens, Assets, Tolerances

The pipeline is only safe to change because of this harness. Unit tests follow
existing conventions (`Tests/*Tests.cs`, xUnit, `dotnet test HappyPhoton.sln`).

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
| High-ISO Bayer raw | luminance-NR quality/runtime tuning | CC0 research dataset |
| High-ISO iPhone HEIC (≤ 8.5 MiB) | standard-source luminance-NR tuning | contributor original, GPL-3.0-or-later |
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

The provenance URL is also the download endpoint (a project-controlled mirror
is deferred until a release gate makes upstream availability release-critical).
An existing cached length or hash mismatch fails closed, names the fixture and
observed/expected values, and is never replaced automatically. Selected
fixtures are capped at 30 MiB. Tests never access the network.

The manifest uses strict JSON. `selectionStatus` is `pending` or `selected`; selected
entries use `expectationStatus` `candidate` or `reviewed`. Candidates omit `expected`
and run only in discovery. Reviewed entries require capability, metadata, sensor,
camera-WB/matrix, tolerance, and review expectations appropriate to their outcome.
Camera matrices are 3-by-the-native-multiplier-count in row-major order and each row
sums to 1. All seven current entries use `testLevel: smoke`.

The selected Leica M Monochrom (Typ 246) entry pins `M2462362.DNG` by exact length
and SHA-256 with sensor `Colors == 1` and absent camera-color expectations. Discovery
and strict modes exercise preview and full decode, then require exact equal channels
for the neutral render and lossless PNG and 16-bit TIFF exports in both sRGB and
Display P3 under extreme dormant WB, profile, saturation/vibrance, mixer, and
channel-curve settings. The local Q2 MONO file
is not a manifest fixture; its manual preview-base gate is ≤2.5 s and ≤600 MiB peak
private-memory delta.

`HAPPY_PHOTON_COMPAT` has four states:

| Value | Behavior |
|-------|----------|
| unset | One compatibility fact skips before loading the manifest or touching a fixture. |
| `1` | Runs reviewed fixtures; missing files produce named skip terminals and a fetch instruction. |
| `discovery` | Requires every selected fixture and valid hash, records candidate and reviewed application-path observations, and never changes expectations. |
| `strict` | Rejects pending/candidate entries and fails for a missing, invalid, or behaviorally different reviewed fixture. |

Any other non-empty value fails so a misspelled gate cannot silently skip. The
harness is intentionally Windows x64 only and lives wholly in the ordinary test host,
whose assembly fixture initializes Avalonia/WIC. It owns each fixture's metadata,
browse-thumbnail, preview base, full base, camera facts, default and edited renders,
JPEG export, orientation, and disposal checks. It emits exactly one `COMPAT TERMINAL`
line per selected fixture followed by `COMPAT COMPLETE observed=N selected=N`, and writes
the ignored structured report to `artifacts/compatibility-results/`. Discovery also
writes 500 px default-render review images there.

Known reviewed limitation: Nikon Z8 High Efficiency RAW. Metadata and the embedded
JPEG browse thumbnail succeed, but `Unpack` returns LibRaw `-2`,
`Unsupported file format or not RAW file`; preview/full/export return no developed
base, and the existing Nikon-HE user status is set. Because `RawBaseLoader` catches the
native exception and maps any null result to `UnsupportedRaw`, the causal link between
that exact `-2` and the production outcome is inferred by the end-to-end fixture test,
not proven through a production diagnostic seam.

## 2. Golden mechanism

- Goldens are rendered PNGs stored under `Tests/goldens/v<RenderPipeline.Version>/`,
  named `<asset>__<settings-case>.png`, rendered at **long edge 500** (keeps the golden
  directory ≤ 25 MB without LFS).
- **Active baseline marker:** `Tests/goldens/ACTIVE_VERSION` (plain text, e.g.
  `v9` or `pending`) is the source of truth for the active generation — this doc
  deliberately does not restate the current value. Golden and WYSIWYG suites
  read it; the literal value `pending` makes
  them report **skipped-with-reason** ("awaiting re-baseline") instead of failing, so
  an integration branch stays green mid-rework.
- Comparison: per-pixel CIE76 ΔE with an explicit domain. Render comparisons decode
  display sRGB; base comparisons interpret samples as linear Rec.2020 before XYZ/Lab.
  Report mean and p99.
- Re-baselining: `HAPPY_PHOTON_UPDATE_GOLDENS=1 dotnet test` regenerates; CI never sets
  it. A golden diff in review must be justified by a `RenderPipeline.Version` bump or a
  spec change in the PR. Each re-baseline records a pre/post attribution report
  (per-image ΔE summary); only the active generation is kept, and superseded versions
  are pruned once the report is captured.
- Settings cases (18 total): **tonal set** — identity; +2 EV; −2 EV; highlights −100;
  shadows +80; contrast +50; full-combo tonal preset. **WB set** — WB 3000 K;
  WB 9000 K tint +50; WB 9000 K tint −50. Never baseline WB cases while the chromatic
  stage is a stub — that would golden "WB ignored" and immediately invalidate itself.
  **Chroma set** — saturation-only, vibrance-only, combined, and active color-mixer
  settings on the reference RAW and Display-P3 fixture (8 cases).

The full-combo tonal case is pinned to exposure +1 EV, brightness +10, contrast +25,
shadows +35, highlights −50, and a monotone curve through `(0,0)`, `(0.25,0.20)`,
`(0.75,0.82)`, `(1,1)`. Chroma, white balance, geometry, and preset identity remain
at defaults.

### 2.1 Asset × case matrix (keeps golden count and runtime bounded)

| Asset | Tonal cases | WB cases |
|-------|-------------|----------|
| Reference Bayer raw (the CR2) | all 7 | all 3 |
| Display-P3 JPEG | all 7 | all 3 |
| NEF, RAF, DNG, AdobeRGB JPEG, sRGB JPEG, 16-bit TIFF | identity, +2 EV | WB 3000 K |
| HEIC | identity (skippable per §6) | — |

The matrix determines the golden count; the tracked files live under the
generation directory named by `ACTIVE_VERSION`, with the chroma set adding its
eight cases on the reference CR2 and the Display-P3 JPEG. The clipped-highlight
case uses the bright water reflection in the reference CR2
([DECODE.md §2.3](DECODE.md#23-why-clip-and-blend-are-the-supported-modes)).
The perceptual-chroma re-baseline left every neutral-chroma case
byte-identical. Each re-baseline keeps its attribution report beside its
goldens (currently `Tests/goldens/v11/CHROMA_ATTRIBUTION.md`, alongside the
carried `R5A_ATTRIBUTION.md`).

## 3. Tolerances (normative)

| Comparison | Bound |
|------------|-------|
| Same base, repeated render, same platform | bit-identical |
| Golden vs current, same platform | mean ΔE ≤ 1.0, p99 ≤ 3.0 |
| Actual preview-base render vs full-base export aligned to the preview size | mean ΔE ≤ 2.0, p99 ≤ 8.0 |
| Edited sRGB vs Display P3 at the Q16 pre-encode boundary | synthetic mean ΔE00 ≤ 0.034; real RAW ≤ 0.053; sharpening off and on |
| Full-decode base vs half-decode base (raw, at common preview size up to 1600px) | mean ΔE ≤ 2.8 (documented gap) |
| P3-tagged vs sRGB-tagged same-picture bases | mean ΔE ≤ 1.5 |
| Cross-platform: win/linux/mac renders of same case | mean ΔE ≤ 2.0 |
| Built-in characterization vs LibRaw Rec.2020 comparator, Bayer/X-Trans Clip/Blend/direct-ABI FBDD | mean ΔE76 ≤ 1.1, p99 ≤ 9.5 |
| Luminance NR at 25/50/100 on high-ISO RAW and HEIC | flat-patch σ drops ≥40% at 50; edge acutance ≥90% through 50 and ≥70% at 100; σ reduction at 100 is ≥1.15× the reduction at 50; max per-pixel ΔCb/ΔCr ≤1 Q16 LSB |

WYSIWYG is calibrated over every active-generation settings case using the actual
preview base and a full-base export, aligning the occasional one-pixel aspect
difference to the preview dimensions. Crossing-on measured a worst mean of 1.87 and
worst p99 of 7.97; crossing-off was bit-identical. The active-chroma RAW cases
measure 0.82/3.54 (S −50), 0.90/4.15 (V −100), and 1.25/5.61 (combined) mean/p99
ΔE76; standard cases stay bit-identical at the common dimension. Each worst
observation rounds up to the next 0.5, producing 2.0/8.0. The separate half/full
base bound covers the decoded sampling gap before tone.

**OS gating policy:** goldens are generated on Linux CI (canonical). Linux uses
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

1. **Tone suites:** `AgxToneEnginePropertyTests`, `AgxToneEngineDerivationTests`,
   `AgxBlenderOracleTests`, `AgxLookGateTests`, `AgxHighlightQualityTests`, and
   `AgxCrossingPerformanceTests` pin the crossing properties, source-derived
   constants, exact-table interpolation, look and highlight gates, and the Blender
   oracle. `ToneLutTests` pins the retained
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
   every non-v3 write plus explicit v2 legacy-lens materialization.
   `CatalogSchemaTests` pins the clean new schema, acceptance of
   harmless extra columns, and actionable startup rejection for missing columns;
   `CatalogPersistenceTests` pins neutral no-write recovery for null, malformed, or
   unsupported rows. Preset tests require explicit/current versions and reject old
   writes.
7. **Export boundary suites:** `ExportMetadataTests` covers OUTPUT.md §5 EXIF copy,
   orientation, GPS strip, stale-thumbnail removal, ICC presence, and subsampling.
   `TiffExportTests` pins Q16 decode-back parity in both color spaces, 16-bit ZIP,
   exact profiles, and RGB-only output.
8. **Loader suites:** `RawBaseLoaderTests` and `StandardBaseLoaderTests` cover the
   DECODE.md §7 items, including HEIC routing to the platform reader rather than
   LibRaw.
9. **Optics suites:** `LensPrescriptionReaderTests` pins generated DNG opcode payloads,
   mandatory rejection, crop/trim coordinates, and the committed X30 RAF table alarm;
   `LensCorrectionProcessorTests` pins one-pass sampling and scene-linear radial gain;
   `LensSettingsTests` and headless `LensControlTests` pin baseline provenance,
   transfer/cache registration, and constant-layout capability gating.
10. **`RenderDetailTests`**: chroma NR preserves luma and alpha; a seeded noise image
    rendered as one band and as forced non-divisible bands is bit-identical at box
    radii 1 and 3. **`RenderNoiseReductionTests`** pins zero-access identity, native
    scale mapping, seeded-noise reduction, gamut-boundary chroma/alpha preservation,
    single/multiple-band identity, and resting cancellation. Pipeline composition
    tests place luminance NR before capture sharpen on both execution paths.
11. **Working-space suites:** `RawWorkingSpaceTests` proves the built-in
    characterization against the LibRaw Rec.2020 comparator, pins the `cam_xyz`
    semantic oracle under `LibRawOutputConfiguration.LinearCameraNative`
    (`output_color` 0);
    `StandardWorkingSpaceTests` checks the external sRGB-profile target, native P3
    gamut vectors, the thumbnail sRGB-proxy limit, and the one-code JPEG identity gate.
12. **RAW histogram suites:** synthetic Bayer/X-Trans geometry, black-level, sRGB-bin,
    clipping, spatial source-saturation predicate parity, lookup-cap, and cancellation
    cases; six-fixture typed-frame oracle parity; loader fault plus decode-setting/profile
    mask-and-stat invariance; generation-matched lease analysis, full-load no-sampling,
    refresh identity; and headless preferred/effective plus 16-photosite presentation
    boundaries.
13. **Waveform suites:** pure accumulator tests pin the 256×128 grid, column mapping,
    level boundaries, Rec.601 parity, narrow-source back-fill, histogram-bin invariance,
    and the production overflow bound. Painter/view tests pin square-root normalization,
    opaque premultiplied BGRA, theme-token colors, bitmap reuse/disposal and live-theme
    repaint. Headless tests cover the stable three-entry selector, RAW fallback,
    Browse's fixed chrome, cloud-only no-load behavior, and same-image supersession.
    Cached-preview ordering tests additionally hold source work before profile/base
    acquisition and require matching bitmap/scopes with zero source reads or renders;
    background-activity tests hold that gate through the status hysteresis and first
    coherent render.
14. **Wide-gamut output suites:** `WideGamutExportTests` checks the Display P3 profile,
    independently derived native-P3 codes in every format, gamut survival, and common-space
    agreement. `WideGamutColorimetryTests` gates the Q16 finalization boundary at the
    §3 limits with output sharpening Off and Screen, and records the expected 8-bit
    quantization floor as informational. The former frozen per-RID export
    byte hashes are retired; the active goldens own regression.
15. **DCP suites:** `DcpProfileReaderTests` is the §7.2 conformance and hostile-input
    suite; `DcpMatrixAndHueSatTests` covers balanced-seam composition, as-shot dual
    interpolation, 2.5D/single/dual tables, V-only encoding, and seeded direct/LUT
    agreement. `DcpProfileDiscoveryTests` covers all local sources, precedence,
    normalization, refresh, corrupt/oversized/sparse input, and no-read availability
    gates. `DcpAtomicityTests` drives an A→B→C newest-wins replacement through cache
    persistence. Settings, preset, catalog, export-warning, ViewModel, hash, and
    determinism suites cover the integrated boundaries. `DcpColorCheckerAnchorTests`
    applies a generated synthetic profile to the D300 ground-truth fixture without a
    skip path. No Adobe profile is committed.
16. **Perceptual-chroma suites:** `OklabColorDerivationTests` independently derives
    the composed matrices and consumes oracle vectors; `OklabColorPropertyTests`
    pins factor, invariance, taper, skin window, maximal-ray projection, mixer
    partition/periodicity, achromatic reliability, uniform/global equivalence,
    luminance bounds, and post-edit projection semantics;
    `RenderChromaStageTests` covers single-final-Q16 precision, bounded-band parity,
    identity skip, both tone regimes, resting execution, and alpha preservation.
    `PerceptualChromaExportTests` reads active RAW/standard variants back through the
    real export service for both output targets.

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
   OKLab adds Ottosson's XYZ↔LMS and LMS'↔Lab authorities; tests independently
   compose them with a BT.2020 matrix derived from the published primaries.
3. **Independent oracle.** `Tests/assets/color-science-oracle.json` contains linear
   sRGB/D65, linear Rec.2020/D65, and linear ROMM/D50 RGB↔XYZ matrices and round trips,
   Bradford D50↔D65 adaptation, a synthetic camera→sRGB characterization matrix and
   transformed RGB vectors, DCP ProPhoto crossing vectors, sRGB EOTF vectors, and the pre-November-2014
   ColorChecker values for the CIE 1931 2° observer, plus Rec.2020↔OKLab/OKLCh and
   constant-L/h gamut-projection vectors. It is emitted by the dev-only
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

The manifest uses the precommitted "worst supported-RID observation rounded up to the
next 0.5" rule for both anchors. The pre-crossing characterization anchor retains
mean/max ΔE00 bounds 3.0/6.5; the integrated AgX look anchor retains 6.0/14.0.
The recorded win-x64, linux-x64, and osx-arm64 observations agree at the 1e-5 level
and all hold the retained bounds. These budgets pin characterization and look drift;
they are not claims that an aged physical chart should be rendered colorimetrically
exact.

The DCP anchor constructs an independent, non-copyrighted profile whose ForwardMatrix
reproduces the established D300 built-in seam, parses the actual generated bytes, and
runs the same non-skipping physical measurement with frozen mean/max ΔE00 bounds
3.0/6.5. The balanced-neutral CC/AB → D50 white test remains the matrix correctness
anchor. Dev-only `scripts/SyntheticDcpGenerator.csproj` generates valid, malformed,
and dual-table fixtures without checking binaries into the repository.

### 4.2 Perceptual-chroma look gate

`PerceptualChromaLookGateTests` renders a test-only frozen legacy Modulate reference
beside the production OKLCh result from the same upstream pixels. It covers every
canonical golden fixture (including TIFF and HEIC under the normal codec skip policy)
at S/V −100, −50, +50, and +100 plus two combined cases. A sentinel prevents an
accidental shared implementation from erasing the A/B difference. The sheet also joins
the D300 manifest geometry and patch mapping with oracle labels/reference Lab values to
show skin and neighboring non-skin crops under positive and negative vibrance.

```powershell
$env:HAPPY_PHOTON_CHROMA_LOOKGATE='1'
$env:HAPPY_PHOTON_CHROMA_LOOKGATE_DIR='artifacts/perceptual-chroma-lookgate'
dotnet test Tests/HappyPhoton.Tests.csproj -c Release --no-build `
  --filter FullyQualifiedName~PerceptualChromaLookGateTests
```

The generated `index.html` is the maintainer product checkpoint; approval is required
before merge and the artifacts are not a numeric-oracle substitute.

`ColorMixerLookGateTests` generates the D300 ColorChecker identity plus one
Saturation +80 render for each mixer band. The matching headless mixer UI gate writes
Dark and Middle Gray screenshots with the mockup values, so swatch spacing, touched
state, gradients, and the four-row treatment can be reviewed together:

```powershell
$env:HAPPY_PHOTON_MIXER_LOOKGATE='1'
$env:HAPPY_PHOTON_MIXER_LOOKGATE_DIR='artifacts/color-mixer-lookgate'
dotnet test Tests/HappyPhoton.Tests.csproj -c Release --no-build `
  --filter FullyQualifiedName~ColorMixerLookGateTests
dotnet test HeadlessTests/HappyPhoton.Headless.Tests.csproj -c Release --no-build `
  --filter FullyQualifiedName~EffectsControlTests
```

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

The same integrated gate includes an active mixer with global chroma for every slider
fixture, a projection-heavy Canon S=+100 active-mixer endpoint, and active-mixer
full-resolution three-variant RAW
exports for both targets. Active-minus-neutral private peak must stay within the same
+16 MiB ceiling. `PerceptualChromaPerformanceTests` separately measures the complete
pooled-band active-mixer pass, including pixel-cache traffic, against a same-fixture AgX crossing
comparator and enforces the 60 ms class; the neutral identity test proves no pixel
access instead of timing an empty call.

`DcpPerformanceGateTests` runs separately with `HAPPY_PHOTON_R5B_PERF=1` against the
20 MP Canon 6D fixture. It records cold external, cold embedded, and warm selected-
profile decode deltas (≤ 50/30/15 ms), incremental matrix-kernel delta (≤ 10 ms),
HueSat-only preview/full deltas (≤ 80/250 ms and exactly zero inactive), the standing
150 ms slider gate, deterministic managed and retained deltas (each ≤ 8 MiB), active
export +5%/+16 MiB, and cold/warm scans over a fixed 4,000-profile synthetic Adobe
tree (≤ 1.5 s cold, ≤ 0.3 s warm). Run it in a fresh Release process:

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
≤ 150 ms slider budgets. Heap and thread-pool residue from earlier tests in
the class can inflate a later test's medians past budget, so the canonical run
executes each test in its own Release process; apply the same
one-test-per-process rule to any new `HAPPY_PHOTON_PERF` class with latency
budgets, and never loosen a budget to make a single-process class run pass:

The luminance-NR gate uses Canon RAW, Fuji RAW, JPEG, and the committed high-ISO
iPhone HEIC at 1600px, values 50 and 100, with five alternating paired
neutral/active samples in a Release process. Each total is ≤150 ms and each
active-minus-neutral median delta is ≤20 ms. A stage-only diagnostic separately
warms and measures 15 iterations at the representative two-, three-, and four-scale
preview shapes, reporting median latency and peak private-memory delta for each.
`RenderDetailPerformanceTests.FullResolutionLuminanceNr100_MeetsLatencyAndMemoryGate`
uses a 5472×3648 Q16 diagnostic and requires ≤410 ms and ≤150 MiB peak private-memory
delta. The export gate at value 50 retains the standing ≤max(5%, 500 ms) wall delta
per full-resolution render: 1,500 ms across the three-variant RAW export and 500 ms
for the standard export, both from five alternating paired samples per arm.

`AdjacentPreviewPerformanceTests` drives the real `SelectedImage` cached/fresh race
for copied JPEG and RAW fixtures. It compares warm and disabled adjacent paints,
selects a different uncached image while a warm is active, samples private memory,
and checks activity, decode uniqueness, and single-pair retention. Run it alone:

```powershell
$env:HAPPY_PHOTON_PERF='1'
dotnet test Tests/HappyPhoton.Tests.csproj -c Release --no-build --no-restore `
  --filter FullyQualifiedName~AdjacentPreviewPerformanceTests `
  --logger "console;verbosity=detailed"
```

`WaveformTickPerformanceTests.WaveformActiveSliderTickLatency_WhenEnabled`
drives `ApplyEditsToPreviewArtifactsAsync` over the Display P3 JPEG fixture and
asserts waveform presence for the active measurement and absence for its paired
histogram-only measurement. Base `4e2b350` measured 49.2/52.0 ms total and
46.0/48.5 ms histogram-only in separate Release processes (3.2/3.5 ms deltas).
The merge-candidate gate is total ≤ 57.0 ms, active-minus-histogram ≤ 8.5 ms,
and the standing total ≤ 150 ms. Run it in its own process:

```powershell
$env:HAPPY_PHOTON_PERF='1'
dotnet test Tests/HappyPhoton.Tests.csproj -c Release --no-build --no-restore `
  --filter "FullyQualifiedName~WaveformTickPerformanceTests.WaveformActiveSliderTickLatency_WhenEnabled" `
  --logger "console;verbosity=detailed"
```

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
                  'BrowseHistogramLatency') {
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

RAW preview-base decode performance output includes a `RawHistogram` step for the
sensor pass; full/export decode has no such step. Measure at least the 20 MP Canon EOS
6D fixture when this gate is enabled.

The isolated characterization import gate
(`CameraRgbCharacterizationPerformanceTests`) uses that 20 MP Canon in camera-native
output mode, one warm-up plus five samples. It compares direct Q16 import with fused
characterization of the same native span. The budgets are ≤ 45 ms preview,
≤ 150 ms full, and a ≤ 4 MiB deterministic retained private-memory delta;
async-sampled peaks are informational because native-allocator private bytes are not
reproducible.

```powershell
$env:HAPPY_PHOTON_R5A_PERF='1'
dotnet test Tests/HappyPhoton.Tests.csproj -c Release --filter `
  FullyQualifiedName~CameraRgbCharacterizationPerformanceTests
```

The direct full-resolution detail diagnostic warms the optimized kernels, then uses a
5472×3648 synthetic image and reports elapsed time and peak private-memory delta:

```bash
HAPPY_PHOTON_PERF=1 dotnet test Tests/HappyPhoton.Tests.csproj -c Release --filter RenderDetailPerformanceTests
```

The luminance-NR quality gate renders the ISO 6400 Canon and iPhone HEIC fixtures at
0/25/50/100 and reports flat-patch σ, edge acutance, and maximum per-pixel Cb/Cr delta:

```powershell
$env:HAPPY_PHOTON_NR_QUALITY='1'
dotnet test Tests/HappyPhoton.Tests.csproj -c Release --filter LuminanceNoiseReductionQualityTests
```

It is explicit opt-in because the RAW arm requires a full decode. The committed HEIC
hash and provenance are pinned by `PipelineTestAssetTests` and `Tests/assets/README.md`.

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

The PRECISION investigation's diagnostic harness (ramp goldens and the boundary
census) was retired after the investigation closed with the Q16-storage decision;
it lives in git history. The live color-science suites (`PrecisionDeltaE`,
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
discovery before execution with a floor, not exact counts: at least 666 ordinary
listed cases and 35 headless listed cases. Run tests with a 90-second blame
hang timeout while changing either host.

Golden assets and baselines must keep the repo clone under control — if the goldens
directory exceeds ~20 MB, shrink render size before reaching for LFS.
