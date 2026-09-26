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

**Registration** (nine 49×49 green-channel NCC patches, ±4 px search refined to
0.025 px; threshold ≤ 0.5 full-size px). Canon (no applicable prescription) is stable
at ≤ 0.23 px with and without optics. The Fuji X30 half decode (optics arm) sits at the
boundary: 0.32–0.58 px uncorrected across four runs, ≤ 0.40 px after a +0.25 px
horizontal source-phase correction (preview u′ = u − 0.25/fullWidth). That correction
is a candidate to validate against decoder framing before HEAL-WP2, not a production
fix. Quarter turns, horizon, keystone and crop round-trip base points within 1e−9 px.

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
HEAL-WP2 to WP4 are not blocked by this spike; the Fuji registration correction needs
validation before WP2.

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
