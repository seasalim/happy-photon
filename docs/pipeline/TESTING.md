# Pipeline Spec — Testing: Goldens, Assets, Tolerances

The pipeline is only safe to change because of this harness. Unit tests follow
existing conventions (`Tests/*Tests.cs`, xUnit, `./scripts/verify.ps1`).
Use `Tests/TemporaryDirectory.cs` for disposable test roots and
`TestEditSettingsFactory` in `Tests/TestBaseLoaders.cs` for neutral tonal settings;
keep every non-default setting explicit at the call site.

Ordinary `dotnet test` runs use `HappyPhoton.runsettings`, which gives each test
host two logical processors. This bounds xUnit collection concurrency, managed
pixel workers, and the shared Magick/LibRaw OpenMP budget together. Each test
assembly sets the budget before native loading, including `MAGICK_THREAD_LIMIT`,
so the default host uses two native workers. Explicit environment limits win. Opt-in performance
measurements bypass that cap in a fresh process with
`HAPPY_PHOTON_FULL_CPU=1`, which switches to `HappyPhoton.FullCpu.runsettings`:
the same quarantine filter without the processor cap.

## 1. Sample assets (`Tests/assets/`)

Assets are committed without LFS.
`GoldenHarnessTests.AssetAndGoldenBudgets_AreWithinSpec` owns total/per-file asset and
golden budgets; [Tests/assets/README.md](../../Tests/assets/README.md) owns per-file
provenance and licenses. Prefer small CC0 fixtures.

| Asset | Purpose | Source |
|-------|---------|--------|
| Small Bayer raw ×2 (e.g. Canon CR2 + Nikon NEF, ≤ 15 MB ea. — prefer small-sensor bodies) | decode, WB, goldens | raw.pixls.us, CC0 only |
| X-Trans RAF | Fuji path | raw.pixls.us CC0 |
| DNG | Adobe container path | raw.pixls.us CC0 |
| High-ISO Bayer raw | luminance-NR quality/runtime tuning | CC0 research dataset |
| High-ISO iPhone HEIC (≤ 8.5 MiB) | standard-source luminance-NR tuning | author capture, CC0 exception |
| Nikon D300 ColorChecker NEF | physical colorimetric ground truth | author capture, CC0 exception |
| sRGB JPEG with EXIF+GPS+orientation 6 | metadata policy, orientation | author with exiftool from a CC0 photo |
| Display-P3 JPEG of the same picture as an sRGB JPEG | ICC normalize sentinel — sRGB-derived content, so it cannot show gamut preservation | generate via Magick from a CC0 source |
| Display P3 ICC profile (`DisplayP3-v4.icc`) | independent source profile for the wide-gamut normalization test | Compact ICC Profiles, CC0 |
| AdobeRGB JPEG | second ICC case | generate |
| 16-bit TIFF | depth preservation | generate |
| HEIC | bundled codec; skip when Magick reports no read support | generate/CC0 |
| Synthetic gradient PNG (0→1 ramp, generated in-test) | LUT banding, monotonicity | code |

Raw-file burst pair: two consecutive CC0 frames of a similar scene if obtainable; else
duplicate one raw byte-for-byte under two names (sufficient for determinism testing).

### 1.1 Opt-in modern-camera compatibility fixtures

`Tests/compatibility-fixtures.json` owns provenance, license, exact length, SHA-256,
selection lifecycle and reviewed expectations. Downloaded RAWs remain in ignored
`artifacts/compatibility-fixtures/`; tests never use the network.

```powershell
dotnet run --file scripts/fetch-compatibility-fixtures.cs
dotnet run --file scripts/fetch-compatibility-fixtures.cs -- sony-a9m3-lossy
```

A mismatched cached length/hash fails closed and is never replaced automatically.
Selected candidates run only in discovery; reviewed entries require typed expectations.
The Leica M Monochrom (Typ 246) entry pins `M2462362.DNG`, a one-channel sensor and
absent camera-color facts. Preview/full renders and PNG/TIFF exports must preserve exact
equal channels despite dormant color settings. Local non-manifest files are not
fixtures. The local Q2 MONO preview-base gate remains manual (no automated test): ≤2.5 s
and ≤600 MiB peak private-memory delta.

`HAPPY_PHOTON_COMPAT` has four states:

| Value | Behavior |
|-------|----------|
| unset | One compatibility fact skips before loading the manifest or touching a fixture. |
| `1` | Runs reviewed fixtures; missing files produce named skip terminals and a fetch instruction. |
| `discovery` | Requires every selected fixture and valid hash, records candidate and reviewed application-path observations, and never changes expectations. |
| `strict` | Rejects pending/candidate entries and fails for a missing, invalid, or behaviorally different reviewed fixture. |

Other non-empty values fail. The Windows x64 harness uses the ordinary Avalonia/WIC host;
it checks metadata, thumbnail, bases, camera facts, renders, export and disposal, with
one terminal per selected fixture and ignored reports/review images under
`artifacts/compatibility-results/`. Nikon Z8 High Efficiency permits metadata and
embedded previews but remains unsupported for developed preview/full/export bases.

## 2. Golden mechanism

Goldens live in `Tests/goldens/v<RenderPipeline.Version>/` as
`<asset>__<settings-case>.png`, rendered at long edge 500.
`Tests/goldens/ACTIVE_VERSION` selects the generation; `pending` produces an explicit
awaiting-rebaseline skip. Comparison reports mean/p99 CIE76 ΔE: render samples decode
display sRGB, while base samples are linear Rec.2020 before XYZ/Lab conversion.

Set `HAPPY_PHOTON_UPDATE_GOLDENS=1` to regenerate; CI never does. A golden change needs
a render-version increment or an approved spec change and a pre/post attribution report.
Keep only the active generation after capturing attribution; budgets remain test-owned.
If the golden budget fails, shrink render size before adopting LFS.

`GoldenTestCases` defines 15 settings cases: seven tonal (identity, ±2 EV,
Highlights −100, Shadows +80, Contrast +50, full-combo), three WB (3000 K and
9000 K with tint ±50), and five chroma (saturation, vibrance, combined, mixer, Chroma NR).
The matrix yields 49 renders; exact parameter values live in that class.

### 2.1 Asset × case matrix (keeps golden count and runtime bounded)

| Asset | Tonal cases | WB cases |
|-------|-------------|----------|
| Reference Bayer raw (the CR2) | all 7 | all 3 |
| Display-P3 JPEG | all 7 | all 3 |
| NEF, RAF, DNG, AdobeRGB JPEG, sRGB JPEG, 16-bit TIFF | identity, +2 EV | WB 3000 K |
| HEIC | identity (skippable per §6) | — |

The five chroma cases additionally run on the reference CR2 and Display-P3 JPEG.
The clipped-highlight fixture is documented in DECODE.md §2.3.

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

Calibrate by rounding the worst supported observation up to the next 0.5.

Linux CI generates the canonical goldens. Linux uses same-platform bounds; Windows
and Apple Silicon macOS use the cross-platform bound. HEIC alone may skip when
Magick's bundled codec reports no read support, always with an explicit reason.

X-Trans decodes are not byte-comparable across fresh processes with uncontrolled
OpenMP (DECODE.md §2.6). The bit-identical requirement covers repeated renders of one
base; fresh X-Trans decodes use the tolerance gate.

## 4. Required suites

| Suites | Contract pinned |
|---|---|
| `AgxToneEnginePropertyTests`, `AgxToneEngineDerivationTests`, `AgxBlenderOracleTests`, `AgxLookGateTests`, `AgxHighlightQualityTests`, `ToneLutTests` | Tone formulas, oracle, look, interpolation and monotonicity |
| `WhiteBalanceModelTests` | WHITE_BALANCE.md §9 |
| `RenderDeterminismTests` | Repeated renders, bursts and stable hashes |
| `GoldenRenderTests` | Asset/case matrix and actual preview/full-export agreement |
| `WysiwygTests`, `WysiwygCalibrationTests` | Proof, geometry, effects and output-space agreement |
| `EditSettingsJsonTests`, `EditDocumentBoundaryBaselineTests`, `CatalogSchemaTests`, `CatalogPersistenceTests` | Version boundaries, schema and row-local no-write recovery |
| `ExportMetadataTests`, `TiffExportTests` | EXIF/GPS/orientation, encoders, profiles and Q16 TIFF parity |
| `RawBaseLoaderTests`, `StandardBaseLoaderTests` | Decode contract and HEIC routing through Magick |
| `LensPrescriptionReaderTests`, `LensCorrectionProcessorTests`, `LensSettingsTests`, `LensControlTests` | Conservative optics, sampling, settings and capability UI |
| `RenderNoiseReductionTests`, `ChromaNoiseReductionQualityTests` | Identity, native scale, quality, gamut and cancellation |
| `RawWorkingSpaceTests`, `StandardWorkingSpaceTests` | Characterization, source ICC and gamut preservation |
| `RawSensorHistogramTests`, `RawSensorFrameTests` and source-saturation suites | Sensor predicates, artifact alignment, lease generation and no full-load sampling |
| `WaveformAccumulatorTests`, `WaveformPainterTests`, `WaveformScopeUiTests` | Grid/bin mapping, colors, disposal, fallback and cached-outcome ordering |
| `WideGamutExportTests`, `WideGamutColorimetryTests` | Target profiles, gamut survival and Q16 agreement |
| `DcpProfileReaderTests`, `DcpMatrixAndHueSatTests`, `DcpProfileDiscoveryTests`, `DcpAtomicityTests`, `DcpColorCheckerAnchorTests` | Adobe DNG profile conformance, hostile inputs, matrix/table math, discovery, atomicity and ground truth |
| `OklabColorDerivationTests`, `OklabColorPropertyTests`, `RenderChromaStageTests`, `PerceptualChromaExportTests` | Perceptual formulas, mixer windows, precision, identity and exported color |

### 4.1 Pipeline validation anchors

Every pixel-changing stage answers to four independent anchors:

1. **Seeded properties:** `RenderPropertyTests` pins achromatic preservation, post-gain
   middle-grey anchoring; `RenderPipelineToneRegimeTests` pins source-kind dormancy. DCP
   properties compare balanced-neutral matrix round trips and direct versus compiled
   HueSat math.
2. **Source-cited constants:** derive RGB/XYZ matrices from IEC sRGB, ITU-R Rec.2020 and
   ISO ROMM primaries/white points, checking published W3C values and the oracle.
   `RgbColorSpaceMatrices` distinguishes exact primary-derived from published-rounded
   sRGB variants. DCP fixtures independently encode Adobe DNG tags; OKLab derivations
   use Ottosson's published transforms. Formula/provenance authorities are the sibling
   docs.
3. **Independent oracle:** `Tests/assets/color-science-oracle.json` supplies RGB/XYZ,
   Bradford, camera characterization, DCP, EOTF, ColorChecker, OKLab and gamut vectors.
   CI consumes only JSON; the BSD-licensed generator is version-locked:

   ```powershell
   python -m venv .venv-color-oracle
   ./.venv-color-oracle/Scripts/python -m pip install `
     colour-science==0.4.7 numpy==2.4.4
   ./.venv-color-oracle/Scripts/python scripts/generate-color-science-oracle.py
   git diff --exit-code -- Tests/assets/color-science-oracle.json
   ```

4. **Physical ground truth:** the manifest-pinned D300 ColorChecker uses fixed chart
   geometry, analytic picked WB, a frozen least-squares exposure scalar and ICC D50
   adaptation at the pre-crossing seam. A separate default-AgX measurement uses those
   gains without post-look exposure normalization. Fresh observations never feed gains
   or bounds: characterization mean/max ΔE00 stays 3.0/6.5, integrated look 6.0/14.0. A
   generated, independent DCP reproduces the built-in seam under the same 3.0/6.5 gate;
   no Adobe profile is committed. Adobe/DCP behavior also requires manual verification
   at look sign-off; `scripts/SyntheticDcpGenerator.csproj` is the dev-only fixture
   generator.

### 4.2 Perceptual-chroma look gate

`PerceptualChromaLookGateTests` compares a frozen legacy Modulate reference with
production OKLCh across canonical fixtures, signed slider extremes and ColorChecker
crops. `ColorMixerLookGateTests` renders identity plus each band's saturation treatment.

```powershell
$env:HAPPY_PHOTON_CHROMA_LOOKGATE='1'
$env:HAPPY_PHOTON_CHROMA_LOOKGATE_DIR='artifacts/perceptual-chroma-lookgate'
dotnet test Tests/HappyPhoton.Tests.csproj -c Release --no-build `
  --filter FullyQualifiedName~PerceptualChromaLookGateTests
```

```powershell
$env:HAPPY_PHOTON_MIXER_LOOKGATE='1'
$env:HAPPY_PHOTON_MIXER_LOOKGATE_DIR='artifacts/color-mixer-lookgate'
dotnet test Tests/HappyPhoton.Tests.csproj -c Release --no-build `
  --filter FullyQualifiedName~ColorMixerLookGateTests
```

Maintainer approval of the generated review sheets is required before merge; sheets
never replace numeric oracles. The headless filter prefix
`EffectsControlTests.MixerGroup_Showcase` selects
`MixerGroup_ShowcaseRendersInDarkTheme` and `MixerGroup_ShowcaseRendersInMidGrayTheme`,
saving UI evidence under `artifacts/shots/`.

## 5. Performance

Use Release builds and fresh processes for each gate class; where a class has multiple
latency gates, run each test separately. Set `HAPPY_PHOTON_PERF=1` and
`HAPPY_PHOTON_FULL_CPU=1` before launching so the ordinary CPU cap cannot distort
measurements. Never loosen a budget to make a single-process run pass. The [culling
runner](../cull-perf.md) owns frozen workload qualification and comparison. Perf hosts
are JIT-only: early ticks run tier-0 code; `DOTNET_TieredCompilation=0` shows
steady-state cost.

The table names additional environment variables; exact thresholds and sampling
protocols live in the listed code. Report-only diagnostics are not acceptance gates.

| Gate / diagnostic | Additional environment | What it measures or gates |
|---|---|---|
| `AgxPerformanceGateTests` | `HAPPY_PHOTON_AGX_PERF_TARGET=srgb` or `display-p3`; `HAPPY_PHOTON_AGX_PERF_REPORT=<path>` | Integrated preview tick ≤150 ms, export and memory |
| `AgxCrossingPerformanceTests` | None | Fused crossing cost |
| `PerceptualChromaPerformanceTests` | None | Mixer/chroma cost including pixel-cache traffic; identity has no pixel access |
| `DcpPerformanceGateTests` | `HAPPY_PHOTON_R5B_PERF=1` | Profile decode, matrix/HueSat, discovery, allocation and export deltas |
| `RenderNoiseReductionPerformanceTests`, `ChromaNrPreviewPerformanceTests` | None | Full-resolution detail and preview NR latency/memory |
| `WorkingSpaceColorConversionTests` | `MAGICK_MAP_LIMIT=0` for the disk-backed arm | Exact managed-kernel parity, including the disk-cache commit step |
| `RenderSharpeningPreviewGateTests` | None | Fit/zoom feedback, determinism and tick cost; rerun full-resolution `RenderNoiseReductionPerformanceTests` when sharpening changes |
| `AdjacentPreviewPerformanceTests` | None | Warm/cold navigation, overlap, decode uniqueness and retained pairs |
| `WaveformTickPerformanceTests` | None | Scope-active tick and histogram-only delta |
| Effects arms in `PreviewPipelinePerformanceTests` and `ExportPipelinePerformanceTests` | None | Active preview ≤150 ms; export delta ≤max(5%, 500 ms per full render), memory and cancellation |
| `CameraRgbCharacterizationPerformanceTests` | `HAPPY_PHOTON_R5A_PERF=1` | Fused import versus direct import latency/retained memory |
| `RenderStageBreakdownPerformanceTests` | Optional `HAPPY_PHOTON_STAGE_REPORT_DIR` | Report-only stage attribution, allocation and base load |
| `LocalsFusedBaselineTests` | `HAPPY_PHOTON_LOCALS_FIXTURE=raw`, `standard` or `synthetic`; `LOCALS_SAMPLES` | Local color/geometry/ranges, ≤150 ms tick, export delta ≤max(5%, 500 ms) |
| Headless `LocalRangeOverlayGateTests` | `HAPPY_PHOTON_LOCALS_FIXTURE=standard` selects HEIC; otherwise RAW. Run both | Requested mask and hue-picker responsiveness |
| Brush arms of `LocalsFusedBaselineTests` and `LocalRangeOverlayGateTests` | As locals | Production brush: B1/BCap tick ≤150 ms, BCap contended ≤175 ms, export ≤max(5%, 700 ms), mask ≤60 ms, index ≤1 MiB |
| `PlatformRenderGoldens` | None | Platform-specific frozen render sentinels consumed by tests; not a timed gate |
| `scripts/startup-perf.ps1` | Isolated catalog/cache roots | Published first-frame/startup milestones; supports cold-copy and runtime-environment comparisons |

`Tests/RunLocalsBaseline.ps1` selects a local gate/fixture/sample count in an isolated
process. Local correctness is covered by `RenderLocalsTests`, `LocalPersistenceTests`,
`LuminanceRangeTests`, `HueRangeTests`, `LocalRangeMaskTests` and `LocalsViewModelTests`.
Inactive stages are proved by identity/no-access tests, not by timing empty calls.
Locals qualification asserts medians for contended ticks and classification memory,
prints every sample, and fails as invalid when a control median leaves its ±25% validity
band. Frozen agreement bounds live in `LocalsFusedBaselineTests` (range, production and
adversarial agreement); eight-local overlay limits live in `LocalRangeOverlayGateTests`.
`LocalsShowcaseTests` renders the Locals scenes, including eight disabled locals and an
unavailable source, under `artifacts/shots/`.

Brush gates (pinned in LOCALBRUSH BR-WP1, production wiring in BR-WP2) run
`RenderPipeline` and `LocalRangeMaskRenderer` on B1 (one brush, 40 strokes) and BCap (eight range-restricted
brushes, 96 strokes, 3,936 points, just below the 4,000-point document cap). BCap's contended
tick (≤175 ms median) and export delta (≤max(5%, 700 ms)) allowances apply only to
documents with brush locals. Workload seeds, density and controls are unchanged;
points enter the production model at its required 1/16384 quantization. The
LH8 export-delta and requested-mask controls use ±max(25%, 75 ms) and ±max(25%, 3 ms).
`LocalsContractPrototypeTests` holds the independent brush oracle (≤1e-10) and the
persistence, per-document cap and index-size checks. The index is ≤1 MiB and
independent of pixel count. `LocalsBrushCatalogGrowthTests` asserts history at the cap,
using production serialization, and `Tests/RunBrushDeterministic.ps1` runs the
deterministic checks. `LocalBrushPersistenceTests` covers malformed input, caps,
rotation and value/copy semantics. `LocalBrushRenderTests` checks the independent
oracle through `RenderLocals`, crop and frame override, preview/export weight fields,
and exact single-dab/circular-radial pipeline agreement through both tone regimes
and resting rendering, including mixed creation order and untouched pixels.
`LocalBrushMaskTests` covers requested masks with and without range restrictions.

**HEAL-WP1 measurement spike (2026-09-25, Windows, Q16 OpenMP, 24 logical CPUs).**
Test-only: net production LOC **0**; deletes nothing (the prototype and oracle stay as
HEAL-WP3's reference). The owner or spec lane carries the FINAL choice, the relative G1
bound (owner ruling, 2026-09-25), the radius contract and the area limit into HEAL
Design. All numbers below were measured under an exclusive `measure` host lease;
decimal MB is 1,000,000 bytes.

**FINAL heal: Membrane (discrete Poisson boundary interpolation), additive, linear
Rec.2020.** `HealOracle` (scalar doubles, rounded at each spot's Q16 write) and
`HealPrototype` agree within 1 code. Composition follows the spec: creation order,
source and destination snapshots before each spot's write, bilinear source sampling,
opacity × inward smoothstep feather. Clone places translated source pixels.

- *Boundary samples.* 32 fixed angles. Each sample is the exact mean of the clamped
  bilinear reconstruction over an axis-aligned square of half-width **r/3**, centred
  **r(1+√2/3)** from the spot centre: the smallest offset that keeps every square
  outside the disc (it touches at 45°). The footprint is defined in normalized
  coordinates, so the correction field is resolution consistent, and a blemish that
  fills its spot never enters the destination's boundary estimate. The Poisson weights
  use that sampling circle (radius 1 + √2/3 in spot radii), so an affine destination
  is reproduced inside the disc without a seam at its edge. The prototype
  integrates with per-row trapezoid prefixes (`HealBoundaryRows`, O(rows) per sample,
  equal to direct quadrature within 1e-9); the oracle integrates cell by cell.
- *Correction.* Destination-minus-source boundary means, interpolated by normalized
  Poisson weights. For correction m, source s and directional headroom
  h = max(0, (1−s up, s down) − 0.51/65535), m ≤ 7h/8 is unchanged; beyond the knee
  the magnitude is 29h/32 − h²/[1024(m − 27h/32)], sign preserved. The rule is C1,
  moves toward the source and never clamps. It reserves ≥ 3h/32 plus just over half a
  code, so a result never rounds onto 65535 (or 0) unless the source is already there,
  and the result's derivative in s is ≥ 3/32: a 65400–65463 near-ceiling source keeps
  7 distinct codes (65522–65528) and shows no plateau after −2 EV. In-range
  corrections are exact up to the knee; 0.6 + 0.35 lands within 0.001 code.
- *Selection.* G1 eligibility, then equal-weight seam excess (candidates within 5 % of
  the best tie), then repair-only cost. Both ratio candidates fail G1 on RAW. The
  additive candidates tie on seams (Membrane 0.005551, Gaussian 0.005591); Membrane
  is cheaper at every size (repair only, two workers, median of 7, ms):

| Workload | Membrane-Additive | Gaussian-Additive |
|---|---|---|
| S64, RAW 1600×1068 | 50.2 | 76.9 |
| S64, RAW 2748×1835 | 134.6 | 217.2 |
| S64, RAW 5496×3670 | 521.6 | 817.7 |
| SCap (64 discs), RAW 1600×1068 | 925.3 | 1629.6 |

- *Boundary footprint evidence* (`HealContaminationDiagnosticTests`, report only): a
  dark blemish filling a fraction of a 40 px spot on a textured field; mean absolute
  error inside the healed disc, in Q16 codes:

| Blemish fill | Point samples | Ring-centred r/3 | Outward √2/3 (FINAL) |
|---|---|---|---|
| ≤ 0.5r | 414.7 | 389.6 | 379.8 |
| 0.7r | 936.7 | 1187.7 | 892.1 |
| 0.8r | 2265.7 | 3614.8 | 2210.2 |
| 0.9r | 4659.2 | 7501.0 | 4591.3 |

**Spot contract.** Count cap **64**. Stored radius **0.002–0.10** long-edge units; the
effective pixel radius is **min(radius × max(W,H), min(W,H)/2)** in both kernels and
the comparison masks, before the source centre is clamped so the source disc lies
inside the frame. The cap depends only on aspect ratio (resolution independent);
destinations may cross frame edges. Centres map to x = uW − 0.5, y = vH − 0.5.

**Frozen workloads.** `HealWorkloads.S64()`: 48 heal and 16 clone spots, radii
0.005–0.04, feather 0.5, opacity 1, centres quantized to 1/16384, overlaps, sources
outside a centred 3:2 crop; summed area 0.12817698026646362 long-edge². `SCap`: 64
heals at radius 0.10 on a serpentine 11×6 grid (6×11 portrait) with half-area
neighbour overlap; summed area 2.0106192982974695. LH8 is the qualified production
locals control. Fixtures: `canon-eos-6d-iso-6400.cr2` (5496×3670; real half-decode
pair 1600×1068 / 2748×1835) and `iphone-14-pro-iso-1000.heic` (3024×4032; pair
1200×1600 / 2400×3200). Preview bases are never derived from the full decode.

**Registration — FINAL: no phase correction** (owner ruling, 2026-09-26).
The frozen workload uses nine 49×49 green-channel NCC patches, ±4 px search
refined to 0.025 px; bounds are **Bayer ≤ 0.5 full-size px** and
**X-Trans ≤ 0.75 full-size px**. Every decode uses x = uW − 0.5, y = vH − 0.5.
X-Trans half-size decoding is nondeterministic; see
[HEAL_REGISTRATION.md](HEAL_REGISTRATION.md) for decoder evidence and qualification.

Historical WP1 measurements: Canon (no applicable prescription) was stable at
≤ 0.23 px with and without optics. Fuji X30 measured 0.32–0.58 px uncorrected
across four runs, and ≤ 0.40 px with a diagnostic +0.25 full-pixel horizontal
correction (preview u′ = u − 0.25/fullWidth). That candidate was not adopted.
Quarter turns, horizon, keystone and crop round-trip base points within 1e−9 px.

**Gate results** (Membrane-Additive; G3–G6 medians of five fresh processes; G7/G8
three processes; ranges span two full qualification passes where they differ):

| Gate | Control | Workload | Threshold | Outcome |
|---|---|---|---|---|
| G1 RAW 1600×1068 | region 2.283 / 11.249 | 2.390 / 10.026 | ≤ 2.783 / 13.249 | pass |
| G1 RAW 2748×1835 | region 5.148 / 26.462 | 4.060 / 20.038 | ≤ 5.648 / 28.462 | pass |
| G1 HEIC 1200×1600 | region 1.393 / 13.723 | 1.282 / 10.455 | ≤ 1.893 / 15.723 | pass |
| G1 HEIC 2400×3200 | region 0.684 / 8.766 | 0.825 / 8.112 | ≤ 1.184 / 10.766 | pass |
| G2 RAW 5496×3670 | spot-free 0 codes | S64+LH8 0 of 60,510,960 | 0 | pass |
| G3 RAW | 50.25 ms | 79.36 ms (+29.44) | ≤ 150; +≤ 40 | pass |
| G3 HEIC | 38.19 ms | 64.99 ms (+28.12) | ≤ 150; +≤ 40 | pass |
| G4 RAW, unlimited SCap | 116–127 ms | 401–489 ms | ≤ 175 | miss → area limit |
| G4 HEIC, unlimited SCap | 118–120 ms | 453–484 ms | ≤ 175 | miss → area limit |
| G5 RAW warm refinement | Loupe 2.16–2.18 s | 2.88–3.95 s | ≤ 1.5 s | **miss** |
| G6 RAW cold refinement | Loupe 2.16–2.18 s | 4.21–5.43 s | ≤ 2.38–2.39 s | **miss** |
| G7 RAW installed 1:1 | Fit 202.6–213.5 MB | delta 573.0–705.6 MB | ≤ 221.87 MB | **miss** |
| G8 RAW install peak | idle 781.7–919.1 MB | delta 486.6–775.0 MB | ≤ 310.62 MB | **miss** |

One G3 RAW run was invalid (its LH8 control drifted to 65.10 ms, outside the ±25 %
band); the table shows the valid rerun under the same lease conditions. Two-worker
refinement times vary about 27 % between passes (the same code's render stage measured
3.31 s and 2.43 s), consistent with core placement on this hybrid host; both passes miss
G5/G6 by more than 1.3 s.

G1 is the owner's relative bound: repaired-region ΔE (DisplaySrgb mean / nearest-rank
p99) may exceed the same region's spot-free ΔE by at most +0.5 mean and +2.0 p99. The
spot-free whole-image controls fail the spec's original absolute bound on three of four
cases (RAW 1.675/10.015 and 3.754/23.887; HEIC 0.941/10.634 and 0.418/5.618) because
of the half-size decode and scale on these noisy fixtures, not render intent.

**G4 area limit** (pre-approved fallback). Tick medians, ms, five processes per count,
in two passes (A, then B after the final kernel fixes); controls 113–134 ms:

| Discs at r = 0.10 | RAW A / B | HEIC A / B |
|---|---|---|
| 5 | 149.01 / 154.98 | 151.92 / 147.81 |
| **6** | **153.36 / 143.17** | **153.30 / 152.61** |
| 7 | 145.74 / 170.44 | 159.34 / 163.29 |
| 8 | 166.94 / 160.59 | 167.32 / 169.39 |
| 10 | 188.95 / 178.83 | 181.19 / 181.57 |
| 12 | 183.82 / – | 189.83 / – |
| 16 | 216.99 / – | 228.99 / – |
| 32 | 297.63 / – | 293.35 / – |

**Limit: 6 discs, 6π(0.10)² = 0.18849555921538758 long-edge²** (1.47× S64's area): the
largest count that passes both fixtures in both passes by more than the control spread
(≥ 21.6 ms). Seven and eight discs pass nominally in both passes but with as little as
4.6 ms to spare. `HealWorkloads.SCap` admits n discs when n·π(0.10)² ≤ limit, so the
limit must be quoted exactly (or as its generating expression). The per-spot cost is dominated by the boundary
footprint's snapshot box (about 3.3× the disc's box) and its row prefixes, rebuilt for
every spot. A production stage that shares one frame-level prefix across spots
(HEAL-WP3) can requalify SCap and raise the limit; that is additive, not a format change.

**Refinement diagnostics** (report only; one warm refinement per G5/G6 process, S64+LH8,
Export intent; medians, ms):

| Workers | Base copy | Repair | Render | Total |
|---|---|---|---|---|
| 2 (gate policy), pass A | 22 | 481 | 3307 | 3924 |
| 2 (gate policy), pass B | 18 | 345 | 2425 | 2892 |
| 24 (all logical), pass A | 19 | 183 | 523 | 835 |
| 24 (all logical), pass B | 19 | 163 | 491 | 780 |

BGRA extraction (≈ 72 ms) and bitmap construction (≈ 22 ms) are included in the totals.
The two-worker cap explains the G5 miss: unrestricted, a warm refinement is 0.78–0.84 s.

**Retained memory** (report only, after the ungated G7/G8 samples): a blocking,
compacting full GC returns nothing; managed live is 79.2 MB in every process. Of the
581–706 MB installed delta, the full base (121.0 MB), display bitmap (80.7 MB),
`RenderSharpening.Scratch` (32.1 MB; NR and chroma slots 0) and prototype scratch
(29.3 MB) account for 263.1 MB, already 1.30 × w·h·10; the remaining 318–443 MB is
native commitment not returned after the refinement's transient copies are freed, and
it varies between processes.

**40 MP, report only.** X-T50 not fetched; a generated 7752×5178 JPEG (pair 1600×1069 /
3200×2138). Loupe / warm / cold medians **1.209 / 5.442 / 6.193 s**; warm at all logical
processors 1.228 s. Fit **179.6 MB**, refined idle **1368.3 MB** (installed delta
1188.7 MB), install peak **2663.9 MB** (delta 1295.6 MB); prototype scratch 58.1 MB.

**Outcome.** G1–G3 pass; G4 misses unlimited and takes the 6-disc area limit; G5–G8
miss, so **HEAL-WP5 is blocked** and its design, WPs and envelopes return to the owner.
HEAL-WP2 to WP4 are not blocked by this spike; registration is resolved by the
FINAL owner ruling above.

Run `Tests/RunHealGates.ps1 -Gate <name> -Fixture raw|standard` under an exclusive
measurement lease. Names: `G1Candidates`, `G1PerSpotDiagnostic`, `G2ExportParity`,
`Registration`, `G3Tick`, `G4Contention` (`-AreaLimit <area>` or
`-AreaDiscCounts <counts>`), `G5G6Refinement`, `G7G8Memory`, `Record40Mp`,
`RepairCostDiagnostic`. G3/G4 run five fresh processes of five alternating samples and
fail invalid controls (G3 with the LH8 harness's ten warm-up pairs, G4 with three).
G5/G6 run five fresh processes; G7/G8 run in the ordinary Windows/WIC host with the
preview pair, analysis and Fit bitmap retained, sampling through installation while
the previous full bitmap is referenced, without forced GC. The prototype's extra base
copy is included in G3–G8 (conservative; production writes into the pipeline's working
copy). JSON and logs go to `artifacts/heal/`; this section is the durable record.
`HAPPY_PHOTON_XT50_FIXTURE` selects a local opt-in RAF for the 40 MP record.

### HEAL-WP2 frozen controls

Area validation uses the shared Neumaier compensated `RepairArea.Sum` over clamped
radii and admits sums ≤ `Repair.MaximumArea * (1 + 1e-12)`, keeping `MaximumArea`
exactly 0.18849555921538758 long-edge²; the relative tolerance covers rounding only
(below about 5e-13 relative radius change).

The [repairs contract](REPAIRS.md) defines the model, persistence and settings
flows; [HEAL_REGISTRATION.md](HEAL_REGISTRATION.md) records the FINAL no-phase
mapping, per-CFA bounds and X-Trans decoder evidence.

Controls were frozen against production revision
15c1063b5970a915a3b328d5c2891608e321aab8, with test instrumentation only.

**G1 — canonical serialization.** The frozen document set and exact pre-change
UTF-8 outputs live in
[HealSerializationBaseline.jsonl](../../Tests/HealSerializationBaseline.jsonl):
84 named documents covering all 49 asset/settings pairs in GoldenTestCases,
all 16 geometry goldens, RenderSequenceGoldenTests.Settings, and 18 supported
complete JSON settings documents embedded in Tests/HeadlessTests, including nested
preset settings and pipe-delimited migration goldens. The scanner covers raw,
verbatim and concatenated C# strings and JSON files. Unsupported/invalid negative
fixtures have no canonical output and are excluded; runtime-generated mutations
are not standalone fixture documents.

HealSerializationControlTests checks corpus identity and exact canonical bytes;
source formatting and valid v3 migration syntax are not differences. There is no
baseline update switch. The observed control was **0 differing bytes** (three
runs: 0, 0, 0); the acceptance threshold remains **0**.

**G2 — history payload.** The frozen exposure control is **32,938 bytes** across
65 persisted rows (Original plus 64 commits), position −1 → 64. It fell below the
original 40–250 KB envelope; the owner explicitly accepted it on 2026-09-26.
The S64 acceptance threshold is **432,938 bytes**: control + 400,000 bytes
(decimal KB). Never recompute the control to change this allowance.

Starting catalog: a newly initialized CatalogService in a unique temporary
directory, one synthetic.cr2 path registered with GetOrCreateImageAsync, no actual
photo file, default EditSettings, zero edit_history rows and position −1. No source
content or availability probe is needed. S64 uses this same initial state.

Frozen exposure script: for integer i = 1..64 in increasing order, assign
Exposure = i / 16d (+0.0625 through +4.0 EV), with all other settings at defaults.
Each step clones the previous state, calls EditHistory.PrepareAppend, awaits
CatalogService.SaveEditSettingsWithHistoryAsync, then calls EditHistory.Publish.
These are Develop's production history APIs and order. Labels are derived by
production code; the first commit also persists Original. S64 reuses
HealHistoryWorkload.RunAsync, replacing only the edit callback with one appended
spot per step.

Accounting: after minus before of the persisted UTF-8 byte lengths of label plus
settings_json over this image's edit_history rows, including Original. The SQL
sums length(CAST(label AS BLOB)) + length(CAST(settings_json AS BLOB)); it never
reserializes rows or estimates bytes from managed strings. SQLite row/index/page
overhead and images.edit_settings are excluded. Secondary catalog.db growth was
36,864 → 77,824 bytes (**40,960 bytes**), measured only after a non-busy
wal_checkpoint(TRUNCATE) at each boundary, excluding WAL allocation. This secondary
metric must not replace the history payload metric.

Run Tests/RunHealWp2Controls.ps1 -Gate G1 for serialization. Its -Gate G2 selects
the opt-in historical exposure workload (HEAL_CONTROL=1), not the S64 gate;
ordinary runs skip that control. The runner bounds each invocation to 120 seconds.
RepairFlowTests.S64HistoryGrowthStaysWithinTheFrozenAllowance measures S64 against
the recorded control and threshold above; it does not rerun the exposure control.

**OPS-WP1 measurement spike (2026-09-26, Windows/WIC, Q16 OpenMP, 24 logical CPUs).**
Test-only: net production LOC **0**. Deletes nothing: the prototypes and independent
oracles remain the reference for the operator WPs. Base `0515aaf`; the owner's
2026-09-26 ruling makes G1 relative (operators-off control + 0.5 mean / + 2.0 p99). All
numbers below were measured under an exclusive `measure` host lease.

**Outcome.** Texture and guided Clarity pass every gate; Gaussian Clarity fails the halo
gate. Dehaze (with the pre-approved edge-aware refinement) passes G4, G5 and G7 but
misses G1 for positive amounts, G2 on RAW, G3 and G6, so the combined OP stack misses
G1, G2 (RAW) and G3. Per the spec, the Dehaze design returns to the owner; no formulation
is recorded FINAL here. Texture and guided Clarity are the candidates for the owner's
look review (sheets below). OPS-WP2 and OPS-WP6 do not depend on this spike.

**Candidate constants (frozen for reproduction, not approved production choices).**
`OpsPresenceOracle` uses scalar doubles, direct two-dimensional convolution and
source-pixel area scattering. `OpsPresencePrototype` uses separable filtering,
cell gathering and one working-size float plane. `OpsDehazeOracle` independently
builds the lattice, airlight, interpolation and signed correction; its tone side
uses the existing scalar tone formulas. Synthetic contracts require max error
≤1 Q16 code; workers and bands must agree exactly.

- Texture: B3 taps `[1,4,6,4,1]/16`, à trous dilations **1, 2, 4 native pixels**.
  The sum of these three bands is original minus the composed low-pass. Its
  separable support is **±14 native pixels**, scaled continuously by render/native
  long edge, with fractional taps distributed to adjacent pixels. Clamp extension
  is applied to the composed kernel; the summed band has soft threshold **0.01**.
  There is no intent floor. Signed slider/100 multiplies the thresholded band.
- Both Clarity candidates reduce luma by exact fractional area to at most **256**
  cells on the long edge, preserving aspect, then bilinearly reconstruct at pixel
  centres. Gaussian σ is **0.010 long edge**, truncated at 3σ. The self-guided
  candidate uses box radius `ceil(sqrt(3) * sigma)` (5 at a 256-cell edge), variance
  regularizer **0.0025**, and box-averaged affine coefficients. Both limit the luma
  band to **±0.04**, weight it by **4Y(1−Y)** and multiply by signed slider/100.
  Texture and Clarity deltas sum before the NR-style equal-channel gamut clamp.
- Presence allocates at most one working-size float plane plus the small grids.
  The Texture horizontal smooth occupies that plane; the Q16 working array
  supplies original luma. There is no full-size mask, RGB-float image or row ring.
  Default bands are **32 rows**; the one-row limit and worker caps 1/2/all execute
  the same reductions. Cancellation is checked at each band and before writeback.
- Dehaze: **48 cells** on the long edge (RAW 48×32; HEIC 36×48), exact area
  averages from the pre-geometry base after WB. Airlight is a normalized weighted
  mean, with weight `max(0,min(R,G,B))^8 + 1e-12`, floored per channel at 1/65535.
  Cell transmission is `clamp(1−0.95*min(R/A_R,G/A_G,B/A_B),0.1,1)`.
  Positive amounts divide the signed distance from airlight by
  `max(0.1,1−abs(amount)/100*(1−t))`; negative amounts multiply by that factor.
  Lattice-only reconstruction is bilinear. Its G5 miss activates the approved
  fallback: joint bilateral reconstruction of the four adjacent cells, guided by
  the pixel's WB luma, with Gaussian variance **0.0025** and weight floor **1e−30**.
- Amount fields: two signed Int16 planes, **100 codes per slider unit**, summing
  each enabled local's final geometry × luminance-window × hue-window weight.
  Clamp the complete local sum to **±200**, then quantize. The presence stage adds
  field/10000 to global slider/100. No fields are allocated when inactive.
  `OpsKernelTests` independently exposes production weights through a +1 EV local;
  `OpsContractTests` covers overlap saturation and full-frame local/global identity.

**Stage placement and controls.** Presence uses production display-Rec.2020 with
capture sharpen set to zero, applies the test stage after production NR, then calls
production capture sharpen with the original detail settings and intent. Dehaze
uses a test-only WB → signed affine correction → existing extended tone seam;
negative and above-one corrections never round-trip through an unsigned base.
It rejects crop/geometry and DCP HueSat, which are absent from these whole-frame
workloads. Its operators-off arm must equal production at all three real base
sizes before a runner invocation collects gates. The independent oracle also
covers ±50/±100, nonidentity WB, −2 EV and out-of-range corrections.

G1 uses actual loader pairs: RAW **1600×1068 / 2748×1835**, from the real half decode,
and HEIC **1200×1600 / 2400×3200**. Full bases are **5496×3670 / 3024×4032**.
No real preview is constructed from the full decode. G2/G4 use the production
export service, full decode, JPEG quality 85 and its real variants (RAW: full,
2048 and 1024 with Screen output sharpen; HEIC: full, output sharpen Off).
The hook substitutes only the test upstream. G3 controls are the production NL
request with a fresh pipeline, 1600 max dimension and stats/overlays off.
The on arm uses that same output arrangement. All extra prototype pixel-cache
copies are charged to the on arm; none are subtracted. G2 also charges lattice
construction. After R1-2, G3 builds analysis once per process before warm-ups and
timing, reuses it for DH± and OP, and reports the build time as `G3-lattice-build`.
This models the fixed base/repairs/WB of a slider tick; it is not a production cache.
Each materialized RGB Q16 array is 6wh bytes, in addition to the 4wh
presence float plane and, for LPP8, 4wh amount fields. Actual private commitment,
including native copies and GC timing, is measured rather than inferred from these
array sizes.

**Frozen synthetic fixtures.** Skyline is a vertical linear-gray step **0.08 → 0.65**,
at half width, at **1600×1066** and **5496×3664**. Measure the larger band's width
on either side at the middle row, relative to the far-field value one-quarter of
the frame from the edge; the cutoff is 2% of that render's far-field step.
The off band is exactly zero. Haze uses the full CR2 base and a 1600×1068 reduction
of that same clear base, airlight **[0.65,0.70,0.75]** in linear Rec.2020,
and transmission **0.975 + 0.0125 cos(πu) cos(πv)** at pixel centres (range
0.9625–0.9875). Q16 input construction rounds once. Bright neutral-to-cool airlight
is frozen; only haze strength was calibrated using operators-off renders. No
estimator, transmission rule, operator amount or threshold was retuned.

Construction history (full / 1600 operators-off mean ΔE):

| Attempt | Airlight | Transmission mean / variation | Full / 1600 ΔE | Disposition |
|---|---|---|---|---|
| Original bright, strong haze | [0.65,0.70,0.75] | 0.65 / 0.10 | 60.667 / 60.617 | outside 10–20 construction band |
| Original dark-airlight workaround | [0.04875,0.0525,0.05625] | 0.65 / 0.10 | 15.612 / 15.542 | rejected in R1-1: dark haze does not test the bright-haze prior |
| R1 bright, weaker haze | [0.65,0.70,0.75] | 0.90 / 0.05 | 38.569 / 38.530 | outside band |
| R1 bright, weaker haze | same | 0.95 / 0.025 | 26.317 / 26.327 | outside band |
| **R1 frozen construction** | same | **0.975 / 0.0125** | **15.544 / 15.549** | in band at both sizes |
| R1 construction-only trial | same | 0.98 / 0.01 | 12.717 / 12.673 | in band; not selected |

`HazeConstruction` records the new off-only trials; the 0.975 construction was
selected before any recovery render. Evidence: `artifacts/ops/review-construction.log`.
G5/G6 use Export intent at both synthetic sizes; review sheets use Preview intent,
including its existing capture-sharpen floor. The four sheets were regenerated with
the corrected construction during review turn 1.

**Workloads and protocol.** TX ±60; CL ±60; DH ±50; OP Texture +40, Clarity +40,
Dehaze +30. LPP8 is the existing LH8 geometry, exposure/color and ranges plus local
Texture +30 / Clarity +50 on all eight. G2/G4 take five alternating pairs after
one warm pair; G3 takes five alternating pairs after ten warm pairs in each of
five fresh processes, then gates medians across those processes. G4 samples
private bytes in the ordinary Windows/WIC host without forced GC, recording each
arm's baseline and absolute peak; the increment is the paired absolute-peak
subtraction. An out-of-range control invalidates a result, never changes its bound.
Qualification runs under the exclusive `measure` host lease; an initial pre-lease
skyline diagnostic and superseded row-ring measurements are not used below.

**G1 (mean / p99 ΔE, relative bound).** Real preview pairs (RAW 1600×1068 / 2748×1835
from the half decode; HEIC 1200×1600 / 2400×3200) against the full render downsampled.

| Arm | RAW 1600 | RAW 2748 | HEIC 1200 | HEIC 2400 |
|---|---|---|---|---|
| Off control | 1.675 / 10.015 | 3.754 / 23.887 | 0.941 / 10.634 | 0.418 / 5.618 |
| Bound | 2.175 / 12.015 | 4.254 / 25.887 | 1.441 / 12.634 | 0.918 / 7.618 |
| TX+ | 1.726 / 10.114 | 3.821 / 24.006 | 1.076 / 10.804 | 0.556 / 6.434 |
| TX− | 1.663 / 9.970 | 3.762 / 24.031 | 0.886 / 10.245 | 0.455 / 5.952 |
| CL+ (guided) | 1.701 / 10.115 | 3.786 / 24.036 | 0.956 / 10.559 | 0.426 / 5.551 |
| CL− (guided) | 1.656 / 9.933 | 3.746 / 23.833 | 0.934 / 10.746 | 0.417 / 5.692 |
| DH+ | **1.997 / 13.546** | **4.435 / 31.257** | 1.427 / **14.550** | 0.733 / **9.071** |
| DH− | 1.393 / 6.964 | 3.088 / 16.819 | 0.639 / 6.748 | 0.280 / 3.528 |
| OP | 1.908 / **12.038** | 4.202 / **28.033** | 1.306 / **12.790** | 0.701 / **7.927** |

Bold values exceed the bound. Report-only diagnostic: rendering the preview arm with the
full base's lattice and airlight leaves the DH+ excess essentially unchanged (RAW 1600
1.984 / 13.51 against 1.997 / 13.55; per-base airlights agree within 0.03 %). The miss is
positive Dehaze amplifying the pipeline's existing preview/full gap (it divides the
distance from the airlight by t′), not a per-base analysis disagreement.

**G2 (export, paired ms over no edits; bound max(5 %, 500 ms), OP max(10 %, 900 ms)) and
G3 (1600 tick increment over NL, lattice cached; TX/CL ≤ 25, DH ≤ 20, OP ≤ 60 ms).**
Medians of five pairs (G2) and of five fresh processes (G3); every control in range.

| Arm | G2 RAW | G2 HEIC | G3 RAW | G3 HEIC |
|---|---|---|---|---|
| TX+ | +329 | +207 | +15.8 | +21.2 |
| TX− | +276 | +213 | +14.4 | +21.4 |
| CL+ | +103 | +92 | +18.9 | +22.5 |
| CL− | +102 | +85 | +19.0 | +22.6 |
| DH+ | **+640** | +376 | **+46.7** | **+52.6** |
| DH− | **+674** | +400 | **+46.4** | **+58.5** |
| OP | **+1022** | +633 | **+73.6** | **+88.9** |

Controls: G2 RAW 2226–2246 ms, HEIC 846–869 ms; G3 NL RAW 25.8–27.6 ms, HEIC 18.0–21.0 ms.
Report-only attribution at 1600 (RAW, DH): edge-aware refinement ≈ 28 ms, affine
correction ≈ 10 ms, harness copies ≈ 4 ms; lattice-only correction ≈ 14 ms. The lattice
build from the 1600 base (not charged to G3) is ≈ 400 ms, relevant to OPS-WP4 G2
(first tick after a new base ≤ 150 ms). Not gated here: the LPP8 amount-field prototype
export adds ≈ 1.94 s RAW and 1.23 s HEIC over no edits.

**G4 (paired private-peak delta, same process).** OP RAW +2.3 MB (bound 121.0 MB), HEIC
−0.1 MB (73.2 MB); LPP8 RAW −59.1 MB (201.7 MB), HEIC −69.9 MB (121.9 MB). Controls: RAW
490–572 MB, HEIC 145–306 MB. Pass; per-sample deltas are noisy (GC commitment) and
retained in the JSON.

**G5 (halo width, long-edge units; bound ≤ 0.010; off = 0).** Guided Clarity +100: 0.0031
at 1600 and full. Refined Dehaze +100: 0 at both. Gaussian Clarity: **0.020** (fails, so it
is ineligible). Lattice-only Dehaze: **0.0104–0.0106** (fails, which triggers the
pre-approved refinement).

**G6 (refined Dehaze +50, bright-airlight fixture).** Construction airlight
[0.65, 0.70, 0.75], transmission 0.975 ± 0.0125; hazy baseline 15.54 / 15.55 ΔE (range
10–20). Recovery **25.7 %** (full) and **27.3 %** (1600) against ≥ 40 %; airlight
agreement within 1 %. A first construction with a dark airlight (≈ 0.05) was rejected as
physically implausible haze.

**G7.** OP and LPP8, workers 1/2/24 × band rows 1/32, both fixtures: 0 differing Q16 codes
in all 24 comparisons. Prototype versus oracle: ≤ 1 code on every contract, including
fractional-lattice Dehaze refinement at 419×283 and 283×419 (max error 0 codes, where
refinement changes outputs materially) and negative Dehaze outputs at both tone regimes'
input handling (standard clamps at 0, as production).

Run `Tests/RunOpsGates.ps1 -Gate <name> -Fixture raw|standard -Clarity Gaussian|Guided`
under an exclusive measurement lease; `-Refine` selects the pre-approved DH fallback.
Gate names: `Qualification`, `G1Parity`, `G2Export`, `G3Tick`, `G4Memory`, `G5Halo`,
`G6Haze`, `G7Determinism`, `ReviewSheets`; report-only names are
`HazeConstruction`, `G1SharedAnalysis`, `CostAttribution`. Each invocation first
runs the oracle
contracts and real-fixture off-arm qualification. Stale Release assemblies are
rejected. JSON, TRX and detailed logs are retained in `artifacts/ops/`; JSON pins the
assembly and OPS source hashes. The runner preserves failed observations and exits
nonzero on misses. Review sheets live in `artifacts/ops/sheets/<fixture>/<candidate>/`:
operator off/active full-frame pairs and 1:1 centre crops, at ±60/±100, for both
real fixtures and both frozen synthetic scenes. These are formulation-selection
sheets only; production operator WPs still need their own owner-approved sheets.

**OPS-WP2 qualification (2026-09-27, `3dfd0a2`, Windows/WIC, Q16 OpenMP).**
Production Whites/Blacks results on the Canon EOS 6D RAW and iPhone 14 Pro HEIC
fixtures; paired values below are +60 / −60, with both controls set to that amount.

| Gate | RAW | HEIC | Bound / result |
|---|---:|---:|---|
| G1, 1600 tick increment over NL | +12.6 / +4.6 ms | +8.8 / +7.3 ms | ≤20 ms; active ≤45 ms against ≤150 ms |
| G2, LH8 local Whites +30 / Blacks −20 increment | +2.3 ms | +0.4 ms | ≤10 ms |
| G3, full-export paired delta | +114 / +62 ms | +47 / +37 ms | ≤max(5%, 500 ms), here 500 ms |

G5: 84 frozen HEAL documents + 65 v14 golden renders/settings + 12 legacy
documents, with **0 differing canonical bytes or pixels**.

G4 measured mean / p99 ΔE against full export downsampled to each production
preview size. Operators-off controls remain as measured. The owner's **2026-09-27
option 1 ruling** asserts only −100 against control +0.5 mean / +2.0 p99 at both
sizes on both fixtures. −60, +60 and +100 are report-only under that ruling;
−60 is within the relative bound, while both brightening arms exceed it.

| Fixture and preview size | Operators off | −100 (asserted) | −60 (report-only) | +60 (report-only) | +100 (report-only) |
|---|---:|---:|---:|---:|---:|
| RAW 1600×1068 | 1.675 / 10.015 | 0.852 / 8.332 | 1.061 / 8.855 | 4.392 / 14.518 | 9.428 / 27.552 |
| RAW 2748×1835 | 3.754 / 23.887 | 1.554 / 18.753 | 1.996 / 20.534 | 10.436 / 33.655 | 18.723 / 56.070 |
| HEIC 1200×1600 | 0.941 / 10.634 | 0.939 / 12.139 | 0.903 / 10.864 | 1.417 / 17.256 | 1.910 / 23.619 |
| HEIC 2400×3200 | 0.418 / 5.618 | 0.411 / 5.863 | 0.392 / 5.258 | 0.669 / 9.931 | 0.915 / 14.050 |

The brightening excess amplifies the existing preview/export gap: identical input
with sharpening off is exact; the RAW preview decode exposure offset is 0.495 EV
against 0.732 EV for the full decode. Resize order and RAW preview sharpening add
smaller differences. The maximum +100 pre-tone luminance slope is 19.95 (RAW) and
10.32 (standard). See [DECODE §2](DECODE.md#2-rawbaseloader-libraw-via-the-happy-photon-bridge)
for the half-size preview decode and independent preview resizes, and
[DECODE §2.2](DECODE.md#22-raw-exposure) for the source exposure estimate.

The extended tone table's cold build measured about 10 ms (RAW 9.76, HEIC 9.57),
or 33–35 ms with channel curves (RAW 34.96, HEIC 33.02); the tone-identity cache
reuses it across Whites/Blacks amount changes. The owner approved the production
look from the `3dfd0a2` review sheets on 2026-09-27. The remaining preview/export
gap goes to the spec lane as a separate work package.

### 5.1 Display-reference comparison

`ReferenceComparisonTests` is report-only against external lossless renders.
[Tests/assets/README.md](../../Tests/assets/README.md) owns reference filenames;
`HAPPY_PHOTON_COMPARE_REFERENCE_DIR` replaces the default directory. Missing references
skip explicitly. The harness normalizes to sRGB, downsizes in linear light and solves
exposure against median luminance; assertions cover geometry, ROI discovery,
convergence and finite metrics, not agreement with another editor's look.

```powershell
$env:OMP_NUM_THREADS='1'
$env:HAPPY_PHOTON_COMPARE='1'
# Optional: $env:HAPPY_PHOTON_COMPARE_REFERENCE_DIR='D:\references'
dotnet test Tests/HappyPhoton.Tests.csproj -c Release --filter FullyQualifiedName~ReferenceComparisonTests --logger "console;verbosity=detailed"
```

LibRaw single-file smoke: `./scripts/verify-libraw-single-file.ps1 -RuntimeIdentifier
<RID>` checks package extraction and decode. Working-space review: `dotnet run --file
scripts/evaluate-wide-working-space.cs -- <baseline> artifacts/wide-working-space`;
record look approval outside the numeric gate.

## 6. CI

The three-platform workflow runs ordinary/native bitmap tests in
`Tests/HappyPhoton.Tests.csproj` and dispatcher/UI tests in
`HeadlessTests/HappyPhoton.Headless.Tests.csproj`. Windows WIC stays in the ordinary
host so native and headless Avalonia platforms never share a process.
On Linux and macOS, the ordinary host initializes Skia rendering/font services
without a desktop windowing backend. Set `HAPPY_PHOTON_TEST_SKIA_ONLY=1` to exercise
that fixture path on Windows with a focused view-model test filter; native Windows
bitmap tests still require the default fixture.
`ShowcaseTestHelper` saves named scenes to `artifacts/shots/<scene>.png`; tests assert
frame dimensions, while reviewers assess the image.

Platform/codec gaps use reasoned xUnit runtime skips. `scripts/check-test-quarantine.ps1`
owns discovery floors and result reconciliation; `scripts/verify.ps1` enforces them.
Use a blame-hang timeout when changing hosts. Asset/golden size limits are enforced by
`GoldenHarnessTests.AssetAndGoldenBudgets_AreWithinSpec`, not copied into this guide.
