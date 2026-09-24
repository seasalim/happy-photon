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
`ShowcaseTestHelper` saves named scenes to `artifacts/shots/<scene>.png`; tests assert
frame dimensions, while reviewers assess the image.

Platform/codec gaps use reasoned xUnit runtime skips. `scripts/check-test-quarantine.ps1`
owns discovery floors and result reconciliation; `scripts/verify.ps1` enforces them.
Use a blame-hang timeout when changing hosts. Asset/golden size limits are enforced by
`GoldenHarnessTests.AssetAndGoldenBudgets_AreWithinSpec`, not copied into this guide.
